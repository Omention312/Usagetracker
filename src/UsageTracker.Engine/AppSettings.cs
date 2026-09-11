using System.IO;
using System.Text.Json;

namespace UsageTracker.Engine;

/// <summary>引擎/UI 共享设置(存 dataDir/settings.json)。</summary>
internal sealed class AppSettings
{
    public string Theme { get; set; } = "dark";     // dark | light
    public double FontScale { get; set; } = 1.0;    // 0.85 / 1.0 / 1.15 / 1.3
    public bool SilentStart { get; set; } = true;   // 后台静默启动(不弹主界面)

    private string? _path;

    public static AppSettings Load(string dataDir)
    {
        var s = new AppSettings { _path = Path.Combine(dataDir, "settings.json") };
        try
        {
            if (File.Exists(s._path))
                s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(s._path)) ?? s;
            s._path = Path.Combine(dataDir, "settings.json");
        }
        catch { }
        s.Normalize();
        return s;
    }

    public void Normalize()
    {
        if (Theme is not ("light" or "dark" or "system")) Theme = "dark";
        FontScale = FontScale is >= 0.8 and <= 1.5 ? FontScale : 1.0;
    }

    public void Save()
    {
        try
        {
            if (_path != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch { }
    }
}
