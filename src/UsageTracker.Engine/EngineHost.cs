using System.Diagnostics;
using UsageTracker.Core.Data;
using UsageTracker.Core.Edge;
using UsageTracker.Core.Model;
using UsageTracker.Core.Native;
using UsageTracker.Core.Normalize;

namespace UsageTracker.Engine;

/// <summary>
/// M1 采集引擎:单线程 STA 循环。
///  前台归属:1s 轮询 GetForegroundWindow;Edge 前台时每 2s 读地址栏域名;
///  生命周期:4s 进程快照差分((PID,CreateTime) 主键);
///  落库:片段/会话关闭时写 SQLite,并折叠 daily_stats;60s checkpoint 防断电丢数据。
/// </summary>
internal static class EngineHost
{
    private static readonly HashSet<string> FgNoiseExe = new(StringComparer.OrdinalIgnoreCase)
    {
        "LogonUI.exe", "lockapp.exe", "consent.exe", "ShellExperienceHost.exe",
    };

    private sealed class FgSeg
    {
        public string App = "";
        public long Pid;
        public long BornMs;
        public long TsFrom;
        public string? Domain;
    }

    private sealed class OpenSession
    {
        public long Pid;
        public long BornMs;
        public string App = "";
        public uint Sid;
        public long Parent;
        public long StartedMs;
    }

    private sealed class ProcRec
    {
        public string FileName = "";
        public string App = "";
        public long BornMs;
        public uint Sid;
        public long Parent;
        public long StartedMs;
        public bool Excluded;
    }

    public static void Run(CancellationToken ct, string dataDir, EngineLog log)
        => Loop(ct, dataDir, log, selfTest: false);

    public static void RunWithSelfTest(CancellationToken ct, string dataDir, EngineLog log)
        => Loop(ct, dataDir, log, selfTest: true);

