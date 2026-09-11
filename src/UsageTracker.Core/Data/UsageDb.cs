using System.IO;
using Microsoft.Data.Sqlite;
using UsageTracker.Core.Model;

namespace UsageTracker.Core.Data;

/// <summary>
/// SQLite 数据层(v2 DDL 子集)。单写者假设:引擎是唯一写者;查询方(CLI)只读。
/// 每次落库顺带增量折叠 daily_stats,保证长期体积可控。
/// </summary>
public sealed class UsageDb : IDisposable
{
    private readonly string _path;
    private SqliteConnection _conn = null!;

    public string DbPath => _path;

    public UsageDb(string dbPath)
    {
        _path = dbPath;
    }

    public void Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        _conn = new SqliteConnection($"Data Source={_path}");
        _conn.Open();
        Exec("PRAGMA journal_mode=WAL;");
        Exec("PRAGMA busy_timeout=5000;");
        EnsureSchema();
    }

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private void EnsureSchema()
    {
        Exec("""
            CREATE TABLE IF NOT EXISTS app_info (
              exe_hash   TEXT PRIMARY KEY,
              exe_path   TEXT,
              app_name   TEXT NOT NULL,
              is_ignored INTEGER DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS process_sessions (
              id         INTEGER PRIMARY KEY,
              pid        INTEGER NOT NULL,
              pid_born   INTEGER NOT NULL,
              app_name   TEXT NOT NULL,
              session_id INTEGER,
              parent_pid INTEGER,
              started_at INTEGER NOT NULL,
              ended_at   INTEGER,
              exit_kind  INTEGER DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS idx_ps_time ON process_sessions(started_at);
            CREATE INDEX IF NOT EXISTS idx_ps_open ON process_sessions(ended_at);
            CREATE TABLE IF NOT EXISTS foreground_segments (
              id        INTEGER PRIMARY KEY,
              ts_from   INTEGER NOT NULL,
              ts_to     INTEGER NOT NULL,
              pid       INTEGER,
              pid_born  INTEGER,
              app_name  TEXT NOT NULL,
              domain    TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_fg_time ON foreground_segments(ts_from);
            CREATE INDEX IF NOT EXISTS idx_fg_app  ON foreground_segments(app_name, ts_from);
            CREATE TABLE IF NOT EXISTS daily_stats (
              day        TEXT NOT NULL,
              app_name   TEXT NOT NULL,
              fg_sec     INTEGER NOT NULL,
              total_sec  INTEGER NOT NULL,
              sessions   INTEGER NOT NULL DEFAULT 0,
              PRIMARY KEY (day, app_name)
            );
            CREATE TABLE IF NOT EXISTS meta (
              key   TEXT PRIMARY KEY,
              value TEXT
            );
            """);
        // 统计起始时间:首次建库写入(视图顶部“开始统计时间”用)
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "INSERT OR IGNORE INTO meta(key,value) VALUES('stats_since', $now)";
            cmd.Parameters.AddWithValue("$now", TimeUtil.UtcNowMs().ToString());
            cmd.ExecuteNonQuery();
        }
    }

    // ---------- 会话(进程运行) ----------

    /// <summary>登记/更新应用注册表(记录 exe 路径,供界面取图标)。exePath 为空则跳过。</summary>
    public void UpsertAppInfo(string appName, string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return;
        string hash = ExeUtil.HashPath(exePath);
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO app_info(exe_hash, exe_path, app_name, is_ignored)
            VALUES($h, $p, $a, 0)
            ON CONFLICT(exe_hash) DO UPDATE SET exe_path=$p, app_name=$a
            """;
        cmd.Parameters.AddWithValue("$h", hash);
        cmd.Parameters.AddWithValue("$p", exePath);
        cmd.Parameters.AddWithValue("$a", appName);
        cmd.ExecuteNonQuery();
    }

    /// <summary>当前忽略名单(按应用显示名)。</summary>
    public HashSet<string> IgnoredNames()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT app_name FROM app_info WHERE is_ignored=1";
        using var r = cmd.ExecuteReader();
        while (r.Read()) set.Add(r.GetString(0));
        return set;
    }

    /// <summary>黑名单增删。返回当前是否在名单中。</summary>
    public bool SetIgnored(string appName, bool ignored)
    {
        if (ignored)
        {
            // 登记占位行(可能尚无 exe 路径)
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = """
                    INSERT INTO app_info(exe_hash, exe_path, app_name, is_ignored) VALUES($h, NULL, $a, 1)
                    ON CONFLICT(exe_hash) DO UPDATE SET is_ignored=1
                    """;
                cmd.Parameters.AddWithValue("$h", ExeUtil.HashPath("app:" + appName));
                cmd.Parameters.AddWithValue("$a", appName);
                cmd.ExecuteNonQuery();
            }
        }
        else
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "UPDATE app_info SET is_ignored=0 WHERE app_name=$a";
            cmd.Parameters.AddWithValue("$a", appName);
            cmd.ExecuteNonQuery();
        }
        return ignored;
    }

    /// <summary>统计起始(引擎首次记录)时间 ms。</summary>
    public long StatsSinceMs()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key='stats_since'";
        var o = cmd.ExecuteScalar();
        return o != null && long.TryParse(o.ToString(), out var v) ? v : TimeUtil.UtcNowMs();
    }

    public void SessionOpen(long pid, long pidBornMs, string appName, uint sessionId, long parentPid, long startedAtMs, string? exePath)
    {
        UpsertAppInfo(appName, exePath);
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO process_sessions(pid, pid_born, app_name, session_id, parent_pid, started_at, ended_at, exit_kind)
            VALUES($p, $b, $a, $s, $pp, $st, NULL, 0)
            """;
        cmd.Parameters.AddWithValue("$p", pid);
        cmd.Parameters.AddWithValue("$b", pidBornMs);
        cmd.Parameters.AddWithValue("$a", appName);
        cmd.Parameters.AddWithValue("$s", (long)sessionId);
        cmd.Parameters.AddWithValue("$pp", parentPid);
        cmd.Parameters.AddWithValue("$st", startedAtMs);
        cmd.ExecuteNonQuery();
    }

    /// <summary>关闭一条运行中会话并折叠进日汇总。返回是否更新了行。</summary>
    public bool SessionClose(SessionCloseInfo info)
    {
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE process_sessions SET ended_at=$e, exit_kind=$k WHERE pid=$p AND pid_born=$b AND ended_at IS NULL";
            cmd.Parameters.AddWithValue("$e", info.EndedAtMs);
            cmd.Parameters.AddWithValue("$k", info.ExitKind);
            cmd.Parameters.AddWithValue("$p", info.Pid);
            cmd.Parameters.AddWithValue("$b", info.PidBornMs);
            int n = cmd.ExecuteNonQuery();
            if (n == 0) return false;
        }
        FoldDailyRange(info.AppName, info.StartedAtMs, info.EndedAtMs, totalSec: DiffSec(info.StartedAtMs, info.EndedAtMs), fgSec: 0, sessionsDelta: 1);
        return true;
    }

    // ---------- 前台片段 ----------

    public void WriteSegment(FgSegmentInfo seg)
    {
        if (seg.TsToMs <= seg.TsFromMs) return;
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO foreground_segments(ts_from, ts_to, pid, pid_born, app_name, domain)
                VALUES($f, $t, $p, $b, $a, $d)
                """;
            cmd.Parameters.AddWithValue("$f", seg.TsFromMs);
            cmd.Parameters.AddWithValue("$t", seg.TsToMs);
            cmd.Parameters.AddWithValue("$p", (object?)seg.Pid ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$b", (object?)seg.PidBornMs ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$a", seg.AppName);
            cmd.Parameters.AddWithValue("$d", (object?)seg.Domain ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        FoldDailyRange(seg.AppName, seg.TsFromMs, seg.TsToMs, totalSec: 0, fgSec: DiffSec(seg.TsFromMs, seg.TsToMs), sessionsDelta: 0);
    }

    // ---------- 对账与折叠 ----------

    private static int DiffSec(long fromMs, long toMs)
    {
        long ms = Math.Max(0, toMs - fromMs);
        return (int)((ms + 500) / 1000);
    }

    /// <summary>把一段时长累加到“某一天”的日汇总(单日原语:dayAnchorMs 决定落在哪一天)。</summary>
    private void FoldDaily(string appName, long dayAnchorMs, int totalSec, int fgSec, int sessionsDelta)
    {
        string day = TimeUtil.LocalDayKey(dayAnchorMs);
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO daily_stats(day, app_name, fg_sec, total_sec, sessions)
            VALUES($d, $a, $fg, $tot, $s)
            ON CONFLICT(day, app_name) DO UPDATE SET
              fg_sec    = MAX(0, daily_stats.fg_sec    + excluded.fg_sec),
              total_sec = MAX(0, daily_stats.total_sec + excluded.total_sec),
              sessions  = MAX(0, daily_stats.sessions  + excluded.sessions)
            """;
        cmd.Parameters.AddWithValue("$d", day);
        cmd.Parameters.AddWithValue("$a", appName);
        cmd.Parameters.AddWithValue("$fg", fgSec);
        cmd.Parameters.AddWithValue("$tot", totalSec);
        cmd.Parameters.AddWithValue("$s", sessionsDelta);
        cmd.ExecuteNonQuery();
    }

    /// <summary>本地自然日的次日 00:00(毫秒)。</summary>
    private static long NextLocalDayStartMs(long ms)
    {
        var dt = DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime();
        var dayStart = new DateTimeOffset(dt.Year, dt.Month, dt.Day, 0, 0, 0, dt.Offset);
        return dayStart.AddDays(1).ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// 跨天安全折叠:把 [fromMs,toMs] 按自然日切开、分别累加到各自那一天的日汇总。
    /// 修复旧实现的偏差 —— 旧版把整段时长记在“会话起点那一天”(跨午夜会话会把 8 小时算进昨天/今天)。
    /// 秒数按切片占比分配、余数补到最后一片,保证 Σ切片 == 总秒数;会话次数只记在起点那一天。
    /// </summary>
    private void FoldDailyRange(string appName, long fromMs, long toMs, int totalSec, int fgSec, int sessionsDelta)
    {
        if (toMs <= fromMs)
        {
            if (sessionsDelta != 0) FoldDaily(appName, fromMs, 0, 0, sessionsDelta);
            return;
        }
        long span = toMs - fromMs;
        long cur = fromMs;
        int totUsed = 0, fgUsed = 0;
        bool first = true;
        while (cur < toMs)
        {
            long sliceEnd = Math.Min(NextLocalDayStartMs(cur), toMs);
            bool last = sliceEnd >= toMs;
            double share = (sliceEnd - cur) / (double)span;
            int tot = last
                ? Math.Max(0, totalSec - totUsed)
                : Math.Max(0, Math.Min(totalSec - totUsed, (int)Math.Round(totalSec * share)));
            int fgs = last
                ? Math.Max(0, fgSec - fgUsed)
                : Math.Max(0, Math.Min(fgSec - fgUsed, (int)Math.Round(fgSec * share)));
            FoldDaily(appName, cur, tot, fgs, first ? sessionsDelta : 0);
            totUsed += tot;
            fgUsed += fgs;
            first = false;
            cur = sliceEnd;
        }
    }

    /// <summary>
    /// 引擎启动对账:把上次异常退出遗留的 ended_at IS NULL 会话封口并折叠。
    /// 封口时刻取“最后已知还活着的时刻”(meta.last_seen 心跳,引擎每 60s 写一次),**不是**当前时刻 ——
    /// 这样机器睡眠/关机/引擎停摆期间不会被算成运行时长(设计 §4.4-3、§3.5,验收 T6)。
    /// 心跳缺失或早于会话起点时退回 min(now, …);误差上界 = 心跳周期(60s),与设计“断电丢失窗口 ≤60s”一致。
    /// </summary>
    public int ReconcileOpenSessions(long nowMs)
    {
        long? hb = LastSeenMs();
        long cap = hb.HasValue && hb.Value < nowMs ? hb.Value : nowMs;
        var open = new List<(long Id, string App, long Started)>();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, app_name, started_at FROM process_sessions WHERE ended_at IS NULL";
            using var r = cmd.ExecuteReader();
            while (r.Read()) open.Add((r.GetInt64(0), r.GetString(1), r.GetInt64(2)));
        }
        foreach (var (id, app, started) in open)
        {
            long ended = cap <= started ? started : Math.Min(cap, nowMs);
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE process_sessions SET ended_at=$e, exit_kind=2 WHERE id=$id";
                cmd.Parameters.AddWithValue("$e", ended);
                cmd.Parameters.AddWithValue("$id", id);
                cmd.ExecuteNonQuery();
            }
            FoldDailyRange(app, started, ended, totalSec: DiffSec(started, ended), fgSec: 0, sessionsDelta: 1);
        }
        return open.Count;
    }

    /// <summary>一条“疑似幻影会话”的修边方案:会话区间内部有一段“完全没有任何写入活动”的死区(睡眠/关机期间)。</summary>
    public sealed class PhantomSpan
    {
        public long Id;
        public string App = "";
        public long Pid, PidBorn, SessionId, ParentPid;
        public long StartedMs, OldEndMs;      // OldEndMs:已封口 = 原 ended_at;仍开着 = nowMs
        public bool Open;
        public int ExitKind;
        public long GapLeftMs, GapRightMs;    // 死区左右边界(死区 = 无任何写入活动)
        public long HeadMs => Math.Max(0, GapLeftMs - StartedMs);
        public long TailMs => Math.Max(0, OldEndMs - GapRightMs);
        public bool HeadOk => HeadMs >= 60_000;
        public bool TailOk => TailMs >= 60_000;
    }

    /// <summary>
    /// 诊断一次性修边:找出“声称在运行、但区间内部有一段完全没有任何写入活动”的会话。
    /// 成因(2026-09 定位):旧实现把进程创建时间当会话起点、对账时又把 ended_at 顶到重启时刻,
    /// 于是睡眠/关机期间(全程没有任何写入)被算成运行时长 —— 外表就是“今日后台 ≈ 距今日 00:00”。
    /// 判据:只处理 exit_kind=2(重启对账遗留)与仍开着(ended_at IS NULL)的会话;死区取该会话区间内
    /// “除自身之外的所有写入时刻(会话起止 + 片段 ts_to)”之间的最大空档,且必须 > minGapMs。
    /// </summary>
    public List<PhantomSpan> FindPhantomSpans(long minGapMs, long nowMs, long crossFromMs, bool anyDay = false, long hbSinceMs = 0)
    {
        var stamps = new List<long>();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT started_at FROM process_sessions
                UNION ALL SELECT ended_at FROM process_sessions WHERE ended_at IS NOT NULL
                UNION ALL SELECT ts_to FROM foreground_segments
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read()) if (!r.IsDBNull(0)) stamps.Add(r.GetInt64(0));
        }
        stamps.Sort();
        long lastStamp = stamps.Count > 0 ? stamps[^1] : 0;

        var rows = new List<(long Id, string App, long Pid, long Born, long Sid, long Parent, long St, long? En, int Kind)>();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, app_name, pid, pid_born, session_id, parent_pid, started_at, ended_at, exit_kind
                FROM process_sessions WHERE ended_at IS NULL OR exit_kind = 2
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
                rows.Add((r.GetInt64(0), r.GetString(1), r.GetInt64(2), r.GetInt64(3), r.GetInt64(4),
                          r.GetInt64(5), r.GetInt64(6), r.IsDBNull(7) ? null : r.GetInt64(7), r.GetInt32(8)));
        }

        var result = new List<PhantomSpan>();
        foreach (var (id, app, pid, born, sid, parent, st, en, kind) in rows)
        {
            long oldEnd = en ?? nowMs;
            if (oldEnd - st <= minGapMs) continue;
            // 默认只修“跨过某个自然日边界”的会话(这就是把睡眠/关机期间算进“今日”的那一类)。
            // 同一天内的长会话(例如午后离席两小时)不动 —— 那可能是真的没写入但应用一直在跑,不该裁。
            if (!anyDay && !(st < crossFromMs && oldEnd > crossFromMs)) continue;
            // 仍开着的会话:不能拿“现在”当结尾来判空档(那只是“最后写入之后到现在”,无从判断) —— 上限收到最后一条写入。
            long scanEnd = en ?? Math.Min(oldEnd, lastStamp);
            if (scanEnd - st <= minGapMs) continue;

            long gapL = st, gapR = scanEnd, best = 0, prev = st;
            int i0 = stamps.BinarySearch(st);
            if (i0 < 0) i0 = ~i0;
            for (int i = i0; i < stamps.Count; i++)
            {
                long t = stamps[i];
                if (t <= st) continue;        // 自身起点不算活动
                if (t >= scanEnd) break;      // 自身终点不算活动
                if (t - prev > best) { best = t - prev; gapL = prev; gapR = t; }
                prev = t;
            }
            if (scanEnd - prev > best) { best = scanEnd - prev; gapL = prev; gapR = scanEnd; }
            if (best <= minGapMs) continue;
            // 默认只修“能确证机器没在跑”的空档,两种信号:
            //   ① 跨自然日边界 —— 历史数据里唯一可靠的信号(隔夜空档);
            //   ② 心跳纪元之后的同日空档 —— 引擎每 60s 必写 meta['last_seen'],那时没写入说明它真的没在跑。
            // 两者都不满足、也没显式 --any-day 时跳过:同期“无写入”也可能只是锁屏/空闲,裁掉会丢真实时长。
            bool crossDay = st < crossFromMs && oldEnd > crossFromMs;
            bool hbEra = hbSinceMs > 0 && gapL >= hbSinceMs;
            if (!crossDay && !hbEra && !anyDay) continue;
            result.Add(new PhantomSpan
            {
                Id = id, App = app, Pid = pid, PidBorn = born, SessionId = sid, ParentPid = parent,
                StartedMs = st, OldEndMs = oldEnd, Open = en == null, ExitKind = kind,
                GapLeftMs = gapL, GapRightMs = gapR,
            });
        }
        return result.OrderByDescending(x => x.OldEndMs - x.StartedMs).ToList();
    }

    /// <summary>
    /// 应用修边:把跨死区的会话切成两段(必要时插入新行),并同步修正 daily_stats。
    /// 已封口行:先撤销旧折叠(整段记在起点那天),再按新分段逐日重折;仍开着且后段有效时保留其 id 并前移起点。
    /// 返回改动/新增行数。
    /// </summary>
    public int ApplyPhantomSpans(List<PhantomSpan> spans)
    {
        int changed = 0;
        foreach (var s in spans)
        {
            if (!s.HeadOk && !s.TailOk) continue;

            if (!s.Open)   // 已封口:撤销旧折叠(旧规则把整段记在起点那一天)
                FoldDaily(s.App, s.StartedMs, -DiffSec(s.StartedMs, s.OldEndMs), 0, -1);

            if (s.HeadOk && s.TailOk)
            {
                // 前段留在原行并封口,后段插入新行(与引擎“封口 + 续开”的行为一致)
                Exec("UPDATE process_sessions SET ended_at=$e, exit_kind=2 WHERE id=$id",
                     ("$e", (object)s.GapLeftMs), ("$id", (object)s.Id));
                Exec("""
                    INSERT INTO process_sessions(pid, pid_born, app_name, session_id, parent_pid, started_at, ended_at, exit_kind)
                    VALUES($p, $b, $a, $sd, $pp, $st, $e, $k)
                    """,
                     ("$p", (object)s.Pid), ("$b", (object)s.PidBorn), ("$a", (object)s.App), ("$sd", (object)s.SessionId),
                     ("$pp", (object)s.ParentPid), ("$st", (object)s.GapRightMs),
                     ("$e", s.Open ? (object)DBNull.Value : (object)s.OldEndMs), ("$k", (object)(s.Open ? 0 : 2)));
                FoldDailyRange(s.App, s.StartedMs, s.GapLeftMs, DiffSec(s.StartedMs, s.GapLeftMs), 0, 1);
                if (!s.Open) FoldDailyRange(s.App, s.GapRightMs, s.OldEndMs, DiffSec(s.GapRightMs, s.OldEndMs), 0, 1);
                changed += 2;
            }
            else if (s.HeadOk)
            {
                Exec("UPDATE process_sessions SET ended_at=$e, exit_kind=$k WHERE id=$id",
                     ("$e", (object)s.GapLeftMs), ("$k", (object)(s.Open ? 2 : s.ExitKind)), ("$id", (object)s.Id));
                FoldDailyRange(s.App, s.StartedMs, s.GapLeftMs, DiffSec(s.StartedMs, s.GapLeftMs), 0, 1);
                changed++;
            }
            else
            {
                // 只保留后段:把起点前移到“观测恢复”的时刻(对仍开着的会话尤其重要)
                Exec("UPDATE process_sessions SET started_at=$st WHERE id=$id",
                     ("$st", (object)s.GapRightMs), ("$id", (object)s.Id));
                if (!s.Open) FoldDailyRange(s.App, s.GapRightMs, s.OldEndMs, DiffSec(s.GapRightMs, s.OldEndMs), 0, 1);
                changed++;
            }
        }
        return changed;
    }

    private void Exec(string sql, params (string Name, object Value)[] ps)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in ps) cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>meta 读取(long;缺失/非数字返回 null)。</summary>
    public long? GetMetaLong(string key)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key=$k";
        cmd.Parameters.AddWithValue("$k", key);
        var o = cmd.ExecuteScalar();
        return o != null && long.TryParse(o.ToString(), out var v) ? v : null;
    }

    /// <summary>最后一次心跳时刻(“最后已知还活着的时刻”)。</summary>
    public long? LastSeenMs() => GetMetaLong("last_seen");

    public void SetMeta(string key, string value)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT INTO meta(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    public string Integrity()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check";
        return cmd.ExecuteScalar()?.ToString() ?? "?";
    }

    /// <summary>一致性副本(VACUUM INTO),用于自动备份。</summary>
    public void VacuumInto(string targetPath)
    {
        string escaped = targetPath.Replace("'", "''");
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"VACUUM INTO '{escaped}'";
        cmd.ExecuteNonQuery();
    }

    public void Vacuum()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "VACUUM";
        cmd.ExecuteNonQuery();
    }

    /// <summary>删除截止时刻之前已结束的进程会话(ended_at 为空的行保留,等对账)。</summary>
    public int DeleteOldSessions(long cutoffMs)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM process_sessions WHERE ended_at IS NOT NULL AND ended_at < $c";
        cmd.Parameters.AddWithValue("$c", cutoffMs);
        return cmd.ExecuteNonQuery();
    }

    /// <summary>删除截止时刻之前结束的前台片段。</summary>
    public int DeleteOldSegments(long cutoffMs)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM foreground_segments WHERE ts_to < $c";
        cmd.Parameters.AddWithValue("$c", cutoffMs);
        return cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _conn?.Dispose();
    }
}
