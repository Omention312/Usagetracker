using UsageTracker.Core.Data;

namespace UsageTracker.Engine;

/// <summary>M1 采集引擎入口。无窗口宿主(WinExe):双击只驻托盘;--headless 供自动化验证。</summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // SystemAware:文字由 GDI 按真实 DPI 渲染(清晰);布局按 DpiFactor 放大(自适应)。
        // 不做 PerMonitorV2(本机缩放环境异常时易乱)。可用 USAGETRACKER_DPI=96|120|144… 覆盖系数做实验。
        try { Application.SetHighDpiMode(HighDpiMode.SystemAware); }
        catch { }
        string dataDir = AppPaths.DataDir();
        Directory.CreateDirectory(dataDir);

        if (args.Length > 0 && args[0].Equals("--headless", StringComparison.OrdinalIgnoreCase))
        {
            int seconds = args.Length > 1 && int.TryParse(args[1], out var s) ? s : 30;
            return EngineHeadless.Run(seconds, dataDir);
        }
        if (args.Length > 0 && args[0].Equals("--selftest", StringComparison.OrdinalIgnoreCase))
        {
            int seconds = args.Length > 1 && int.TryParse(args[1], out var s) ? s : 30;
            return EngineHeadless.RunSelfTest(seconds, dataDir);
        }
        if (args.Length > 0 && args[0].Equals("--child", StringComparison.OrdinalIgnoreCase))
        {
            // 自检子进程:休眠后退出(WinExe,无窗口)
            int ms = args.Length > 1 && int.TryParse(args[1], out var m) ? m : 5000;
            Thread.Sleep(ms);
            return 0;
        }
        if (args.Length > 0 && args[0].Equals("--probe-window", StringComparison.OrdinalIgnoreCase))
        {
            // T1 前台验收:显示置顶窗口并持续抢前台,运行 N 毫秒
            return ProbeWindow.Run(args);
        }
        if (args.Length > 0 && args[0].Equals("--ui-shot", StringComparison.OrdinalIgnoreCase))
        {
            // UI 自渲染验收(离屏画页成 PNG)
            return UI.UiShot.Run(args);
        }

        // 托盘模式:单实例
        using var mutex = new Mutex(true, @"Local\UsageTracker.Engine", out bool isFirst);
        if (!isFirst)
        {
            // 已有实例在跑,静默退出(不打扰)
            return 0;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        return TrayApp.Run(dataDir);
    }
}
