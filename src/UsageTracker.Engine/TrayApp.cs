using UsageTracker.Core.Data;
using UsageTracker.Engine.UI;

namespace UsageTracker.Engine;

/// <summary>托盘宿主(WinExe,无窗口):NotifyIcon + 自绘深色圆角菜单;主窗口懒加载。</summary>
internal static class TrayApp
{
    private static MainForm? _main;
    private static AppSettings _settings = null!;
    private static string _dataDir = "";
    private static EngineLog? _log;

    public static int Run(string dataDir)
    {
        _dataDir = dataDir;
        _settings = AppSettings.Load(dataDir);
        Theme.Apply(_settings.Theme, _settings.FontScale);

        using var cts = new CancellationTokenSource();
        var log = new EngineLog(Path.Combine(AppPaths.LogDir(dataDir), "engine.log"));
        _log = log;

        var engineThread = new Thread(() =>
        {
            try { EngineHost.Run(cts.Token, dataDir, log); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { log.Info($"[fatal] {ex}"); }
        });
        engineThread.IsBackground = true;
        engineThread.SetApartmentState(ApartmentState.STA); // Edge UIA 读取需 STA
        engineThread.Start();

        using var icon = new NotifyIcon
        {
            Icon = AppIcons.SmallProgramIcon(),
            Visible = true,
            Text = "UsageTracker — 应用运行时长记录",
            ContextMenuStrip = null,
        };

        icon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && e.X < 0) ShowMainMenu();   // 托盘右键
            if (e.Button == MouseButtons.Right) ShowMainMenu();
        };
        icon.DoubleClick += (_, _) => EnsureMain(TodayKey);

        // 静默启动开关(需求 6.3):关闭 = 启动时显示主界面(延后到消息循环启动后)
        if (!_settings.SilentStart)
        {
            string startPage = TodayKey;
            string? ep = Environment.GetEnvironmentVariable("USAGETRACKER_START_PAGE");
            if (ep != null)
                startPage = ep switch
                {
                    "allapps" => MainForm.AllApps,
                    "blacklist" => MainForm.Blacklist,
                    "settings" => MainForm.Settings,
                    _ => TodayKey,
                };
            var openTimer = new System.Windows.Forms.Timer { Interval = 500 };
            openTimer.Tick += (_, _) => { openTimer.Stop(); EnsureMain(startPage); };
            openTimer.Start();
        }

        Application.Run();

        cts.Cancel();
        engineThread.Join(TimeSpan.FromSeconds(5));
        icon.Visible = false;
        return 0;
    }

    private const string TodayKey = MainForm.Today;

    private static void EnsureMain(string page)
    {
        try
        {
            if (_main == null || _main.IsDisposed)
            {
                _main = new MainForm(_dataDir, _settings);
                _main.FormClosed += (_, _) => _main = null;
            }
            _main.SwitchPage(page);
            if (!_main.Visible) _main.Show();
            if (_main.WindowState == FormWindowState.Minimized) _main.WindowState = FormWindowState.Normal;
            _main.Activate();
        }
        catch (Exception ex)
        {
            _log?.Info("[UI错误] " + ex);
            try
            {
                using var n = new NotifyIcon { Visible = true, Icon = AppIcons.SmallProgramIcon() };
                n.ShowBalloonTip(3000, "UsageTracker", "界面打开失败,详见日志(数据不受影响)", ToolTipIcon.Error);
                n.Visible = false;
            }
            catch { }
        }
    }

    private static void ShowMainMenu()
    {
        var menu = new TrayMenuPopup(new[]
        {
            (GlyphKind.Clock, "今日概览", (Action)(() => EnsureMain(MainForm.Today))),
            (GlyphKind.Grid, "全部应用概览", (Action)(() => EnsureMain(MainForm.AllApps))),
            (GlyphKind.Ban, "黑名单", (Action)(() => EnsureMain(MainForm.Blacklist))),
            (GlyphKind.Sliders, "设置", (Action)(() => EnsureMain(MainForm.Settings))),
            (GlyphKind.Power, "退出", (Action)(() => Application.Exit())),
        });
        menu.ShowAt(Cursor.Position);
    }
}
