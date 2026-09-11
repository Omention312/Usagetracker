using System.Diagnostics;
using System.IO;
using UsageTracker.Spike.Native;

namespace UsageTracker.Spike.Probes;

/// <summary>探针①:进程生命周期快照差分。验证 (PID, CreateTime) 主键在无管理员下稳定产出启动/退出事件。</summary>
internal static class ProcessLifecycleProbe
{
    // 交互会话常见系统进程,过滤噪声输出(仍会记事件,只是不刷屏)
    private static readonly HashSet<string> SysNoise = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Registry", "Memory Compression", "Secure System", "svchost.exe", "csrss.exe",
        "wininit.exe", "services.exe", "lsass.exe", "fontdrvhost.exe", "dwm.exe", "dllhost.exe",
        "conhost.exe", "RuntimeBroker.exe", "ShellExperienceHost.exe", "SearchHost.exe",
    };

    public static int Run(string[] args)
    {
        int seconds = ParseInt(args, 1, 45);
        int intervalMs = ParseInt(args, 2, 3000);
        Console.WriteLine($"[probe-process] 观察 {seconds}s,快照间隔 {intervalMs}ms,普通权限运行");

        var previous = new Dictionary<(long, long), ProcessRow>();
        var seen = new HashSet<(long, long)>();
        bool sawChildStart = false, sawChildStop = false;
        string childName = Path.GetFileName(Environment.ProcessPath ?? "UsageTracker.Spike");
        var sw = Stopwatch.StartNew();
        int childIdx = 0;
        var nextChildAt = TimeSpan.FromSeconds(6);

        while (sw.Elapsed.TotalSeconds < seconds)
        {
            List<ProcessRow> current;
            try { current = ProcessSnapshot.Query(); }
            catch (Exception ex)
            {
                Console.WriteLine($"[probe-process] 快照失败(致命): {ex.Message}");
                return 1;
            }

            var map = new Dictionary<(long, long), ProcessRow>(current.Count);
            foreach (var r in current) map[r.Key] = r;

            // 启动事件:新增 key
            foreach (var (k, row) in map)
            {
                if (!previous.ContainsKey(k))
                {
                    seen.Add(k);
                    bool isChild = row.Name.Contains(childName, StringComparison.OrdinalIgnoreCase);
                    if (isChild) sawChildStart = true;
                    bool noisy = row.SessionId == 0 || IsNoise(row.Name);
                    if (!noisy || isChild)
                    {
                        Console.WriteLine($"[启动] {FmtTime(row.CreateTime)}  {Name(row)}  session={row.SessionId} parent={row.ParentPid} key=({row.Pid},{CtShort(row.CreateTime)})");
                    }
                }
            }

            // 退出事件:key 消失
            foreach (var (k, old) in previous)
            {
                if (!map.ContainsKey(k))
                {
                    bool isChild = old.Name.Contains(childName, StringComparison.OrdinalIgnoreCase);
                    if (isChild) sawChildStop = true;
                    bool noisy = old.SessionId == 0 || IsNoise(old.Name);
                    if (!noisy || isChild)
                    {
                        TimeSpan life = DateTime.UtcNow - DateTime.FromFileTimeUtc(old.CreateTime);
                        Console.WriteLine($"[退出] {DateTime.Now:HH:mm:ss.fff}  {Name(old)}  session={old.SessionId} 存活≈{(int)life.TotalSeconds}s");
                    }
                }
            }

            // 到点后自动开一个子进程验证启停闭环
            if (sw.Elapsed >= nextChildAt && childIdx < 2)
            {
                childIdx++;
                nextChildAt = sw.Elapsed + TimeSpan.FromSeconds(12);
                Console.WriteLine($"[验证] 启动子进程 #{childIdx} (休眠 6s)…");
                try
                {
                    var psi = new ProcessStartInfo(Environment.ProcessPath!)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    psi.ArgumentList.Add("child");
                    psi.ArgumentList.Add("6000");
                    Process.Start(psi);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[验证] 子进程启动失败: {ex.Message}");
                }
            }

            previous = map;
            Thread.Sleep(intervalMs);
        }

        Console.WriteLine();
        Console.WriteLine($"[结果] 观测期间共见 {seen.Count} 个新进程;子进程 启动事件={sawChildStart}, 退出事件={sawChildStop}");
        bool pass = sawChildStart && sawChildStop;
        Console.WriteLine(pass ? "[结果] PASS:子进程启停均被差分捕获,主键 (PID,CreateTime) 成立"
                               : "[结果] FAIL:子进程启停事件缺失");
        return pass ? 0 : 1;
    }

    private static string Name(ProcessRow r) => Path.GetFileName(r.Name.TrimEnd('\0')).Length > 0
        ? Path.GetFileName(r.Name.TrimEnd('\0'))
        : $"(pid {r.Pid})";

    private static string FmtTime(long fileTimeTicks)
    {
        try { return DateTime.FromFileTimeUtc(fileTimeTicks).ToLocalTime().ToString("HH:mm:ss.fff"); }
        catch { return "?"; }
    }

    private static string CtShort(long fileTimeTicks)
    {
        try { return DateTime.FromFileTimeUtc(fileTimeTicks).ToLocalTime().ToString("HH:mm:ss"); }
        catch { return "?"; }
    }

    private static bool IsNoise(string name)
    {
        string n = Path.GetFileName(name.TrimEnd('\0'));
        return SysNoise.Contains(n);
    }

    private static int ParseInt(string[] args, int idx, int def)
        => args.Length > idx && int.TryParse(args[idx], out var v) ? v : def;
}
