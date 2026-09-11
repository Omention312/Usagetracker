using System.IO;
using UsageTracker.Core.Data;

namespace UsageTracker.Engine.UI;

/// <summary>
/// 开发期布局/数据诊断:设置环境变量 USAGETRACKER_DEBUGLAYOUT=1 时,
/// 把控件矩形与量测值追加写入 &lt;数据目录&gt;\logs\layout.log。
/// 引擎是 WinExe(无控制台),原来的 Console.WriteLine 输出会丢失,故改为落盘。
/// 不影响正常运行:未设环境变量时 Enabled=false,调用点全部短路。
/// </summary>
internal static class LayoutLog
{
    private static readonly object Gate = new();

    public static bool Enabled { get; } =
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("USAGETRACKER_DEBUGLAYOUT"));

    public static void Write(string dataDir, string text)
    {
        if (!Enabled) return;
        try
        {
            string dir = AppPaths.LogDir(dataDir);
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "layout.log");
            lock (Gate)
            {
                File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {text}{Environment.NewLine}");
            }
        }
        catch
        {
            // 诊断通道绝不反过来影响功能
        }
    }
}
