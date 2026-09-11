using System.IO;
namespace UsageTracker.Spike.Probes;

/// <summary>探针③a:SQLite 骨架自检(建库、WAL、DDL、读写、integrity_check)。</summary>
internal static class StoreProbe
{
    private const string Ddl = """
        PRAGMA journal_mode=WAL;
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
          exe_hash   TEXT REFERENCES app_info(exe_hash),
          app_name   TEXT NOT NULL,
          session_id INTEGER,
          parent_pid INTEGER,
          started_at INTEGER NOT NULL,
          ended_at   INTEGER,
          exit_kind  INTEGER DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS idx_ps_time ON process_sessions(started_at);
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
        """;

    public static int Run(string[] args)
    {
        // 数据文件默认落在工程目录 spike-data(目录约束)
        string dbPath = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1])
            ? args[1]
            : Path.Combine(AppContext.BaseDirectory, "spike-data", "usage-spike.db");

        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        if (File.Exists(dbPath)) File.Delete(dbPath);

        Console.WriteLine($"[probe-db] 数据库: {dbPath}");

        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = Ddl;
            cmd.ExecuteNonQuery();
        }
        Console.WriteLine("[probe-db] DDL OK(WAL + 4 表 + 索引)");

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long pid = Environment.ProcessId;
        long born = now - 60_000;

        // 样例:1 条运行会话 + 3 条前台片段(其中 Edge 站点 www.bilibili.com)
        Exec(conn, "INSERT INTO process_sessions(pid,pid_born,app_name,started_at,ended_at,exit_kind) VALUES($p,$b,'Notepad', $t0, $t1, 1)",
             ("$p", pid), ("$b", born), ("$t0", now - 300_000), ("$t1", now - 60_000));
        Exec(conn, "INSERT INTO foreground_segments(ts_from,ts_to,pid,pid_born,app_name,domain) VALUES($f,$t,$p,$b,'Microsoft Edge','www.bilibili.com')",
             ("$f", now - 180_000), ("$t", now - 120_000), ("$p", pid), ("$b", born));
        Exec(conn, "INSERT INTO foreground_segments(ts_from,ts_to,pid,pid_born,app_name,domain) VALUES($f,$t,$p,$b,'Microsoft Edge',NULL)",
             ("$f", now - 120_000), ("$t", now - 60_000), ("$p", pid), ("$b", born));
        Exec(conn, "INSERT INTO foreground_segments(ts_from,ts_to,pid,pid_born,app_name,domain) VALUES($f,$t,$p,$b,'Code',NULL)",
             ("$f", now - 60_000), ("$t", now), ("$p", pid), ("$b", born));

        Console.WriteLine("[probe-db] 写入 OK:1 会话 + 3 前台片段");

        // 查询验证
        long fgEdge = ScalarLong(conn, "SELECT COALESCE(SUM(ts_to-ts_from),0) FROM foreground_segments WHERE app_name='Microsoft Edge'");
        long fgBili = ScalarLong(conn, "SELECT COALESCE(SUM(ts_to-ts_from),0) FROM foreground_segments WHERE domain='www.bilibili.com'");
        long sessions = ScalarLong(conn, "SELECT COUNT(*) FROM process_sessions");
        string integrity = ScalarText(conn, "PRAGMA integrity_check");

        Console.WriteLine($"[probe-db] 查询:Edge 片段总毫秒={fgEdge}, bilibili 站点毫秒={fgBili}, 会话数={sessions}, integrity_check={integrity}");

        string walExists = File.Exists(dbPath + "-wal") ? "存在(WAL 激活)" : "无(-wal 为空属正常)";
        Console.WriteLine($"[probe-db] WAL 附属文件: {walExists}");

        bool pass = fgBili == 60_000 && sessions == 1 && integrity == "ok";
        Console.WriteLine(pass ? "[probe-db] PASS" : "[probe-db] FAIL");
        return pass ? 0 : 1;
    }

    private static void Exec(Microsoft.Data.Sqlite.SqliteConnection conn, string sql, params (string Name, object Val)[] ps)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    private static long ScalarLong(Microsoft.Data.Sqlite.SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        object? o = cmd.ExecuteScalar();
        return o == null || o is DBNull ? 0 : Convert.ToInt64(o);
    }

    private static string ScalarText(Microsoft.Data.Sqlite.SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar()?.ToString() ?? "";
    }
}
