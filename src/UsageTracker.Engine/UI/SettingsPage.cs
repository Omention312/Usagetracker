using System.Diagnostics;
using System.IO;
using UsageTracker.Core.Data;

namespace UsageTracker.Engine.UI;

/// <summary>设置页(需求 6)。</summary>
internal sealed class SettingsPage : PageBase
{
    private readonly AppSettings _settings;
    private readonly Action _styleChanged;
    private Label? _exportStatus;

    public SettingsPage(string dataDir, AppSettings settings, Action styleChanged)
        : base(dataDir, "设置")
    {
        _settings = settings;
        _styleChanged = styleChanged;
    }

    public override void OnShow() => Build();

    private static bool Skip(string k)
    {
        string? s = Environment.GetEnvironmentVariable("USAGETRACKER_SETTINGS_SKIP");
        return s != null && s.Contains(k, StringComparison.OrdinalIgnoreCase);
    }

    private void Build()
    {
        Clear();
        Flow.Margin = new Padding(18, 10, 18, 8);
        if (!Skip("a"))
        {
            AddSection("外观");

        AddControlRow("主题模式", () =>
        {
            var seg = new SegmentedControl("浅色", "深色", "跟随系统") { Width = 240 };
            seg.SelectedIndex = _settings.Theme switch { "light" => 0, "dark" => 1, _ => 2 };
            seg.SelectionChanged += (_, _) =>
            {
                _settings.Theme = seg.SelectedIndex switch { 0 => "light", 1 => "dark", _ => "system" };
                _settings.Save();
                _styleChanged();
            };
            return seg;
        }, "浅色 / 深色 / 跟随 Windows 主题");

        AddControlRow("字体大小", () =>
        {
            var seg = new SegmentedControl("小", "标准", "大", "特大") { Width = 260 };
            seg.SelectedIndex = _settings.FontScale switch { <= 0.9 => 0, <= 1.05 => 1, <= 1.2 => 2, _ => 3 };
            seg.SelectionChanged += (_, _) =>
            {
                _settings.FontScale = seg.SelectedIndex switch { 0 => 0.9, 1 => 1.0, 2 => 1.15, _ => 1.3 };
                _settings.Save();
                _styleChanged();
            };
            return seg;
        }, "整体界面字体缩放");
        }

        if (!Skip("s"))
        {
            AddSection("启动");
        AddSwitchRow("开机自启动", () => RegistryUtil.IsEnabled(),
            v => { if (v) RegistryUtil.Enable(Environment.ProcessPath ?? ""); else RegistryUtil.Disable(); },
            "登录 Windows 后自动在后台运行");
        AddSwitchRow("后台静默启动", () => _settings.SilentStart, v =>
        {
            _settings.SilentStart = v;
            _settings.Save();
        }, "启动时不弹出主界面,仅在托盘运行(下次启动生效)");
        }

        if (!Skip("d"))
        {
            AddSection("数据");
        AddControlRow("导出应用使用时长", () =>
        {
            var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Width = 700 };
            var btn = new ActionButton("导出 CSV", ActionButton.Kind.Accent) { Width = 110 };
            btn.Clicked += (_, _) => Export();
            panel.Controls.Add(btn);
            _exportStatus = MakeLabel("", 8.5f, false, Theme.Current.TextDim, 26);
            panel.Controls.Add(_exportStatus);
            return panel;
        }, "按日导出:日期,应用,前台秒,总运行秒,会话次数(CSV)");

        AddControlRow("数据目录", () =>
        {
            var btn = new ActionButton("打开数据目录", ActionButton.Kind.Ghost) { Width = 130 };
            btn.Clicked += (_, _) =>
            {
                try
                {
                    string dir = AppPaths.DataDir();
                    Directory.CreateDirectory(dir);
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
                }
                catch { }
            };
            return btn;
        }, "数据库/日志/备份/报表所在文件夹(默认 %LOCALAPPDATA%\\UsageTracker)");
        }
        ContentChanged();
    }

    private void Export()
    {
        try
        {
            using var dlg = new SaveFileDialog
            {
                Title = "导出应用使用时长",
                Filter = "CSV 文件 (*.csv)|*.csv",
                FileName = $"usage-export-{DateTime.Now:yyyyMMdd}.csv",
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            int rows = Db.ExportDailyCsv(dlg.FileName);
            if (_exportStatus != null) _exportStatus.Text = $"已导出 {rows} 行 → {dlg.FileName}";
        }
        catch (Exception ex)
        {
            if (_exportStatus != null) _exportStatus.Text = "导出失败:" + ex.Message;
        }
    }

    private void AddSection(string title)
    {
        var l = MakeLabel(title, 12f, true, Theme.Current.Accent, 36);
        l.Margin = new Padding(4, 27, 4, 4); // 标题整体下移(顶部曾被裁)
        Flow.Controls.Add(l);
    }

    private void AddSwitchRow(string label, Func<bool> get, Action<bool> set, string desc)
    {
        AddControlRow(label, () =>
        {
            var sw = new SwitchControl { Value = get() };
            sw.Changed += (_, _) => set(sw.Value);
            return sw;
        }, desc);
    }

    private void AddControlRow(string label, Func<Control> controlFactory, string desc)
    {
        float us = Math.Max(1f, Theme.Current.UiScale);
        var row = new Panel { Height = (int)(72 * us), Margin = new Padding(2, 5, 2, 5), BackColor = Theme.Current.Bg };
        var lbl = MakeLabel(label, 10f, false, Color.Empty, (int)(28 * us));
        lbl.Location = new Point(6, (int)(16 * us));   // 文字下移(顶部曾被裁)
        lbl.Width = (int)(260 * us);
        var ctrl = controlFactory();
        if (ctrl.Width > 0) ctrl.Width = (int)(ctrl.Width * us);
        ctrl.Location = new Point((int)(272 * us), Math.Max(3, (row.Height - ctrl.Height) / 2));
        var d = MakeLabel(desc, 8f, false, Theme.Current.TextFaint, (int)(22 * us));
        d.Location = new Point(6, (int)(47 * us));    // 说明文字同步下移
        d.Width = row.Width - 16;
        row.Controls.Add(lbl);
        row.Controls.Add(ctrl);
        row.Controls.Add(d);
        row.Resize += (_, _) => d.Width = row.Width - 16;
        Flow.Controls.Add(row);
    }

    public override void RefreshTheme()
    {
        BackColor = Theme.Current.Bg;
        Build();
    }
}
