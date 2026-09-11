using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using UsageTracker.Core.Data;

namespace UsageTracker.Engine.UI;

/// <summary>主窗口(需求 3.1):左侧导航 + 四页内容切换。深色/浅色主题统一由 Theme.Current 驱动。</summary>
internal sealed class MainForm : Form
{
    public const string Today = "today";
    public const string AllApps = "allapps";
    public const string Blacklist = "blacklist";
    public const string Settings = "settings";

    private readonly string _dataDir;
    private readonly AppSettings _settings;
    private readonly NavMenu _nav = new();
    private readonly Panel _host = new();
    private string _key = Today;

    public MainForm(string dataDir, AppSettings settings)
    {
        _dataDir = dataDir;
        _settings = settings;
        Text = "UsageTracker — 应用运行时长记录";
        Icon = AppIcons.ProgramIcon();
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Current.Bg;
        DoubleBuffered = true;

        // DPI 系数:初始化(可用环境变量 USAGETRACKER_DPI 覆盖为 96/120/144 等做实验档)+ 系统 DPI 变化重建
        try
        {
            float f = 1f;
            string? env = Environment.GetEnvironmentVariable("USAGETRACKER_DPI");
            if (env != null && float.TryParse(env, out var ef) && ef >= 96) f = ef / 96f;
            else { using var g = CreateGraphics(); f = g.DpiX / 96f; }
            Theme.Current.DpiFactor = Math.Max(0.5f, f);
        }
        catch { }
        float us = Math.Max(1f, Theme.Current.DpiFactor);
        Size = new Size((int)(1180 * us), (int)(760 * us));
        MinimumSize = new Size((int)(960 * us), (int)(620 * us));
        DpiChanged += (_, e) =>
        {
            Theme.Current.DpiFactor = e.DeviceDpiNew / 96f;
            try { ReloadStyle(); } catch { }
        };

        _nav.Dock = DockStyle.Left;
        _nav.Width = (int)(208 * us);
        _nav.SetItems(new[]
        {
            new NavMenu.Item(Today, "今日概览", GlyphKind.Clock),
            new NavMenu.Item(AllApps, "全部应用概览", GlyphKind.Grid),
            new NavMenu.Item(Blacklist, "黑名单", GlyphKind.Ban),
            new NavMenu.Item(Settings, "设置", GlyphKind.Sliders),
        }, Today);
        _nav.Navigate = SwitchPage;

        _host.Dock = DockStyle.Fill;
        _host.BackColor = Theme.Current.Bg;
        _host.Padding = new Padding(0, 0, 0, 0);

        Controls.Add(_host);
        Controls.Add(_nav);

        FormClosing += (object? sender, FormClosingEventArgs e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;   // 关闭窗口 = 最小化到托盘
                Hide();
            }
        };

        // 命令文件后端(与界面按钮同处理器):验收/自动化用,详见 ApplyCmdFile
        var cmdTimer = new System.Windows.Forms.Timer { Interval = 400 };
        cmdTimer.Tick += (_, _) => ApplyCmdFile();
        cmdTimer.Start();
    }

    private void ApplyCmdFile()
    {
        string path = Path.Combine(_dataDir, "ui-cmd.json");
        if (!File.Exists(path)) return;
        try
        {
            var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            bool refreshed = false;

            if (root.TryGetProperty("page", out var pg) && pg.ValueKind == JsonValueKind.String)
                SwitchPage(pg.GetString()!);

            if (root.TryGetProperty("theme", out var th) && th.ValueKind == JsonValueKind.String)
            {
                _settings.Theme = th.GetString()!;
                _settings.Normalize();
                Theme.Apply(_settings.Theme, _settings.FontScale);
                _settings.Save();
                refreshed = true;
            }
            if (root.TryGetProperty("fontScale", out var fs) && fs.ValueKind == JsonValueKind.Number)
            {
                _settings.FontScale = fs.GetDouble();
                _settings.Normalize();
                Theme.Apply(_settings.Theme, _settings.FontScale);
                _settings.Save();
                refreshed = true;
            }
            if (root.TryGetProperty("silent", out var sl) && sl.ValueKind == JsonValueKind.True || sl.ValueKind == JsonValueKind.False)
            {
                _settings.SilentStart = sl.GetBoolean();
                _settings.Save();
            }
            if (root.TryGetProperty("autostart", out var au) && au.ValueKind == JsonValueKind.String)
            {
                if (au.GetString() == "on") RegistryUtil.Enable(Environment.ProcessPath ?? "");
                else RegistryUtil.Disable();
            }
            if (root.TryGetProperty("export", out var ex) && ex.ValueKind == JsonValueKind.String)
            {
                var db = new UiDb(_dataDir);
                int rows = db.ExportDailyCsv(ex.GetString()!);
                File.WriteAllText(ex.GetString()! + ".rows.txt", rows.ToString());
            }
            if (root.TryGetProperty("blacklistAdd", out var ba) && ba.ValueKind == JsonValueKind.String)
            {
                var db = new UiDb(_dataDir);
                db.SetIgnored(ba.GetString()!, true);
                refreshed = true;
            }
            if (root.TryGetProperty("blacklistRemove", out var br) && br.ValueKind == JsonValueKind.String)
            {
                var db = new UiDb(_dataDir);
                db.SetIgnored(br.GetString()!, false);
                refreshed = true;
            }
            if (root.TryGetProperty("openDir", out var od) && od.ValueKind == JsonValueKind.True)
            {
                try
                {
                    string dir = AppPaths.DataDir();
                    Directory.CreateDirectory(dir);
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
                }
                catch { }
            }

            if (refreshed) ReloadStyle();
        }
        catch { }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    public void SwitchPage(string key)
    {
        _key = key;
        foreach (Control c in _host.Controls) c.Dispose();
        _host.Controls.Clear();

        PageBase page = key switch
        {
            Today => new TodayPage(_dataDir, SwitchPage),
            AllApps => new AllAppsPage(_dataDir, SwitchPage),
            Blacklist => new BlacklistPage(_dataDir),
            Settings => new SettingsPage(_dataDir, _settings, ReloadStyle),
            _ => new TodayPage(_dataDir, SwitchPage),
        };
        page.Dock = DockStyle.Fill;
        page.BackColor = Theme.Current.Bg;
        _host.Controls.Add(page);
        _nav.Select(key);
        page.OnShow();
    }

    /// <summary>主题/字号变化后整体重建当前页。</summary>
    public void ReloadStyle()
    {
        // 关键:先把设置真正应用到 Theme(含字号缩放),否则切换无效果
        Theme.Apply(_settings.Theme, _settings.FontScale);
        _settings.Normalize();
        BackColor = Theme.Current.Bg;
        _host.BackColor = Theme.Current.Bg;
        _nav.Invalidate();
        SwitchPage(_key);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Control && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D4)
        {
            SwitchPage((e.KeyCode - Keys.D1) switch
            {
                0 => Today,
                1 => AllApps,
                2 => Blacklist,
                _ => Settings,
            });
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    public static bool SystemPrefersLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var v = key?.GetValue("AppsUseLightTheme");
            return v is int i && i == 1;
        }
        catch { return false; }
    }
}
