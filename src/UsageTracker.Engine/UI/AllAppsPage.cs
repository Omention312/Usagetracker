using System.IO;
using Microsoft.Data.Sqlite;
using UsageTracker.Core.Data;

namespace UsageTracker.Engine.UI;

/// <summary>全部应用概览(需求 8):可统计口径说明 + 所有可识别应用(含 0 分钟/未使用)。</summary>
internal sealed class AllAppsPage : PageBase
{
    private readonly Action<string> _navigate;
    private readonly HashSet<string> _noStats = new(StringComparer.OrdinalIgnoreCase);

    private sealed class Ent
    {
        public string Title = "";      // 显示名
        public string? ExePath;
        public string Sub = "";
        public long FgMs, RunMs, Cnt;
        public bool Seen;
    }

    public AllAppsPage(string dataDir, Action<string> navigate) : base(dataDir, "全部应用概览")
    {
        _navigate = navigate;
    }

    public override void OnShow() => Build();

    private List<Ent> LoadEntries()
    {
        // 与今日列头一致:该页三列也使用“今日”口径(前台/后台=运行-前台/总计=今日运行)
        var today = Db.TodayTrue();
        var stats = new Dictionary<string, (long FgMs, long RunMs, long Cnt)>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in today) stats[t.App] = (t.FgMs, t.RunMs, t.Cnt);
        var appRows = new List<(string Name, string? Exe, bool Ignored)>();
        using (var c = new SqliteConnection($"Data Source={AppPaths.DbFile(DataDir)}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT app_name, exe_path, is_ignored FROM app_info GROUP BY app_name";
            using var r = cmd.ExecuteReader();
            while (r.Read()) appRows.Add((r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetInt64(2) == 1));
        }

        var list = new List<Ent>();
        var seenKey = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var byName = appRows.ToDictionary(x => x.Name, x => x, StringComparer.OrdinalIgnoreCase);

        void AddRec(string title, string? exe, bool ignored, long fg, long run, long cnt, string sub, bool markSeen)
        {
            if (ignored) return; // 黑名单不在此页出现(需求 7.2)
            if (seenKey.Add(title + "|" + (exe ?? "").ToLowerInvariant()))
                list.Add(new Ent { Title = title, ExePath = exe, FgMs = fg, RunMs = run, Cnt = cnt, Sub = sub, Seen = markSeen });
        }

        // 1) 已记录应用(app_info 登记过的)
        foreach (var (name, exe, ignored) in appRows)
        {
            stats.TryGetValue(name, out var s);
            AddRec(name, exe, ignored, s.FgMs, s.RunMs, s.Cnt, exe == null ? "已记录" : Path.GetFileName(exe), true);
        }

        // 2) 注册表“已安装应用”中尚未出现过的(供“未使用”展示)
        foreach (var (display, exe) in ExeUtil.EnumerateInstalledApps())
        {
            if (exe == null || seenKey.Contains(display + "|" + exe.ToLowerInvariant())) continue;
            string exeKey = Path.GetFileNameWithoutExtension(exe);
            bool matched = byName.ContainsKey(display) || byName.ContainsKey(exeKey);
            string? hit = null;
            if (matched)
                hit = byName.ContainsKey(display) ? display : exeKey;
            if (hit != null)
            {
                // 已在第 1 步登记过的(以 exe 判重后)直接跳过;这里处理 名称匹配但步骤1未加入的极端情况
                continue;
            }
            AddRec(display, exe, false, 0, 0, 0, "已安装 · 尚未记录到使用", false);
        }

        // 按前台时长降序(未使用垫底),并标记 0 时长口径
        list.Sort((a, b) => b.FgMs.CompareTo(a.FgMs));
        return list;
    }

    private void Build()
    {
        Clear();
        _noStats.Clear();

        Flow.Margin = new Padding(18, 8, 18, 8);
        Flow.Controls.Add(StatsSinceLine("开始统计时间"));

        // 口径说明(需求 8.2)
        var note = MakeLabel("可统计时长 = 有进程/窗口/前台活动的应用;仅注册表探测到、从未运行的安装应用显示“0 分钟 / 未使用”。", 8.5f, false, Theme.Current.TextDim, 20);
        Flow.Controls.Add(note);

        var entries = LoadEntries();

        // 列表:直接置于外层(单一滚动条),行宽由 ScrollArea 递归设置
        var rowsFlow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoScroll = false,
            Margin = new Padding(0, 0, 0, 4),
            BackColor = Theme.Current.Bg,
        };
        Flow.Controls.Add(rowsFlow);

        var ignored = new HashSet<string>(Db.IgnoredApps(), StringComparer.OrdinalIgnoreCase);
        int shown = 0, cap = 600;
        if (entries.Count > 0)
        {
            rowsFlow.Controls.Add(new ColumnHeader { Width = 900 });
        }
        foreach (var e in entries)
        {
            if (shown >= cap) break;
            shown++;

            bool isIgnored = ignored.Contains(e.Title);
            string fg, bg, tot;
            if (e.FgMs > 0 || e.RunMs > 0)
            {
                fg = TimeFmt.Hms(e.FgMs);
                bg = TimeFmt.Hms(Math.Max(0, e.RunMs - e.FgMs));
                tot = TimeFmt.Hms(e.RunMs);
            }
            else
            {
                _noStats.Add(e.Title);
                fg = "0m"; bg = "0m"; tot = e.Sub.Length > 0 ? e.Sub : "未使用";
            }
            var row = new AppTableRow(AppIcons.Get32(e.ExePath, e.Title), e.Title, e.Sub, fg, bg, tot,
                isIgnored ? null : "加入黑名单", true, () => { if (!isIgnored) { Db.SetIgnored(e.Title, true); Build(); } });
            rowsFlow.Controls.Add(row);
        }
        if (shown == 0)
        {
            var none = MakeLabel(entries.Count == 0 ? "未找到已安装/已记录的应用" : "没有匹配的应用", 9f, false, Theme.Current.TextDim, 30);
            rowsFlow.Controls.Add(none);
        }
        ContentChanged();
    }

    public override void RefreshTheme() { BackColor = Theme.Current.Bg; Build(); }
}
