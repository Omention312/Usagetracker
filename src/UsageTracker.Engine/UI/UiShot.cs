using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace UsageTracker.Engine.UI;

/// <summary>
/// UI 自渲染验收:真实创建窗口(短暂显示)→ PrintWindow 捕获(DWM 渲染,不依赖 z-order)。
/// 用法:Engine.exe --ui-shot &lt;today|allapps|blacklist|settings|menu&gt; &lt;out.png&gt; [theme] [scale]
/// </summary>
internal static class UiShot
{
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }

    public static int Run(string[] args)
    {
        string page = args.Length > 1 ? args[1] : "today";
        string outPath = args.Length > 2 ? args[2] : Path.Combine(Path.GetTempPath(), $"ui-{page}.png");
        string theme = args.Length > 3 ? args[3] : "dark";
        double scale = args.Length > 4 && double.TryParse(args[4], out var s) ? s : 1.0;

        string dataDir = Core.Data.AppPaths.DataDir();
        var settings = AppSettings.Load(dataDir);
        settings.Theme = theme;
        settings.FontScale = scale;
        Theme.Apply(theme, scale);

        if (page == "menu")
        {
            using var popup = new TrayMenuPopup(new[]
            {
                (GlyphKind.Clock, "今日概览", (Action)(() => { })),
                (GlyphKind.Grid, "全部应用概览", (Action)(() => { })),
                (GlyphKind.Ban, "黑名单", (Action)(() => { })),
                (GlyphKind.Sliders, "设置", (Action)(() => { })),
                (GlyphKind.Power, "退出", (Action)(() => { })),
            });
            popup.StartPosition = FormStartPosition.Manual;
            popup.Location = new Point(40, 40);
            popup.Show();
            return Capture(popup, outPath);
        }

        if (page == "solo")
        {
            return Solo(page, outPath, dataDir, settings);
        }

        if (page == "settings")
        {
            // 设置页在 MainForm 内 WM_PRINT 捕获会死锁(仅捕获路径问题,真机正常)——走独立承载
            return Solo(page, outPath, dataDir, settings);
        }

        if (page == "widgets")
        {
            using var f = new Form { Size = new Size(700, 500), StartPosition = FormStartPosition.Manual, Location = new Point(70, 70), Text = "widgets" };
            var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill, Padding = new Padding(20) };
            flow.Controls.Add(new SegmentedControl("A", "B", "C") { Width = 240 });
            flow.Controls.Add(new SwitchControl { Value = true });
            var ab = new ActionButton("Action", ActionButton.Kind.Accent) { Width = 120 };
            flow.Controls.Add(ab);
            var ab2 = new ActionButton("Ghost", ActionButton.Kind.Ghost) { Width = 120 };
            flow.Controls.Add(ab2);
            f.Controls.Add(flow);
            f.Show();
            Console.WriteLine("widgets shown");
            int code = Capture(f, outPath);
            f.Close();
            return code;
        }

        using (var form = new MainForm(dataDir, settings))
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(60, 60);
            Console.WriteLine("step: switch=" + page);
            form.SwitchPage(page switch
            {
                "allapps" => MainForm.AllApps,
                "blacklist" => MainForm.Blacklist,
                "settings" => MainForm.Settings,
                _ => MainForm.Today,
            });
            Console.WriteLine("step: show");
            form.Show();
            Console.WriteLine("step: capture");
            int code = Capture(form, outPath);
            form.Close();
            Console.WriteLine("step: done");
            return code;
        }
    }

    private static int Solo(string page, string outPath, string dataDir, AppSettings settings)
    {
        using var f = new Form { Size = new Size(1100, 700), StartPosition = FormStartPosition.Manual, Location = new Point(90, 90), Text = "solo" };
        PageBase pg = page == "settings" ? new SettingsPage(dataDir, settings, () => { }) : new TodayPage(dataDir, _ => { });
        pg.Dock = DockStyle.Fill;
        f.Controls.Add(pg);
        f.Show();
        pg.OnShow();
        Console.WriteLine("solo shown");
        int code = Capture(f, outPath);
        f.Close();
        return code;
    }

    private static int Capture(Form form, string outPath)
    {
        Console.WriteLine("cap: pump");
        Pump(500);
        Console.WriteLine("cap: pump-done");
        int w = form.ClientSize.Width, h = form.ClientSize.Height;
        if (w <= 1 || h <= 1) return 1;
        using var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp))
        {
            IntPtr hdc = g.GetHdc();
            try { PrintWindow(form.Handle, hdc, 1 /*PW_CLIENTONLY*/); }
            finally { g.ReleaseHdc(hdc); }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        bmp.Save(outPath, ImageFormat.Png);
        Console.WriteLine("saved: " + outPath + " (" + w + "x" + h + ")");
        return 0;
    }

    private static void Pump(int ms)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
        {
            Application.DoEvents();
            Thread.Sleep(15);
        }
    }
}
