using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;
using UsageTracker.Core.Data;

namespace UsageTracker.Cli;

/// <summary>CLI:查询与维护。只读打开 DB(WAL 允许与引擎并发)。</summary>
internal static class Program
{
    private const string Help = """
        UsageTracker.Cli — 查询与维护(引擎在后台写库,本工具只读)

        用法:
          status                   数据库状态:路径/行数/integrity
          today                    今日各应用前台时长 Top
          top [days]               近 N 天(默认 7)累计使用时长 Top
          overview                 今日概览(与托盘窗口同源:应用 + Edge 站点)
          report [days]            生成近 N 天(默认 7)自包含 HTML 周报并打开
          blacklist list|add|remove <app>  黑名单查看/加入/取消(与界面同源)
          accept-check app startMs durMs  验收判定:app 在 [startMs,startMs+durMs] 的前台覆盖时长
          recent [n]               最近 n 条进程会话与前台片段(调试)
          repair-spans [--apply] [--any-day] [--min-gap-minutes N]
                                   历史幻影修边:把“区间内有整段无任何写入活动”的会话切成两段
                                   (睡眠/关机期间被算成运行时长的历史行)。默认只处理“能确证机器没在跑”
                                   的空档(跨自然日边界,或心跳纪元之后的同日空档),且只预览;
                                   --apply 才写库;--any-day 连心跳前的同日空档也修(激进,可能裁掉锁屏期间的真会话)
          autostart on|off|status  Engine 开机自启(HKCU Run;需指定引擎 exe 路径)
        """;