    private static void Loop(CancellationToken ct, string dataDir, EngineLog log, bool selfTest)
    {
        long selfPid = Environment.ProcessId;
        var db = new UsageDb(AppPaths.DbFile(dataDir));
        db.Open();
        long? hbAtStart = db.LastSeenMs();
        int reconciled = db.ReconcileOpenSessions(TimeUtil.UtcNowMs());
        log.Info($"[引擎启动] 对账封口 {reconciled} 个遗留会话;封口上限=上次心跳 " +
                 (hbAtStart.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(hbAtStart.Value).ToLocalTime().ToString("MM-dd HH:mm:ss") : "无(退回当前时刻)") +
                 $";自检模式={selfTest}");
        db.SetMeta("last_seen", TimeUtil.UtcNowMs().ToString());   // 本次观测起点心跳(必须在对账之后写)
        // 心跳纪元起点:repair-spans 只对“有心跳之后”的同日空档做修边(此前即使无写入也可能是锁屏,不可判定)
        if (db.GetMetaLong("hb_since") == null) db.SetMeta("hb_since", TimeUtil.UtcNowMs().ToString());
        // 用户自定义排除名单(数据目录 tracking-exclude.txt,每行一个 exe 名;改文件即可,不必改代码)
        int uxCount = TrackingPolicy.LoadUserExcluded(Path.Combine(dataDir, "tracking-exclude.txt"));
        if (uxCount > 0) log.Info($"[策略] 已加载用户排除名单 {uxCount} 条(tracking-exclude.txt)");

        long startMs = TimeUtil.UtcNowMs();
        long lastFg = 0, lastLife = 0, lastEdge = 0, lastCheckpoint = startMs;
        // 观测起点:会话起点不得早于它(用户定案 (C)=①:已有进程从“引擎开始观测”算起)。
        // 检测到睡眠/断电墙钟跳变后,它会前移到恢复观测的时刻。
        long obsEpoch = startMs;
        long lastTick = startMs;
        // 墙钟跳变阈值:loop 每 200ms 一拍,超过这个间隔只可能是睡眠/休眠/断电(设计 §3.5、验收 T6)。
        long sleepJumpMs = 90_000;
        if (long.TryParse(Environment.GetEnvironmentVariable("USAGETRACKER_SLEEP_JUMP_MS"), out var sj) && sj >= 5000)
            sleepJumpMs = sj;
        // 每日维护(默认启动 2 分钟后检查;测试可用环境变量缩短)
        long maintDelayMs = 120_000;
        if (long.TryParse(Environment.GetEnvironmentVariable("USAGETRACKER_MAINT_DELAY_MS"), out var md) && md >= 1000)
            maintDelayMs = md;
        string maintDay = "";
        bool childSpawned = false;
        bool sawChildStart = false, sawChildStop = false;
        var pidInfo = new Dictionary<long, ProcRec>();
        var openSessions = new Dictionary<(long Pid, long Ct), OpenSession>();
        var ignoredApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long lastIgnoreRefresh = 0;
        string lastMetaFg = "";
        FgSeg? fg = null;
        int edgeNullStreak = 0;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                long now = TimeUtil.UtcNowMs();

                // ---- 睡眠/断电检测(设计 §3.5):loop 墙钟大跳变 = 这段时间机器不在跑 ----
                // 把前台片段与所有会话都封口到“跳变前”的时刻,然后重新开始观测:
                // 睡眠/休眠/关机期间的墙钟不计入任何时长(验收 T6),恢复后按新观测起点继续续开。
                if (now - lastTick > sleepJumpMs)
                {
                    long before = lastTick;
                    long gapSec = (now - lastTick) / 1000;
                    if (fg != null) { CloseFg(db, fg, before); fg = null; }
                    int cut = 0;
                    foreach (var (key, s) in openSessions.ToList())
                    {
                        openSessions.Remove(key);
                        db.SessionClose(new SessionCloseInfo
                        {
                            Pid = s.Pid, PidBornMs = s.BornMs, AppName = s.App,
                            SessionId = s.Sid, ParentPid = s.Parent,
                            StartedAtMs = s.StartedMs, EndedAtMs = before, ExitKind = 3,
                        });
                        cut++;
                    }
                    pidInfo.Clear();
                    obsEpoch = now;
                    db.SetMeta("last_seen", now.ToString());
                    log.Info($"[睡眠/断电] 墙钟跳变 {gapSec}s:片段与会话已封口到 {DateTimeOffset.FromUnixTimeMilliseconds(before).ToLocalTime():HH:mm:ss}(会话 {cut} 条,exit_kind=3)," +
                             $"恢复观测于 {DateTimeOffset.FromUnixTimeMilliseconds(now).ToLocalTime():HH:mm:ss},该段不计入任何时长");
                }
                lastTick = now;

                // ---- 生命周期 4s ----
                if (now - lastLife >= 4000)
                {
                    lastLife = now;
                    var rows = ProcessSnapshot.Query();
                    var currentKeys = new HashSet<(long, long)>(rows.Count);
                    pidInfo.Clear();
                    foreach (var r in rows)
                    {
                        var key = r.Key;
                        currentKeys.Add(key);
                        if (!pidInfo.ContainsKey(r.Pid))
                        {
                            string file = r.FileName;
                            pidInfo[r.Pid] = new ProcRec
                            {
                                FileName = file,
                                App = TrackingPolicy.DisplayName(file),
                                BornMs = r.CreatedMsUtc,
                                Sid = r.SessionId,
                                Parent = r.ParentPid,
                                StartedMs = r.CreatedMsUtc,
                                Excluded = !TrackingPolicy.ShouldTrack(r, selfPid),
                            };
                        }
                        // 新进程 → 开会话(黑名单应用不再记录)
                        if (r.Pid != selfPid && TrackingPolicy.ShouldTrack(r, selfPid) && !openSessions.ContainsKey(key))
                        {
                            var app = TrackingPolicy.DisplayName(r.FileName);
                            if (ignoredApps.Contains(app)) continue;
                            // 会话起点 = “我们开始观测它的时刻”(用户定案 (C)=①):
                            //   · 引擎启动前就在跑的进程 → obsEpoch(观测起点),不把没观测到的时段(含关机/睡眠)补成运行时长;
                            //   · 正常新启动的进程 → 进程创建时刻(≈观测时刻,更准);
                            //   · 时间戳垃圾(0/1601)或未来时间 → 用当前时刻兜底。
                            // CreateTime 仍完整保留为 pid_born(会话身份键,防 PID 复用串账)。
                            const long SaneMinMs = 946_684_800_000L;   // 2000-01-01 UTC
                            long created = r.CreatedMsUtc;
                            long sessStart = created < SaneMinMs || created > now ? now
                                           : created <= obsEpoch ? obsEpoch
                                           : created;
                            openSessions[key] = new OpenSession
                            {
                                Pid = r.Pid, BornMs = r.CreatedMsUtc, App = app,
                                Sid = r.SessionId, Parent = r.ParentPid, StartedMs = sessStart,
                            };
                            db.SessionOpen(r.Pid, r.CreatedMsUtc, app, r.SessionId, r.ParentPid, sessStart, r.ImagePath);
                            if (selfTest && app == "UsageTracker.Engine" && r.Pid != selfPid) sawChildStart = true;
                        }
                    }
                    // 消失的进程 → 关会话
                    foreach (var (key, s) in openSessions.ToList())
                    {
                        if (!currentKeys.Contains(key))
                        {
                            openSessions.Remove(key);
                            db.SessionClose(new SessionCloseInfo
                            {
                                Pid = s.Pid, PidBornMs = s.BornMs, AppName = s.App,
                                SessionId = s.Sid, ParentPid = s.Parent,
                                StartedAtMs = s.StartedMs, EndedAtMs = now, ExitKind = 1,
                            });
                            if (selfTest && s.App == "UsageTracker.Engine" && s.Pid != selfPid) sawChildStop = true;
                        }
                    }

                    if (selfTest && !childSpawned && now - startMs >= 5000)
                    {
                        childSpawned = true;
                        log.Info("[自检] 启动子进程(休眠 5s)…");
                        try
                        {
                            var psi = new ProcessStartInfo(Environment.ProcessPath!)
                            {
                                UseShellExecute = false,
                                CreateNoWindow = true,
                            };
                            psi.ArgumentList.Add("--child");
                            psi.ArgumentList.Add("5000");
                            Process.Start(psi);
                        }
                        catch (Exception ex)
                        {
                            log.Info($"[自检] 子进程启动失败: {ex.Message}");
                        }
                    }
                }

                // ---- 前台归属 1s ----
                if (now - lastFg >= 1000)
                {
                    lastFg = now;
                    IntPtr hwnd = NativeMethods.GetForegroundWindow();
                    NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid32);
                    bool hasFg = hwnd != IntPtr.Zero && pid32 != 0;
                    ProcRec? rec = null;
                    if (hasFg)
                    {
                        pidInfo.TryGetValue(pid32, out rec);
                        if (rec == null && now - lastLife >= 2000)
                        {
                            // 前台进程快照里还没有(刚启动):拉一次快照补
                            foreach (var r in ProcessSnapshot.Query())
                            {
                                if (r.Pid == pid32)
                                {
                                    string file = r.FileName;
                                    rec = new ProcRec
                                    {
                                        FileName = file, App = TrackingPolicy.DisplayName(file),
                                        BornMs = r.CreatedMsUtc, Sid = r.SessionId,
                                        Parent = r.ParentPid, StartedMs = r.CreatedMsUtc,
                                        Excluded = !TrackingPolicy.ShouldTrack(r, selfPid),
                                    };
                                    pidInfo[pid32] = rec;
                                    break;
                                }
                            }
                        }
                    }

                    bool goodFg = rec != null && !rec.Excluded && !FgNoiseExe.Contains(rec.FileName) && !ignoredApps.Contains(rec.App);
                    if (!hasFg || !goodFg)
                    {
                        if (fg != null) { CloseFg(db, fg, now); fg = null; }
                        continue;
                    }

                    // 需要开始/切换片段
                    if (fg == null)
                    {
                        fg = new FgSeg { App = rec!.App, Pid = pid32, BornMs = rec.BornMs, TsFrom = now };
                    }
                    else if (fg.App != rec!.App || fg.Pid != pid32)
                    {
                        CloseFg(db, fg, now);
                        fg = new FgSeg { App = rec!.App, Pid = pid32, BornMs = rec.BornMs, TsFrom = now };
                    }

                    // Edge 域名:每 2s 读一次;host 变化即切片段(保证站点时长正确)
                    if (fg!.App == "Microsoft Edge" && hwnd != IntPtr.Zero && now - lastEdge >= 2000)
                    {
                        lastEdge = now;
                        string? host = HostNormalizer.TryNormalize(EdgeUrlReader.TryReadUrl(hwnd));
                        string? desired;
                        if (host == null)
                        {
                            edgeNullStreak++;
                            desired = edgeNullStreak >= 2 ? null : fg.Domain; // 偶发读取失败不切片段
                        }
                        else
                        {
                            edgeNullStreak = 0;
                            desired = host;
                        }
                        if (desired != fg.Domain)
                        {
                            CloseFg(db, fg, now);
                            fg = new FgSeg { App = fg.App, Pid = fg.Pid, BornMs = fg.BornMs, TsFrom = now, Domain = desired };
                        }
                    }
                }

