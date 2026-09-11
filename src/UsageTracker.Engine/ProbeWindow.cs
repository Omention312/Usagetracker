using System.Runtime.InteropServices;

namespace UsageTracker.Engine;

/// <summary>
/// 前台验收探针窗口:显示一个置顶小窗并持续尝试把自己置为前台,
/// 供 T1(前台时长精度)自动化验收使用。运行 N 毫秒后自动关闭。
/// </summary>
internal static class ProbeWindow
{
    public static int Run(string[] args)
    {
        int ms = args.Length > 1 && int.TryParse(args[1], out var m) ? m : 20_000;
        int left = ms / 1000;

        using var form = new Form
        {
            Text = "UsageTracker 前台验收探针",
            StartPosition = FormStartPosition.CenterScreen,
            TopMost = true,
            ClientSize = new Size(520, 200),
        };
        var lbl = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 14F),
            Text = $"前台验收探针 pid={Environment.ProcessId}\n剩余 {left}s 后自动关闭",
        };
        form.Controls.Add(lbl);

        var closeTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        closeTimer.Tick += (_, _) =>
        {
            left--;
            lbl.Text = $"前台验收探针 pid={Environment.ProcessId}\n剩余 {left}s 后自动关闭";
            if (left <= 0) form.Close();
        };

        // 持续尝试把自己变成前台(普通方式 + AttachThreadInput 抢占)
        var focusTimer = new System.Windows.Forms.Timer { Interval = 250 };
        focusTimer.Tick += (_, _) => TryActivate(form);

        form.Shown += (_, _) =>
        {
            focusTimer.Start();
            closeTimer.Start();
        };
        form.FormClosed += (_, _) =>
        {
            focusTimer.Stop();
            closeTimer.Stop();
        };

        Application.Run(form);
        return 0;
    }

    /// <summary>尽力把窗口置为前台:先常规 SetForegroundWindow,再用 AttachThreadInput 跨越前台锁。</summary>
    private static void TryActivate(Form form)
    {
        IntPtr h = form.Handle;
        NativeMethods.ShowWindow(h, 9 /*SW_RESTORE*/);
        NativeMethods.ShowWindow(h, 5 /*SW_SHOW*/);
        NativeMethods.BringWindowToTop(h);
        NativeMethods.SetForegroundWindow(h);
        try { form.Activate(); } catch { }
        try { form.TopMost = true; form.TopMost = false; form.TopMost = true; } catch { }

        uint cur = NativeMethods.GetCurrentThreadId();
        IntPtr fgWin = NativeMethods.GetForegroundWindow();
        if (fgWin == h) return;
        uint fgThread = NativeMethods.GetWindowThreadProcessId(fgWin, out _);
        if (fgThread != cur && fgThread != 0)
        {
            NativeMethods.AttachThreadInput(cur, fgThread, true);
            NativeMethods.BringWindowToTop(h);
            NativeMethods.SetForegroundWindow(h);
            NativeMethods.AttachThreadInput(cur, fgThread, false);
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] internal static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);
    }
}
