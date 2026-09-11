using System.Text;

namespace UsageTracker.Engine.UI;

/// <summary>
/// 今日概览:顶部汇总 → 今日应用(列表:列头+行,行=卡片+图标+名称+三列时长+黑名单按钮)
/// → 后台运行程序(可折叠,默认收起,同列表样式)。
/// 5s 定时刷新仅更新三列时长数字(不重建/不闪烁/不改菜单开合)。
/// </summary>
internal sealed class TodayPage : PageBase
{
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Action<string> _navigate;
    private bool _refreshing;
    private readonly Dictionary<string, AppTableRow> _todayRows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AppTableRow> _bgRows = new(StringComparer.OrdinalIgnoreCase);
    private FlowLayoutPanel _bgList = null!;
    private HashSet<string> _bgApps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _bgSince = new(StringComparer.OrdinalIgnoreCase);

    public TodayPage(string dataDir, Action<string> navigate) : base(dataDir, "今日概览")
    {
        _navigate = navigate;
        Flow.Margin = new Padding(16, 8, 8, 8);
        _timer = new System.Windows.Forms.Timer { Interval = 5000 };
        _timer.Tick += (_, _) => { if (!_refreshing) RefreshPartial(); };
        _timer.Start();
        Load += (_, _) => Build();
    }

    public override void OnShow()
    {
        Build();
        if (IsHandleCreated)
        {
            try
            {
                BeginInvoke(() =>
                {
                    if (IsDisposed) return;
                    ContentChanged();
                    Invalidate(true);
                    Refresh();
                });
            }
            catch { }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }

    private static string[] Dur3(long fgMs, long bgMs, long runMs)
        => new[] { TimeFmt.Hms(fgMs), TimeFmt.Hms(bgMs), TimeFmt.Hms(runMs) };

    private void Build()
    {
        Clear();
        _todayRows.Clear();

        var today = Db.TodayTrue();
        long todayFg = today.Sum(x => x.FgMs);
        long todayRun = today.Sum(x => x.RunMs);

        var top = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(2, 0, 0, 6), BackColor = Theme.Current.Bg };
        top.Controls.Add(StatsSinceLine("开始统计时间"));
        top.Controls.Add(MakeLabel($"今日:前台使用 {TimeFmt.Hms(todayFg)} · 总运行 {TimeFmt.Hms(todayRun)}", 10.5f, false, Theme.Current.TextDim, 26));
        Flow.Controls.Add(top);

        var secApps = new ToggleSection($"今日应用({today.Count})");
        var bodyApps = NewListFlow();
        secApps.Bind(bodyApps);
        secApps.Toggled += () => ContentChanged();
        secApps.Open = true;
        Flow.Controls.Add(secApps);
        Flow.Controls.Add(bodyApps);

        var exeMap = Db.MostRecentExeMap(today.Select(x => x.App).ToList());
        if (today.Count > 0)
        {
            var header = new ColumnHeader();
            header.Width = 900;
            bodyApps.Controls.Add(header);
        }
        foreach (var (app, fgMs, bgMs, runMs, cnt, firstMs) in today)
        {
            string? exe = exeMap.TryGetValue(app, out var p) ? p : null;
            string start = firstMs > 0 ? $"开始记录时间:{DateTimeOffset.FromUnixTimeMilliseconds(firstMs).ToLocalTime():HH:mm:ss}" : "";
            var d = Dur3(fgMs, bgMs, runMs);
            var row = new AppTableRow(AppIcons.Get32(exe, app), app,
                cnt > 0 ? $"今日 {cnt} 次" : "", d[0], d[1], d[2],
                "加入黑名单", true, () => ToggleBlack(app));
            row.Start = start;   // 独立一行,不再拼接到长句被截断
            _todayRows[app] = row;
            bodyApps.Controls.Add(row);
        }
        if (today.Count == 0)
        {
            bodyApps.Controls.Add(MakeLabel("今日还没有使用记录,去用一下应用吧", 9.5f, false, Theme.Current.TextDim, 30));
        }

        var secBg = new ToggleSection("后台运行程序");
        var bodyBg = NewListFlow();
        secBg.Bind(bodyBg);
        secBg.Toggled += () => ContentChanged();
        secBg.Open = false;
        Flow.Controls.Add(secBg);
        Flow.Controls.Add(bodyBg);
        _bgList = bodyBg;
        RebuildBgList();

        ContentChanged();
        DumpTodayData(today);
    }