                // ---- checkpoint 60s:写心跳 + 关当前片段并续开,把断电丢失窗口压到 ≤60s ----
                if (now - lastCheckpoint >= 60_000)
                {
                    lastCheckpoint = now;
                    // 心跳(meta.last_seen):下次启动对账按它封口,睡眠/关机期间不计入时长(设计 §4.4-3)
                    db.SetMeta("last_seen", now.ToString());
                    if (fg != null && now - fg.TsFrom >= 60_000)
                    {
                        var prev = fg;
                        CloseFg(db, fg, now);
                        fg = new FgSeg { App = prev.App, Pid = prev.Pid, BornMs = prev.BornMs, TsFrom = now, Domain = prev.Domain };
                    }
                }

                // ---- 每日维护:自动备份 + 90 天滚动清理(单写者线程内执行) ----
                string todayKey = TimeUtil.LocalDayKey(now);
                if (!maintDay.Equals(todayKey, StringComparison.Ordinal) && now - startMs >= maintDelayMs)
                {
                    maintDay = todayKey;
                    log.Info("[维护] 执行每日维护(自动备份 + 滚动清理)…");
                    Maintenance.RunOnce(db, dataDir, log);
                }

                // ---- 黑名单热载:每 8s 刷新;新入黑名单的会话立即封口 ----
                if (now - lastIgnoreRefresh >= 8000)
                {
                    lastIgnoreRefresh = now;
                    var fresh = db.IgnoredNames();
                    if (!ignoredApps.SetEquals(fresh))
                    {
                        ignoredApps = fresh;
                        foreach (var (key, s) in openSessions.ToList())
                        {
                            if (ignoredApps.Contains(s.App))
                            {
                                openSessions.Remove(key);
                                db.SessionClose(new SessionCloseInfo
                                {
                                    Pid = s.Pid, PidBornMs = s.BornMs, AppName = s.App,
                                    SessionId = s.Sid, ParentPid = s.Parent,
                                    StartedAtMs = s.StartedMs, EndedAtMs = now, ExitKind = 1,
                                });
                            }
                        }
                    }
                }

