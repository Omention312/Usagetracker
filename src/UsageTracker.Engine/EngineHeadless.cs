using UsageTracker.Core.Data;

namespace UsageTracker.Engine;

/// <summary>headless 运行:起一个 STA 线程跑采集引擎 N 秒后退出(供自动化验证/未来 selftest)。</summary>
internal static class EngineHeadless
{
    public static int Run(int seconds, string dataDir)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        var log = new EngineLog(Path.Combine(AppPaths.LogDir(dataDir), "engine.log"));
        var done = new ManualResetEventSlim(false);
        Exception? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                EngineHost.Run(cts.Token, dataDir, log);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                error = ex;
                log.Info($"[fatal] {ex}");
            }
            finally
            {
                done.Set();
            }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA); // UIA(Edge 读取)需要 STA
        thread.Start();

        log.Info($"[headless] 引擎运行 {seconds}s(数据目录 {dataDir})");
        done.Wait();
        log.Info(error == null ? "[headless] 正常退出" : $"[headless] 异常退出: {error}");
        return error == null ? 0 : 1;
    }

    /// <summary>自检:跑 N 秒并自动开/关一个子进程,验证生命周期事件入库;通过返回 0。</summary>
    public static int RunSelfTest(int seconds, string dataDir)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        var log = new EngineLog(Path.Combine(AppPaths.LogDir(dataDir), "engine.log"));
        var done = new ManualResetEventSlim(false);
        Exception? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                EngineHost.RunWithSelfTest(cts.Token, dataDir, log);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { error = ex; log.Info($"[fatal] {ex}"); }
            finally { done.Set(); }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        done.Wait();
        return error == null ? 0 : 1;
    }
}
