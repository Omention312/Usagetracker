using System.IO;
using Microsoft.Data.Sqlite;

namespace UsageTracker.Core.Data;

/// <summary>统计读取(独立只读连接,WAL 与引擎并发安全)。UI 概览与 CLI 共用。</summary>
public static class StatsReader
{
    public static (long TotalFgSec, long TotalRunSec) TodayTotals(string dbPath)
    {
        using var conn = Open(dbPath);
        long start = LocalDayStartMs();
        long fg = Scalar(conn, "SELECT COALESCE(SUM(ts_to-ts_from),0) FROM foreground_segments WHERE ts_from>=$s", start) / 1000;
        long run = Scalar(conn, "SELECT COALESCE(SUM(ended_at-started_at),0) FROM process_sessions WHERE ended_at IS NOT NULL AND started_at>=$s", start) / 1000;
        return (fg, run);
    }

    public static List<(string App, long FgSec, long RunSec, long Count)> TodayApps(string dbPath, int limit = 10)
    {
        var list = new List<(string, long, long, long)>();
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT app_name, SUM(fg_sec), SUM(total_sec), SUM(sessions)
            FROM daily_stats WHERE day=$d
            GROUP BY app_name ORDER BY SUM(fg_sec) DESC LIMIT $n
            """;
        cmd.Parameters.AddWithValue("$d", DateTime.Now.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$n", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((r.GetString(0), r.GetInt64(1), r.GetInt64(2), r.GetInt64(3)));
        return list;
    }

    public static List<(string Site, long Sec)> TodaySites(string dbPath, int limit = 10)
    {
        var list = new List<(string, long)>();
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT domain, SUM(ts_to-ts_from)
            FROM foreground_segments
            WHERE domain IS NOT NULL AND ts_from>=$s
            GROUP BY domain ORDER BY 2 DESC LIMIT $n
            """;
        cmd.Parameters.AddWithValue("$s", LocalDayStartMs());
        cmd.Parameters.AddWithValue("$n", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((r.GetString(0), r.GetInt64(1) / 1000));
        return list;
    }

    public static List<(string App, long Sec)> RecentSessions(string dbPath, int n = 10)
    {
        var list = new List<(string, long)>();
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT app_name, ended_at-started_at FROM process_sessions WHERE ended_at IS NOT NULL ORDER BY id DESC LIMIT $n";
        cmd.Parameters.AddWithValue("$n", Math.Clamp(n, 1, 200));
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((r.GetString(0), r.GetInt64(1) / 1000));
        return list;
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

    private static long Scalar(SqliteConnection conn, string sql, long p)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$s", p);
        object? o = cmd.ExecuteScalar();
        return o == null || o is DBNull ? 0 : Convert.ToInt64(o);
    }

    private static long LocalDayStartMs()
    {
        var n = DateTimeOffset.Now;
        return new DateTimeOffset(n.Year, n.Month, n.Day, 0, 0, 0, n.Offset).ToUnixTimeMilliseconds();
    }
}
