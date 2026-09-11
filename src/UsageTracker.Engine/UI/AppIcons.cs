using System.IO;
using System.Runtime.InteropServices;

namespace UsageTracker.Engine.UI;

/// <summary>程序级图标 + 每个应用图标缓存(SHGetFileInfo / Icon 按尺寸提取,高分屏取大图,失败用字母占位)。</summary>
internal static class AppIcons
{
    private static readonly Dictionary<string, Bitmap> IconCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<(string Key, int Px), Bitmap> SizedCache = new();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;

    /// <summary>按当前绘制尺寸取图标(高分屏自动取更大源,更清晰)。</summary>
    public static Bitmap Get32(string? exePath, string appName) => Get(exePath, appName, RowGeom.IconSize);

    /// <summary>按目标尺寸取图标(高分屏用较大源,绘制更清晰)。</summary>
    public static Bitmap Get(string? exePath, string appName, int px)
    {
        px = Math.Clamp(px, 16, 256);
        string? key = string.IsNullOrWhiteSpace(exePath) ? null : exePath.Trim().ToLowerInvariant();
        if (key == null)
        {
            if (IconCache.TryGetValue("__ph_" + appName + "_" + px, out var pc)) return pc;
            var ph = Placeholder(appName, px);
            IconCache["__ph_" + appName + "_" + px] = ph;
            return ph;
        }
        if (SizedCache.TryGetValue((key, px), out var cached)) return cached;

        Bitmap? made = null;
        if (File.Exists(key))
        {
            // 优先用 Icon 按尺寸加载 exe 内最合适图标资源(现代 exe 常含 48/64/256)
            try { using var ico = new Icon(key, px, px); made = new Bitmap(ico.ToBitmap()); }
            catch { made = null; }
            if (made == null)
            {
                try
                {
                    var sh = new SHFILEINFO();
                    IntPtr r = SHGetFileInfo(key, 0, ref sh, (uint)Marshal.SizeOf<SHFILEINFO>(), ShgfiIcon | ShgfiLargeIcon);
                    if (r != IntPtr.Zero && sh.hIcon != IntPtr.Zero)
                    {
                        using var ico = Icon.FromHandle(sh.hIcon);
                        made = new Bitmap(ico.ToBitmap(), px, px);
                        DestroyIcon(sh.hIcon);
                    }
                }
                catch { made = null; }
            }
        }
        made ??= Placeholder(appName, px);
        SizedCache[(key, px)] = made;
        return made;
    }

    private static readonly Color[] PlaceholderColors =
    {
        Color.FromArgb(51, 90, 150), Color.FromArgb(96, 84, 158), Color.FromArgb(44, 120, 128),
        Color.FromArgb(150, 90, 56), Color.FromArgb(120, 70, 120), Color.FromArgb(60, 120, 90),
    };

    private static Bitmap Placeholder(string name, int px)
    {
        var bmp = new Bitmap(px, px);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        int h = 0;
        foreach (char c in name) h += c;
        var color = PlaceholderColors[Math.Abs(h) % PlaceholderColors.Length];
        float m = Math.Max(1, px * 0.03f);
        DrawUtil.FillRound(g, new RectangleF(m, m, px - 2 * m, px - 2 * m), px * 0.22f, color);
        string ch = name.Trim().Length > 0 ? name.Trim()[..1].ToUpperInvariant() : "?";
        using var f = new Font("Microsoft YaHei UI", px * 0.42f, FontStyle.Bold);
        TextRenderer.DrawText(g, ch, f, new Rectangle(0, 0, px, px), Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        return bmp;
    }

    /// <summary>程序主图标(从自身 exe 提取;任务栏/标题栏/托盘统一)。</summary>
    public static Icon ProgramIcon()
    {
        try
        {
            string path = Environment.ProcessPath ?? "";
            if (File.Exists(path))
            {
                using var ico = Icon.ExtractAssociatedIcon(path);
                if (ico != null) return new Icon(ico, 32, 32);
            }
        }
        catch { }
        return SystemIcons.Application;
    }

    public static Icon SmallProgramIcon()
    {
        try
        {
            string path = Environment.ProcessPath ?? "";
            if (File.Exists(path))
            {
                using var ico = Icon.ExtractAssociatedIcon(path);
                if (ico != null) return new Icon(ico, 16, 16);
            }
        }
        catch { }
        return SystemIcons.Application;
    }
}
