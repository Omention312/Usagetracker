using System.IO;
using System.Text;

namespace UsageTracker.Engine;

/// <summary>极简文件日志(引擎无控制台)。</summary>
internal sealed class EngineLog
{
    private readonly string _path;
    private readonly object _lock = new();

    public EngineLog(string path)
    {
        _path = path;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        }
        catch { }
    }

    public void Info(string msg)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {msg}";
        lock (_lock)
        {
            try { File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8); }
            catch { /* 日志失败不影响主流程 */ }
        }
    }
}
