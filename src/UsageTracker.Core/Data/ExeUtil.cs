using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace UsageTracker.Core.Data;

/// <summary>exe 路径 → 稳定短哈希(应用注册表主键用)。</summary>
public static class ExeUtil
{
    public static string HashPath(string pathOrKey)
    {
        string norm = (pathOrKey ?? "").Trim().ToLowerInvariant();
        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(norm));
        var sb = new StringBuilder(16);
        for (int i = 0; i < 8; i++) sb.Append(hash[i].ToString("x2"));
        return sb.ToString();
    }

    private static List<(string Name, string? ExePath)>? _installedCache;
    private static DateTime _cacheAt = DateTime.MinValue;

    /// <summary>注册表/开始菜单探测到的“已安装应用”候选(尽力枚举;供全部应用概览)。60s 缓存,加速重复进入页面。</summary>
    public static List<(string Name, string? ExePath)> EnumerateInstalledApps()
    {
        if (_installedCache != null && (DateTime.UtcNow - _cacheAt).TotalSeconds < 60)
            return _installedCache;

        var seen = new Dictionary<string, (string Name, string? Exe)>(); // key=exe文件名小写(若可得)
        void Scan(string rootKey)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(rootKey) ?? Microsoft.Win32.Registry.CurrentUser.OpenSubKey(rootKey);
                if (key == null) return;
                foreach (var sub in key.GetSubKeyNames())
                {
                    try
                    {
                        using var app = key.OpenSubKey(sub);
                        if (app == null) continue;
                        string? display = app.GetValue("DisplayName") as string;
                        string? icon = app.GetValue("DisplayIcon") as string;
                        if (string.IsNullOrWhiteSpace(display) || string.IsNullOrWhiteSpace(icon)) continue;
                        // DisplayIcon 常为 “路径,序号” 或裸 exe 路径
                        string exe = icon.Split(',')[0].Trim().Trim('"');
                        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                        if (File.Exists(exe)) seen[Path.GetFileName(exe).ToLowerInvariant()] = (display, exe);
                    }
                    catch { }
                }
            }
            catch { }
        }
        Scan(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
        Scan(@"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
        var result = seen.Values.Select(v => (v.Name, v.Exe)).OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();
        _installedCache = result;
        _cacheAt = DateTime.UtcNow;
        return result;
    }
}
