using System.IO;

namespace UsageTracker.Core.Data;

/// <summary>数据目录约定:默认 %LOCALAPPDATA%\UsageTracker;可用环境变量 USAGETRACKER_DATA_DIR 覆盖(测试用)。</summary>
public static class AppPaths
{
    public const string AppName = "UsageTracker";
    public const string EnvDataDir = "USAGETRACKER_DATA_DIR";

    public static string DataDir()
    {
        string? env = Environment.GetEnvironmentVariable(EnvDataDir);
        if (!string.IsNullOrWhiteSpace(env)) return env;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);
    }

    public static string DbFile(string? dataDir = null)
        => Path.Combine(dataDir ?? DataDir(), "usage.db");

    public static string LogDir(string? dataDir = null)
        => Path.Combine(dataDir ?? DataDir(), "logs");
}
