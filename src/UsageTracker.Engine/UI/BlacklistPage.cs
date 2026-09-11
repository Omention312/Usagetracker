using System.IO;

namespace UsageTracker.Engine.UI;

/// <summary>黑名单页(需求 7.3):查看当前黑名单、取消恢复。</summary>
internal sealed class BlacklistPage : PageBase
{
    public BlacklistPage(string dataDir) : base(dataDir, "黑名单")
    {
    }

    public override void OnShow() => Build();

    private void Build()
    {
        Clear();
        Flow.Margin = new Padding(18, 8, 18, 8);
        var note = MakeLabel("加入黑名单的应用:从加入时刻起不再记录使用时长,也不再出现在“今日概览/全部应用概览”。历史数据保留(可随时取消后继续累计)。", 8.5f, false, Theme.Current.TextDim, 32);
        Flow.Controls.Add(note);

        var list = Db.IgnoredApps();
        if (list.Count == 0)
        {
            var empty = MakeLabel("当前没有黑名单应用。可在“今日概览/全部应用概览”条目右侧点击“加入黑名单”。", 9.5f, false, Theme.Current.TextDim, 40);
            Flow.Controls.Add(empty);
        }
        foreach (var app in list)
        {
            string? exe = Db.MostRecentExe(app);
            var row = new AppTableRow(AppIcons.Get32(exe, app), app, exe == null ? "已加入黑名单,不再记录" : Path.GetFileName(exe),
                "", "", "", "取消黑名单", false, () =>
                {
                    Db.SetIgnored(app, false);
                    Build();
                });
            Add(row);
        }
        ContentChanged();
    }

    public override void RefreshTheme() { BackColor = Theme.Current.Bg; Build(); }
}
