using System.Diagnostics;
using System.Text;

namespace UsageTracker.Engine.UI;

/// <summary>页面基类:标题 + 开始统计时间 + 可滚动内容区。</summary>
internal abstract class PageBase : UserControl
{
    protected readonly string DataDir;
    protected readonly UiDb Db;
    protected readonly FlowLayoutPanel Flow;
    private readonly ScrollArea _scroll;

    protected PageBase(string dataDir, string title)
    {
        DataDir = dataDir;
        Db = new UiDb(dataDir);
        DoubleBuffered = true;
        BackColor = Theme.Current.Bg;

        Flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Theme.Current.Bg,
            AutoSize = true,
            AutoScroll = false,
            Margin = new Padding(0),
            // 内容区顶部小幅留白(标题下不再出现大片空白栏)
            Padding = new Padding(0, 8, 0, 6),
        };
        _scroll = new ScrollArea(Flow) { Dock = DockStyle.Fill, BackColor = Theme.Current.Bg };
        Controls.Add(_scroll);

        var head = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Top, Height = (int)(56 * Math.Max(1f, Theme.Current.UiScale)), WrapContents = false, Padding = new Padding(26, (int)(10 * Math.Max(1f, Theme.Current.UiScale)), 16, 0), BackColor = Theme.Current.Bg };
        var headLabel = MakeLabel(title, 19f, true, Color.Empty);
        headLabel.Width = 700; // 标题宽度充足,防“字符被裁/丢失”
        head.Controls.Add(headLabel);
        Controls.Add(head);
        // 注意:**不要** SetChildIndex(head, 0)!
        // WinForms 的 Dock 按 z 序倒序布局,而 Controls.Add 的顺序(_scroll 先、head 后)正好让 Fill 最后布局、
        // 只拿到“页头以下”的区域。一旦把 head 提到最前,Fill 会先占满整个客户区,页头带就变成盖在内容上的
        // overlay(实测 scrollBounds=(0,0,1197,903) == page,内容顶部两行被遮 = 用户报的“大空白栏”)。
        // 若要动 Dock 顺序:保持 head 在 _scroll 之后 Add,或显式 SetChildIndex(_scroll, 0)。
    }

    /// <summary>Build 完成后通知滚动区域重新测量内容高度(并落盘布局诊断)。</summary>
    protected void ContentChanged()
    {
        _scroll.ContentChanged();
        DebugLayout("ContentChanged");
    }

    /// <summary>布局诊断:USAGETRACKER_DEBUGLAYOUT=1 时把矩形与量测值写入 logs\layout.log(WinExe 无控制台,必须落盘)。</summary>
    protected void DebugLayout(string tag)
    {
        if (!LayoutLog.Enabled) return;
        try
        {
            var sb = new StringBuilder();
            sb.Append($"[L][{tag}] page={Width}x{Height} scrollClient={_scroll.ClientSize.Width}x{_scroll.ClientSize.Height} " +
                      $"scrollBounds=({_scroll.Left},{_scroll.Top},{_scroll.Width},{_scroll.Height}) flow={Flow.Width}x{Flow.Height} " +
                      $"uiScale={Theme.Current.UiScale:0.###} dpi={DeviceDpi}");
            sb.Append(_scroll.GeometryInfo());
            int i = 0;
            foreach (Control c in Flow.Controls)
            {
                sb.Append($"\n[L]  #{i++} {c.GetType().Name} loc=({c.Left},{c.Top}) {c.Width}x{c.Height} vis={c.Visible} " +
                          $"margin=({c.Margin.Left},{c.Margin.Top},{c.Margin.Right},{c.Margin.Bottom})");
                // 列头/卡片都按 RowGeom.Cols(Width) 排版:顺带打出前三个子项的几何,
                // 用来验证“列头是否随窗口宽度移动”“三列是否收在按钮左侧”。
                if (c is FlowLayoutPanel nf)
                {
                    int j = 0;
                    foreach (Control n in nf.Controls)
                    {
                        if (j >= 3) break;
                        var cols = RowGeom.Cols(n.Width);
                        sb.Append($"\n[L]    >{j++} {n.GetType().Name} w={n.Width} " +
                                  $"cols=[{cols[0].X}..{cols[0].X + cols[0].W}][{cols[1].X}..{cols[1].X + cols[1].W}][{cols[2].X}..{cols[2].X + cols[2].W}] " +
                                  $"btnLeft={n.Width - RowGeom.ActionW - 12}");
                    }
                }
                if (i >= 40) break;
            }
            LayoutLog.Write(DataDir, sb.ToString());
        }
        catch { }
    }

    protected static Label MakeLabel(string text, float size, bool bold, Color? color, float height = 0)
    {
        var t = Theme.Current;
        // 自动行高:保证任何字号/缩放下不裁剪(裁剪是“丢字符/看不见”的主因)
        float autoH = (float)Math.Ceiling(size * 1.9f) + 8f;
        if (height <= 0) height = autoH;
        else height = Math.Max(height, autoH);
        return new Label
        {
            Text = text,
            Font = t.Font(size, bold),
            // Color.Empty 视为未指定 → 用主题文字色(深色=白,浅色=深),修复标题黑字
            ForeColor = color.HasValue && color.Value.ToArgb() != Color.Empty.ToArgb() ? color.Value : t.Text,
            Height = (int)Math.Ceiling(height),
            AutoSize = false,
            Margin = new Padding(0, 0, 0, 0),
        };
    }

    protected Label StatsSinceLine(string label)
    {
        long since = Db.StatsSinceMs();
        string s = since > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(since).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "—";
        return MakeLabel($"{label}: {s}", 9f, false, Theme.Current.TextDim, 22);
    }

    protected static Label Dim(string text, float height = 0) => MakeLabel(text, 8.5f, false, Theme.Current.TextDim, height);

    /// <summary>加入一个行控件,并保证行宽铺满。</summary>
    protected void Add(Control c)
    {
        c.Width = Flow.ClientSize.Width - 8;
        Flow.Controls.Add(c);
    }

    protected void Clear() => Flow.Controls.Clear();

    public virtual void OnShow() { }

    /// <summary>主题/字号变更后的重建入口。</summary>
    public virtual void RefreshTheme() { }
}
