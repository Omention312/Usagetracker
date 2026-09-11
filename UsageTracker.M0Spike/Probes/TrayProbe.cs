namespace UsageTracker.Spike.Probes;

/// <summary>探针③b:WinForms 托盘 NotifyIcon 冒烟测试(需 STA)。</summary>
internal static class TrayProbe
{
    public static int Run(string[] args)
    {
        int seconds = args.Length > 1 && int.TryParse(args[1], out var s) ? s : 25;
        Console.WriteLine($"[probe-tray] 显示托盘图标 {seconds}s 后自动退出(如需手动验证请右键图标)");

        using var icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "UsageTracker M0 Spike",
        };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("今日概览(占位)", null, (_, _) =>
            System.Windows.Forms.MessageBox.Show("M0 spike:此处将来是今日概览窗口。", "UsageTracker"));
        menu.Items.Add("退出", null, (_, _) => System.Windows.Forms.Application.Exit());
        icon.ContextMenuStrip = menu;
        icon.ShowBalloonTip(1500, "UsageTracker M0", $"托盘骨架 OK,{seconds}s 后自动退出", System.Windows.Forms.ToolTipIcon.Info);

        using var timer = new System.Threading.Timer(_ => System.Windows.Forms.Application.Exit(), null, seconds * 1000, System.Threading.Timeout.Infinite);
        System.Windows.Forms.Application.Run();
        Console.WriteLine("[probe-tray] 消息循环正常结束 → PASS(若托盘无异常)");
        return 0;
    }
}