                // ---- 当前前台应用写入 meta(供 UI 展示“正在使用”) ----
                string curFg = fg?.App ?? "";
                if (!curFg.Equals(lastMetaFg, StringComparison.Ordinal))
                {
                    lastMetaFg = curFg;
                    db.SetMeta("fg_app", curFg);
                }

                ct.WaitHandle.WaitOne(200);
            }
        }
        finally
        {
            long exitMs = TimeUtil.UtcNowMs();
            if (fg != null) CloseFg(db, fg, exitMs);
            // 干净退出:主动封口所有仍开着的会话(exit_kind=4),不再留给下次对账。
            // 收益:①误差从“≤心跳周期 60s”降到 0;②exit_kind=2 今后只代表“崩溃/异常重启遗留”,语义干净。
            int closedOnExit = 0;
            foreach (var (key, s) in openSessions.ToList())
            {
                openSessions.Remove(key);
                if (db.SessionClose(new SessionCloseInfo
                    {
                        Pid = s.Pid, PidBornMs = s.BornMs, AppName = s.App,
                        SessionId = s.Sid, ParentPid = s.Parent,
                        StartedAtMs = s.StartedMs, EndedAtMs = exitMs, ExitKind = 4,
                    })) closedOnExit++;
            }
            db.SetMeta("last_clean_shutdown", exitMs.ToString());
            db.SetMeta("last_seen", exitMs.ToString());   // 干净退出的心跳:下次启动对账按它封口
            db.Dispose();
            log.Info($"[引擎退出] 自检:子进程 启动={sawChildStart} 停止={sawChildStop};已封口 {closedOnExit} 条运行中会话(exit_kind=4)");
        }
    }

    private static void CloseFg(UsageDb db, FgSeg fg, long nowMs)
    {
        if (nowMs - fg.TsFrom < 1000) return; // 忽略 <1s 的片段(防碎片)
        db.WriteSegment(new FgSegmentInfo
        {
            TsFromMs = fg.TsFrom,
            TsToMs = nowMs,
            Pid = fg.Pid,
            PidBornMs = fg.BornMs,
            AppName = fg.App,
            Domain = fg.Domain,
        });
    }
}