    [STAThread]
    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
        string dbPath = AppPaths.DbFile();
        if (!File.Exists(dbPath))
        {
            Console.WriteLine($"数据库不存在: {dbPath}");
            Console.WriteLine("(先运行 UsageTracker.Engine 采集后再查询;或设置 USAGETRACKER_DATA_DIR)");
            return 1;
        }
        return cmd switch
        {
            "status" => Status(dbPath),
            "today" => Today(dbPath),
            "top" => Top(dbPath, ParseDays(args, 1, 7)),
            "overview" => Overview(dbPath),
            "report" => Report(dbPath, ParseDays(args, 1, 7)),
            "blacklist" => Blacklist(dbPath, args),
            "accept-check" => AcceptCheck(dbPath, args),
            "recent" => Recent(dbPath, args.Length > 1 && int.TryParse(args[1], out var n) ? n : 10),
            "repair-spans" => RepairSpans(dbPath, args),
            "autostart" => AutoStart(args),
            "help" or "-h" or "--help" => Print(Help, 0),
            _ => Print(Help, 2),
        };
    }

    private static int Print(string s, int code) { Console.WriteLine(s); return code; }

    private static int ParseDays(string[] args, int idx, int def)
    {
        if (args.Length > idx && int.TryParse(args[idx], out var v) && v is > 0 and <= 3660) return v;
        return def;
    }

    private static int Status(string dbPath)
    {
        using var conn = Open(dbPath);
        long sessions = Scalar(conn, "SELECT COUNT(*) FROM process_sessions");
        long open = Scalar(conn, "SELECT COUNT(*) FROM process_sessions WHERE ended_at IS NULL");
        long exit2 = Scalar(conn, "SELECT COUNT(*) FROM process_sessions WHERE exit_kind=2");
        long segs = Scalar(conn, "SELECT COUNT(*) FROM foreground_segments");
        long daily = Scalar(conn, "SELECT COUNT(*) FROM daily_stats");
        string integrity = Text(conn, "PRAGMA integrity_check");
        long lastClean = Text(conn, "SELECT value FROM meta WHERE key='last_clean_shutdown'") is { Length: > 0 } s
            ? long.Parse(s) : 0;
        var size = new FileInfo(dbPath).Length;
        Console.WriteLine($"数据库     : {dbPath}");
        Console.WriteLine($"体积       : {size / 1024.0:F1} KB (WAL 不计)");
        Console.WriteLine($"运行会话   : {sessions} 行(其中运行中 {open})");
        Console.WriteLine($"reconciled(exit=2): {exit2}   <- 崩溃/重启对账封口标记数");
        Console.WriteLine($"前台片段   : {segs} 行");
        Console.WriteLine($"日汇总     : {daily} 行");
        Console.WriteLine($"integrity  : {integrity}");
        Console.WriteLine($"上次干净退出: {(lastClean > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(lastClean).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "(无记录)")}");
        return integrity == "ok" ? 0 : 1;
    }

    private static int Today(string dbPath)
    {
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        return DumpStats(dbPath, "WHERE day=$d", ("$d", today), "今日");
    }

    private static int Top(string dbPath, int days)
    {
        string from = DateTime.Now.AddDays(-(days - 1)).ToString("yyyy-MM-dd");
        return DumpStats(dbPath, "WHERE day>=$d", ("$d", from), $"近 {days} 天");
    }

    private static int DumpStats(string dbPath, string whereSql, (string, object) p, string title)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT app_name, SUM(fg_sec), SUM(total_sec), SUM(sessions)
            FROM daily_stats {whereSql}
            GROUP BY app_name
            ORDER BY SUM(fg_sec) DESC
            LIMIT 20
            """;
        cmd.Parameters.AddWithValue(p.Item1, p.Item2);
        Console.WriteLine($"{title} Top20(前台使用 / 运行总时长 / 次数):");
        using var r = cmd.ExecuteReader();
        int i = 1;
        while (r.Read())
        {
            string app = r.GetString(0);
            long fg = r.GetInt64(1), tot = r.GetInt64(2), cnt = r.GetInt64(3);
            Console.WriteLine($"  {i++,2}. {app,-24} 前台 {Fmt(fg),-10} 总 {Fmt(tot),-10} {cnt} 次");
        }
        return 0;
    }

    private static int Overview(string dbPath)
    {
        var (fg, run) = StatsReader.TodayTotals(dbPath);
        Console.WriteLine($"今日:前台使用 {Fmt(fg)} · 运行总时长 {Fmt(run)}\n");
        Console.WriteLine("应用 Top(前台 / 总运行 / 次数):");
        foreach (var (app, aFg, aRun, cnt) in StatsReader.TodayApps(dbPath))
            Console.WriteLine($"  {app,-24} 前台 {Fmt(aFg),-10} 总 {Fmt(aRun),-10} {cnt} 次");
        Console.WriteLine("\nEdge 站点 Top:");
        foreach (var (site, sec) in StatsReader.TodaySites(dbPath))
            Console.WriteLine($"  {site,-28} {Fmt(sec)}");
        return 0;
    }

    private static int Blacklist(string dbPath, string[] args)
    {
        string mode = args.Length > 1 ? args[1].ToLowerInvariant() : "list";
        using var conn = Open(dbPath);
        switch (mode)
        {
            case "list":
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT DISTINCT app_name FROM app_info WHERE is_ignored=1 ORDER BY app_name";
                using var r = cmd.ExecuteReader();
                int n = 0;
                while (r.Read()) { Console.WriteLine(r.GetString(0)); n++; }
                Console.WriteLine(n == 0 ? "(黑名单为空)" : $"共 {n} 项");
                return 0;
            }
            case "add":
            case "remove":
            {
                string app = args.Length > 2 ? args[2] : "";
                if (app.Length == 0) { Console.WriteLine("用法: blacklist add|remove <应用名>"); return 2; }
                using var cmd = conn.CreateCommand();
                if (mode == "add")
                {
                    cmd.CommandText = "INSERT INTO app_info(exe_hash, exe_path, app_name, is_ignored) VALUES($h, NULL, $a, 1) ON CONFLICT(exe_hash) DO UPDATE SET is_ignored=1";
                    cmd.Parameters.AddWithValue("$h", UsageTracker.Core.Data.ExeUtil.HashPath("app:" + app));
                }
                else
                {
                    cmd.CommandText = "UPDATE app_info SET is_ignored=0 WHERE app_name=$a";
                }
                cmd.Parameters.AddWithValue("$a", app);
                cmd.ExecuteNonQuery();
                Console.WriteLine(mode == "add" ? $"已加入黑名单: {app}(此后不再记录;历史数据保留)" : $"已取消黑名单: {app}");
                return 0;
            }
            default:
                Console.WriteLine("用法: blacklist list|add <应用>|remove <应用>");
                return 2;
        }
    }

    /// <summary>T1 验收判定:统计指定 app 在 [startMs, startMs+durMs] 窗口内与前台片段重叠的总毫秒。</summary>
    private static int AcceptCheck(string dbPath, string[] args)
    {
        if (args.Length < 4 || !long.TryParse(args[2], out long startMs) || !long.TryParse(args[3], out long durMs) || durMs <= 0)
        {
            Console.WriteLine("用法: accept-check <app名称> <startMs(UTC毫秒)> <durMs>");
            return 2;
        }
        string app = args[1];
        long endMs = startMs + durMs;
        long overlapMs = 0;
        using (var conn = Open(dbPath))
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT ts_from, ts_to FROM foreground_segments
                WHERE app_name=$a AND ts_from < $e AND ts_to > $s
                """;
            cmd.Parameters.AddWithValue("$a", app);
            cmd.Parameters.AddWithValue("$s", startMs);
            cmd.Parameters.AddWithValue("$e", endMs);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                long f = r.GetInt64(0), t = r.GetInt64(1);
                overlapMs += Math.Min(t, endMs) - Math.Max(f, startMs);
            }
        }
        long allowedShortfallMs = 8000; // 覆盖激活延迟与 1s 轮询边界(粗验;精确验收用手动 T1)
        bool pass = overlapMs >= durMs - allowedShortfallMs;
        string verdict = overlapMs == 0 ? "ZERO" : pass ? "PASS" : "FAIL";
        Console.WriteLine($"[accept-check] app={app} 窗口={durMs}ms 实测覆盖={overlapMs}ms 缺口={durMs - overlapMs}ms 阈值允许缺口={allowedShortfallMs}ms");
        Console.WriteLine($"[accept-check] verdict={verdict}");
        return pass ? 0 : 1;
    }

    /// <summary>生成近 N 天自包含 HTML 周报并打开(无常驻服务,属“查看”)。</summary>
    private static int Report(string dbPath, int days)
    {
        var rows = new List<(string Day, string App, long Fg, long Run, long Cnt)>();
        string from = DateTime.Now.AddDays(-(days - 1)).ToString("yyyy-MM-dd");
        using (var conn = Open(dbPath))
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT day, app_name, SUM(fg_sec), SUM(total_sec), SUM(sessions)
                FROM daily_stats WHERE day>=$d GROUP BY day, app_name ORDER BY day DESC, SUM(fg_sec) DESC
                """;
            cmd.Parameters.AddWithValue("$d", from);
            using var r = cmd.ExecuteReader();
            while (r.Read()) rows.Add((r.GetString(0), r.GetString(1), r.GetInt64(2), r.GetInt64(3), r.GetInt64(4)));
        }
        // 今日站点
        var sites = StatsReader.TodaySites(dbPath, 15);

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"zh\"><head><meta charset=\"utf-8\"><title>UsageTracker 周报</title>");
        sb.AppendLine("<style>body{font-family:'Microsoft YaHei UI',sans-serif;margin:24px;color:#222}table{border-collapse:collapse;width:100%;margin:8px 0 20px}th,td{border:1px solid #ccc;padding:6px 10px;text-align:left}th{background:#f0f0f0}tr:nth-child(even){background:#fafafa}h2{margin-top:28px}</style></head><body>");
        sb.AppendLine($"<h1>UsageTracker 使用报告</h1><p>生成时间:{DateTime.Now:yyyy-MM-dd HH:mm} · 覆盖最近 {days} 天</p>");
        sb.AppendLine("<h2>Edge 站点(今日)</h2><table><tr><th>站点</th><th>使用时长</th></tr>");
        foreach (var (site, sec) in sites) sb.AppendLine($"<tr><td>{Html(site)}</td><td>{Fmt(sec)}</td></tr>");
        if (sites.Count == 0) sb.AppendLine("<tr><td colspan=2>(今日暂无站点记录)</td></tr>");
        sb.AppendLine("</table>");
        sb.AppendLine("<h2>各应用使用情况(按日)</h2><table><tr><th>日期</th><th>应用</th><th>前台使用</th><th>运行总时长</th><th>次数</th></tr>");
        foreach (var (day, app, fg, run, cnt) in rows)
            sb.AppendLine($"<tr><td>{day}</td><td>{Html(app)}</td><td>{Fmt(fg)}</td><td>{Fmt(run)}</td><td>{cnt}</td></tr>");
        if (rows.Count == 0) sb.AppendLine("<tr><td colspan=5>(暂无数据)</td></tr>");
        sb.AppendLine("</table></body></html>");

        var dir = Path.Combine(Path.GetDirectoryName(dbPath)!, "reports");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, $"report-{DateTime.Now:yyyyMMdd-HHmmss}.html");
        File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
        Console.WriteLine($"已生成: {file}");
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("USAGETRACKER_NO_OPEN")))
        {
            try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); }
            catch { Console.WriteLine("(自动打开失败,请手动打开该文件)"); }
        }
        return 0;
    }

    private static string Html(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string Fmt(long sec)
    {
        var t = TimeSpan.FromSeconds(sec);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h{t.Minutes}m" : $"{t.Minutes}m{t.Seconds}s";
    }

    /// <summary>
    /// 历史幻影修边:把“区间内部有一整段完全没有任何写入活动(睡眠/关机)”的会话切成两段。
    /// 默认只预览、默认只修“能确证机器没在跑”的空档(跨自然日边界,或心跳纪元之后的同日空档);
    /// --any-day 才连“心跳之前、无从判定”的同日空档一起看(可能裁掉锁屏期间的真会话,慎用)。
    /// </summary>
    private static int RepairSpans(string dbPath, string[] args)
    {
        bool apply = args.Any(a => a.Equals("--apply", StringComparison.OrdinalIgnoreCase));
        bool anyDay = args.Any(a => a.Equals("--any-day", StringComparison.OrdinalIgnoreCase));
        int gapMin = 30;
        for (int i = 1; i < args.Length - 1; i++)
            if (args[i].Equals("--min-gap-minutes", StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out var v) && v > 0)
                gapMin = v;
        long minGapMs = gapMin * 60_000L;
        var nowDto = DateTimeOffset.Now;
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long todayStartMs = new DateTimeOffset(nowDto.Year, nowDto.Month, nowDto.Day, 0, 0, 0, nowDto.Offset).ToUnixTimeMilliseconds();

        using var db = new UsageDb(dbPath);
        db.Open();
        long hbSince = db.GetMetaLong("hb_since") ?? 0;
        var spans = db.FindPhantomSpans(minGapMs, nowMs, todayStartMs, anyDay, hbSince);
        string modeLabel = anyDay ? "含心跳前的同日空档(激进)" : (hbSince > 0 ? "跨自然日 + 心跳后的同日" : "仅跨自然日");
        if (spans.Count == 0)
        {
            Console.WriteLine($"没有发现需要修边的会话(死区 > {gapMin} 分钟,范围:{modeLabel})。");
            return 0;
        }
        Console.WriteLine($"发现 {spans.Count} 条跨死区会话(死区 > {gapMin} 分钟,范围:{modeLabel}):");
        long removed = 0;
        foreach (var s in spans)
        {
            long oldLen = s.OldEndMs - s.StartedMs;
            long keep = (s.HeadOk ? s.HeadMs : 0) + (s.TailOk ? s.TailMs : 0);
            removed += oldLen - keep;
            string how = s.HeadOk && s.TailOk ? "[前段封口 + 后段续开]"
                       : s.HeadOk ? "[裁掉尾巴]"
                       : "[起点前移]";
            Console.WriteLine($"  {s.App,-24} {Ts(s.StartedMs)}→{(s.Open ? "运行中" : Ts(s.OldEndMs))} ({Fmt(oldLen / 1000)})" +
                              $"  死区 {Ts(s.GapLeftMs)}→{Ts(s.GapRightMs)}  保留 {Fmt(keep / 1000)} {how}");
        }
        Console.WriteLine($"合计剔除 {Fmt(removed / 1000)}({removed / 1000}s) —— 全是没有写入活动的时段。");
        if (!apply)
        {
            Console.WriteLine("预览模式:数据库未改动。确认后加 --apply 执行(建议先做备份)。");
            return 0;
        }
        int n = db.ApplyPhantomSpans(spans);
        Console.WriteLine($"已应用修边(第 1 轮):改动/新增 {n} 行;daily_stats 已同步改正。");
        // 一条会话可能跨多个死区(例如“从 9/9 起、跨两夜”),每轮只切最大的那一个 —— 循环跑到干净。
        for (int round = 2; round <= 5; round++)
        {
            var more = db.FindPhantomSpans(minGapMs, nowMs, todayStartMs, anyDay, hbSince);
            if (more.Count == 0) { Console.WriteLine($"第 {round} 轮:已无候选,收敛。"); break; }
            int m = db.ApplyPhantomSpans(more);
            Console.WriteLine($"已应用修边(第 {round} 轮):{more.Count} 条候选 → 改动/新增 {m} 行。");
        }
        return 0;
    }

    private static int Recent(string dbPath, int n)
    {
        n = Math.Clamp(n, 1, 2000);
        using var conn = Open(dbPath);

        Console.WriteLine($"最近 {n} 条进程会话:");
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT app_name, pid, started_at, ended_at, exit_kind FROM process_sessions
                ORDER BY id DESC LIMIT $n
                """;
            cmd.Parameters.AddWithValue("$n", n);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string app = r.GetString(0);
                long pid = r.GetInt64(1);
                long st = r.GetInt64(2);
                long? en = r.IsDBNull(3) ? null : r.GetInt64(3);
                int kind = r.GetInt32(4);
                string span = en.HasValue ? $"{Ts(st):HH:mm:ss}→{Ts(en.Value):HH:mm:ss} ({Fmt((en.Value - st) / 1000)})" : $"{Ts(st):HH:mm:ss}→运行中";
                Console.WriteLine($"  {app,-22} pid={pid,-7} {span}  exit={kind}");
            }
        }
        Console.WriteLine($"最近 {n} 条前台片段:");
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT app_name, domain, ts_from, ts_to FROM foreground_segments
                ORDER BY id DESC LIMIT $n
                """;
            cmd.Parameters.AddWithValue("$n", n);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string app = r.GetString(0);
                string dom = r.IsDBNull(1) ? "" : r.GetString(1);
                long f = r.GetInt64(2), t = r.GetInt64(3);
                Console.WriteLine($"  {app,-22} dom={dom,-22} {Ts(f):HH:mm:ss}→{Ts(t):HH:mm:ss} ({Fmt((t - f) / 1000)})");
            }
        }
        return 0;
    }

    private static DateTime Ts(long ms)
    {
        try { return DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().DateTime; }
        catch { return DateTime.MinValue; }
    }

    private static int AutoStart(string[] args)
    {
        const string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string valueName = "UsageTracker";
        string mode = args.Length > 1 ? args[1].ToLowerInvariant() : "status";
        string enginePath = args.Length > 2 ? Path.GetFullPath(args[2])
            : Path.Combine(AppContext.BaseDirectory, "UsageTracker.Engine.exe");

        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(keyPath);
        switch (mode)
        {
            case "on":
                key.SetValue(valueName, $"\"{enginePath}\"");
                Console.WriteLine($"已设置开机自启: {enginePath}");
                break;
            case "off":
                key.DeleteValue(valueName, throwOnMissingValue: false);
                Console.WriteLine("已取消开机自启");
                break;
            default:
                var v = key.GetValue(valueName);
                Console.WriteLine(v is null ? "开机自启: 未设置"
                    : $"开机自启: {v}\n(引擎 exe 需存在且可访问)");
                break;
        }
        return 0;
    }

    private static SqliteConnection Open(string dbPath)
    {
        var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var busy = conn.CreateCommand();
        busy.CommandText = "PRAGMA busy_timeout=5000;";
        busy.ExecuteNonQuery();
        return conn;
    }

    private static long Scalar(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        object? o = cmd.ExecuteScalar();
        return o == null || o is DBNull ? 0 : Convert.ToInt64(o);
    }

    private static string Text(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar()?.ToString() ?? "";
    }
}
