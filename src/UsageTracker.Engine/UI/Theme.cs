namespace UsageTracker.Engine.UI;

/// <summary>设计令牌(源自 UI风格规范.md)。浅色模式为同构映射。</summary>
internal sealed class Theme
{
    public bool Dark;
    public Color Bg;          // 内容区背景
    public Color Sidebar;     // 导航背景
    public Color Surface;     // 卡片/行
    public Color SurfaceHi;   // hover
    public Color Border;
    public Color Text;
    public Color TextDim;
    public Color TextFaint;
    public Color Accent;
    public Color AccentSoft;  // 半透明强调(悬停/选中底)
    public Color Danger;
    public Color Ok;
    public float Scale = 1f;

    /// <summary>当前 DPI 相对 96 的系数(PerMonitorV2 下由所在窗口 DPI 决定)。</summary>
    public float DpiFactor = 1f;

    /// <summary>界面整体缩放 = 用户字号缩放 × 系统 DPI 系数(布局与位图尺寸据此放大)。</summary>
    public float UiScale => Math.Max(0.5f, Scale * DpiFactor);

    private readonly Dictionary<(float Size, bool Bold), Font> _fonts = new();

    public static Theme Current { get; private set; } = DarkPalette;

    public static Theme DarkPalette => new()
    {
        Dark = true,
        Bg = Color.FromArgb(42, 44, 56),        // #2a2c38
        Sidebar = Color.FromArgb(30, 31, 39),   // #1e1f27
        Surface = Color.FromArgb(40, 50, 69),   // #283245
        SurfaceHi = Color.FromArgb(52, 59, 77),
        Border = Color.FromArgb(255, 255, 255, 22),
        Text = Color.White,
        TextDim = Color.FromArgb(196, 200, 210),
        TextFaint = Color.FromArgb(140, 143, 155),
        Accent = Color.FromArgb(23, 139, 255),  // #178bff
        AccentSoft = Color.FromArgb(60, 23, 139, 255),
        Danger = Color.FromArgb(229, 72, 77),
        Ok = Color.FromArgb(48, 209, 88),
    };

    public static Theme LightPalette => new()
    {
        Dark = false,
        Bg = Color.FromArgb(247, 247, 250),
        Sidebar = Color.FromArgb(237, 238, 243),
        Surface = Color.White,
        SurfaceHi = Color.FromArgb(230, 233, 240),
        Border = Color.FromArgb(20, 20, 25, 25),
        Text = Color.FromArgb(28, 29, 34),
        TextDim = Color.FromArgb(120, 122, 132),
        TextFaint = Color.FromArgb(160, 162, 170),
        Accent = Color.FromArgb(10, 132, 255),
        AccentSoft = Color.FromArgb(40, 10, 132, 255),
        Danger = Color.FromArgb(225, 60, 60),
        Ok = Color.FromArgb(36, 160, 70),
    };

    public static void Apply(string mode, double scale)
    {
        bool light = mode switch
        {
            "light" => true,
            "dark" => false,
            _ => SystemPrefersLight(),
        };
        Current = light ? LightPalette : DarkPalette;
        Current.Scale = (float)(scale is >= 0.8 and <= 1.5 ? scale : 1.0);
    }

    public static bool SystemPrefersLight()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var v = key?.GetValue("AppsUseLightTheme");
            return v is int i && i == 1;
        }
        catch { return false; }
    }

    public Font Font(float size, bool bold = false)
    {
        var key = (size, bold);
        if (_fonts.TryGetValue(key, out var f)) return f;
        var style = bold ? FontStyle.Bold : FontStyle.Regular;
        // 字号以磅为单位:DPI 感知下 GDI 按当前 DPI 渲染 → 高分屏自然锐利
        var font = new Font("Microsoft YaHei UI", Math.Max(6f, size * Scale), style, GraphicsUnit.Point);
        _fonts[key] = font;
        return font;
    }

    public static Color Alpha(Color c, int a) => Color.FromArgb(a, c);
}
