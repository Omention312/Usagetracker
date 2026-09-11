namespace UsageTracker.Engine;

/// <summary>开机自启(HKCU Run)读写。仅引擎自用,与 CLI 命令一致。</summary>
internal static class RegistryUtil
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "UsageTracker";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string;
        }
        catch { return false; }
    }

    public static string? CurrentCommand()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) as string;
        }
        catch { return null; }
    }

    public static void Enable(string engineExePath)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(ValueName, $"\"{engineExePath}\"");
    }

    public static void Disable()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
