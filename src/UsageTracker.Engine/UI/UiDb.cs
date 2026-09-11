using Microsoft.Data.Sqlite;
using UsageTracker.Core.Data;
using UsageTracker.Core.Normalize;

namespace UsageTracker.Engine.UI;

/// <summary>UI 数据访问(只读为主;黑名单切换/导出会写)。WAL 与引擎并发安全。</summary>
internal sealed class UiDb
{
    private readonly string _dbPath;

    public UiDb(string dataDir) => _dbPath = AppPaths.DbFile(dataDir);

    private SqliteConnection Open()
    {
        var c = new SqliteConnection($"Data Source={_dbPath}");
        c.Open();
        using var b = c.CreateCommand();
        b.CommandText = "PRAGMA busy_timeout=5000;";
        b.ExecuteNonQuery();
        return c;
    }

    private static long L(object? o) => o is null or DBNull ? 0 : Convert.ToInt64(o);
    private static string S(object? o) => o is null or DBNull ? "" : o.ToString()!;

    public long StatsSinceMs()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key='stats_since'";
        var o = cmd.ExecuteScalar();
        return o != null && long.TryParse(o.ToString(), out var v) ? v : 0;
    }

    public string CurrentFgApp()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key='fg_app'";
        return S(cmd.ExecuteScalar());
    }

    public List<(string App, long FgMs, long RunMs, long Cnt)> TodayStats(int limit)
    {
        var list = new List<(string, long, long, long)>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT app_name, SUM(fg_sec), SUM(total_sec), SUM(sessions) FROM daily_stats WHERE day=$d GROUP BY app_name ORDER BY SUM(fg_sec) DESC LIMIT $n";
        cmd.Parameters.AddWithValue("$d", DateTime.Now.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$n", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((r.GetString(0), r.GetInt64(1) * 1000L, r.GetInt64(2) * 1000L, r.GetInt64(3)));
        return list;
    }

    /// <summary>正在运行的应用(会话未结束;忽略黑名单)。按显示名聚合。</summary>
    public List<(string App, long SinceMs)> RunningApps()
    {
        var dict = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT ps.app_name, MIN(ps.started_at) FROM process_sessions ps
            WHERE ps.ended_at IS NULL
              AND NOT EXISTS (SELECT 1 FROM app_info ai WHERE ai.app_name=ps.app_name AND ai.is_ignored=1)
            GROUP BY ps.app_name ORDER BY MIN(ps.started_at)
            """;
        using var r = cmd.ExecuteReader();
        while (r.Read()) dict[r.GetString(0)] = r.GetInt64(1);
        return dict.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    /// <summary>批量:一次查询返回 名称 → exe 路径(避免逐行 N+1 查询,加速概览加载)。</summary>
    public Dictionary<string, string> MostRecentExeMap(ICollection<string> apps)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (apps.Count == 0) return map;
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT app_name, exe_path FROM app_info WHERE exe_path IS NOT NULL";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            string name = r.GetString(0);
            if (apps.Contains(name)) map[name] = r.GetString(1);
        }
        return map;
    }

    public string? MostRecentExe(string app)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT exe_path FROM app_info WHERE app_name=$a AND exe_path IS NOT NULL LIMIT 1";
        cmd.Parameters.AddWithValue("$a", app);
        return S(cmd.ExecuteScalar()) is { Length: > 0 } s ? s : null;
    }

    /// <summary>黑名单列表(名称 + 是否有 exe 记录)。</summary>
    public List<string> IgnoredApps()
    {
        var list = new List<string>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT app_name FROM app_info WHERE is_ignored=1 ORDER BY app_name";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    public bool IsIgnored(string app)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM app_info WHERE app_name=$a AND is_ignored=1";
        cmd.Parameters.AddWithValue("$a", app);
        return L(cmd.ExecuteScalar()) > 0;
    }

    public void SetIgnored(string app, bool ignored)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = ignored
            ? "INSERT INTO app_info(exe_hash, exe_path, app_name, is_ignored) VALUES($h, NULL, $a, 1) ON CONFLICT(exe_hash) DO UPDATE SET is_ignored=1"
            : "UPDATE app_info SET is_ignored=0 WHERE app_name=$a";
        if (ignored) cmd.Parameters.AddWithValue("$h", ExeUtil.HashPath("app:" + app));
        cmd.Parameters.AddWithValue("$a", app);
        cmd.ExecuteNonQuery();
    }

    /// <summary>全时段聚合(全部应用概览用)。返回 名称→(前台ms,总ms,次数)。</summary>
    public Dictionary<string, (long FgMs, long RunMs, long Cnt)> AllTimeStats()
    {
        var map = new Dictionary<string, (long, long, long)>(StringComparer.OrdinalIgnoreCase);
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT app_name, SUM(fg_sec), SUM(total_sec), SUM(sessions) FROM daily_stats GROUP BY app_name";
        using var r = cmd.ExecuteReader();
        while (r.Read()) map[r.GetString(0)] = (r.GetInt64(1) * 1000L, r.GetInt64(2) * 1000L, r.GetInt64(3));
        return map;
    }

    /// <summary>曾记录过的应用名集合(去重)。</summary>
    public HashSet<string> RecordedApps()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT app_name FROM app_info WHERE is_ignored=0";
        using var r = cmd.ExecuteReader();
        while (r.Read()) set.Add(r.GetString(0));
        return set;
    }

    /// <summary>按日导出 CSV(设置-导出使用时长)。</summary>
    public int ExportDailyCsv(string path)
    {
        using var sw = new StreamWriter(path, false, System.Text.Encoding.UTF8);
        sw.WriteLine("日期,应用,前台秒,总运行秒,会话次数");
        int rows = 0;
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT day, app_name, fg_sec, total_sec, sessions FROM daily_stats ORDER BY day DESC, app_name";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            sw.WriteLine($"{r.GetString(0)},{Csv(r.GetString(1))},{r.GetInt64(2)},{r.GetInt64(3)},{r.GetInt64(4)}");
            rows++;
        }
        return rows;
    }

    private static string Csv(string s) => s.Contains(',') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    /// <summary>
    /// 今日真实指标(按今日时间轴对原始表做重叠计算,避免跨天折叠误差):
    /// fg=今日前台(片段∩今日); run=今日运行(会话∩今日,进行中截至 now);
    /// bg=真正的今日后台累计=run-fg; cnt=今日内启动会话数; first=今日最早开始记录时刻。
    /// </summary>
    public List<(string App, long FgMs, long BgMs, long RunMs, long Cnt, long FirstMs)> TodayTrue()
    {
        var n = DateTimeOffset.Now;
        long s = new DateTimeOffset(n.Year, n.Month, n.Day, 0, 0, 0, n.Offset).ToUnixTimeMilliseconds();
        long e = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var fg = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var run = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var cnt = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var first = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        using (var c = Open())
        {
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT app_name,
                           SUM(CASE WHEN ts_to>$s AND ts_from<$e THEN MIN(ts_to,$e)-MAX(ts_from,$s) ELSE 0 END)
                    FROM foreground_segments WHERE ts_to>$s AND ts_from<$e GROUP BY app_name
                    """;
                cmd.Parameters.AddWithValue("$s", s);
                cmd.Parameters.AddWithValue("$e", e);
                using var r = cmd.ExecuteReader();
                while (r.Read()) fg[r.GetString(0)] = r.GetInt64(1);
            }
            using (var cmd = c.CreateCommand())
            {
                // 逐条读取会话(∩今日),按应用合并重叠后累计——消除引擎重启导致的重复播种重复计
                cmd.CommandText = """
                    SELECT app_name, started_at, ended_at
                    FROM process_sessions
                    WHERE COALESCE(ended_at,$now)>$s AND started_at<$e
                    ORDER BY app_name, started_at
                    """;
                cmd.Parameters.AddWithValue("$s", s);
                cmd.Parameters.AddWithValue("$e", e);
                cmd.Parameters.AddWithValue("$now", e);
                var intervals = new Dictionary<string, List<(long A, long B)>>(StringComparer.OrdinalIgnoreCase);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        string app = r.GetString(0);
                        long a = Math.Max(r.GetInt64(1), s);
                        long b = r.IsDBNull(2) ? e : Math.Min(r.GetInt64(2), e);
                        if (b <= a) continue;
                        if (!intervals.TryGetValue(app, out var ls)) intervals[app] = ls = new List<(long, long)>();
                        ls.Add((a, b));
                        if (r.GetInt64(1) >= s && r.GetInt64(1) < e) cnt[app] = cnt.TryGetValue(app, out var pc) ? pc + 1 : 1;
                        if (r.GetInt64(1) >= s && r.GetInt64(1) < e &&
                            (!first.TryGetValue(app, out var pf) || r.GetInt64(1) < pf)) first[app] = r.GetInt64(1);
                    }
                }
                foreach (var (app, ls) in intervals)
                {
                    ls.Sort();
                    long merged = 0, curA = ls[0].A, curB = ls[0].B;
                    foreach (var (a, b) in ls)
                    {
                        if (a > curB) { merged += curB - curA; curA = a; curB = b; }
                        else curB = Math.Max(curB, b);
                    }
                    merged += curB - curA;
                    run[app] = merged;
                }
            }
        }

        var all = fg.Keys.Concat(run.Keys).Distinct(StringComparer.OrdinalIgnoreCase);
        var list = new List<(string, long, long, long, long, long)>();
        foreach (var app in all)
        {
            fg.TryGetValue(app, out long f);
            run.TryGetValue(app, out long r);
            cnt.TryGetValue(app, out long ct);
            first.TryGetValue(app, out long ft);
            long bg = Math.Max(0, r - f);
            list.Add((app, f, bg, r, ct, ft));
        }
        // 黑名单应用不再出现在「今日概览」(需求:加入后即从列表消失;历史数据仍保留在库里,可取消后继续累计)。
        // 旧实现漏了这层过滤(AllAppsPage 有,今日页没有) —— 用户报的“加入黑名单后仍在今日概览里”就是这里。
        var ignored = IgnoredApps();
        if (ignored.Count > 0)
        {
            var set = new HashSet<string>(ignored, StringComparer.OrdinalIgnoreCase);
            list.RemoveAll(x => set.Contains(x.Item1));
        }
        return list.OrderByDescending(x => x.Item2).ToList();
    }
}