    /// <summary>诊断用:把"界面口径"算出的今日数据落进 logs\layout.log,便于与 DB 原始行对照(仅在 DEBUGLAYOUT 开启时生效)。</summary>
    private void DumpTodayData(List<(string App, long FgMs, long BgMs, long RunMs, long Cnt, long FirstMs)> today)
    {
        if (!LayoutLog.Enabled) return;
        var sb = new StringBuilder();
        sb.Append($"[D][TodayTrue] apps={today.Count}");
        var suspects = today.Where(t => t.RunMs >= 2 * 3600_000L).OrderByDescending(t => t.RunMs).ToList();
        sb.Append($" 运行≥2h 的应用 {suspects.Count} 个(下面列这些 + 前 5 名):");
        foreach (var x in suspects.Take(20).Concat(today.Take(5)).Distinct())
        {
            sb.Append($"\n[D] {x.App}: fg={TimeFmt.Hms(x.FgMs)} bg={TimeFmt.Hms(x.BgMs)} run={TimeFmt.Hms(x.RunMs)} cnt={x.Cnt} " +
                      $"first={(x.FirstMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(x.FirstMs).ToLocalTime().ToString("HH:mm:ss") : "-")}");
        }
        LayoutLog.Write(DataDir, sb.ToString());
    }

    private static FlowLayoutPanel NewListFlow() => new()
    {
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoSize = true,
        AutoScroll = false,
        Margin = new Padding(0, 0, 0, 4),
        BackColor = Theme.Current.Bg,
    };

    private void RebuildBgList()
    {
        _bgList.Controls.Clear();
        _bgRows.Clear();
        _bgSince.Clear();
        var running = Db.RunningApps();
        _bgApps = running.Select(r => r.App).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (running.Count > 0)
        {
            var header = new ColumnHeader();
            header.Width = 900;
            _bgList.Controls.Add(header);
        }
        var exeMap = Db.MostRecentExeMap(running.Select(r => r.App).ToList());
        var todayStats = Db.TodayTrue().ToDictionary(x => x.App, StringComparer.OrdinalIgnoreCase);
        foreach (var (app, since) in running)
        {
            _bgSince[app] = since;
            var row = new AppTableRow(AppIcons.Get32(exeMap.TryGetValue(app, out var exe) ? exe : null, app),
                app, "后台运行中", "0m", "0m", "0m");
            _bgRows[app] = row;
            _bgList.Controls.Add(row);
        }
        if (running.Count == 0)
        {
            _bgList.Controls.Add(MakeLabel("没有后台运行的应用", 9f, false, Theme.Current.TextDim, 30));
        }
        UpdateBgDurations(todayStats);
    }

    private void UpdateBgDurations(Dictionary<string, (string App, long FgMs, long BgMs, long RunMs, long Cnt, long FirstMs)> today)
    {
        foreach (var (app, row) in _bgRows)
        {
            if (!_bgSince.TryGetValue(app, out long since)) continue;
            today.TryGetValue(app, out var st);
            row.Update(TimeFmt.Hms(st.FgMs), TimeFmt.Hms(st.BgMs), TimeFmt.Hms(st.RunMs));
        }
    }

    private void RefreshPartial()
    {
        if (IsDisposed || !IsHandleCreated) return;
        _refreshing = true;
        try
        {
            var stats = Db.TodayTrue().ToDictionary(x => x.App, StringComparer.OrdinalIgnoreCase);
            foreach (var (app, row) in _todayRows)
            {
                if (stats.TryGetValue(app, out var s))
                {
                    var d = Dur3(s.FgMs, s.BgMs, s.RunMs);
                    row.Update(d[0], d[1], d[2]);
                }
            }
            UpdateBgDurations(stats);
        }
        finally { _refreshing = false; }
    }

    private void ToggleBlack(string app)
    {
        Db.SetIgnored(app, true);
        _navigate("blacklist");
    }

    public override void RefreshTheme()
    {
        BackColor = Theme.Current.Bg;
        Build();
    }
}
