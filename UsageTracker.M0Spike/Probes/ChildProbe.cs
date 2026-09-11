namespace UsageTracker.Spike.Probes;

/// <summary>辅助子进程:仅休眠后退出,供 probe-process 验证生命周期差分。</summary>
internal static class ChildProbe
{
    public static int Run(string[] args)
    {
        int ms = args.Length > 1 && int.TryParse(args[1], out var m) ? m : 5000;
        Console.WriteLine($"[child] pid={Environment.ProcessId} 休眠 {ms}ms 后退出");
        Thread.Sleep(ms);
        return 0;
    }
}
