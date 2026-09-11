namespace UsageTracker.Engine.UI;

/// <summary>表格式行几何(列位置统一;按 DPI/字号系数缩放,列宽随内容区自适应拉宽)。</summary>
internal static class RowGeom
{
    private static float S => Theme.Current.UiScale;
    public static int IconX => (int)(18 * S);
    public static int IconSize => (int)(40 * S);
    public static int NameX => (int)(70 * S);
    public static int ActionW => (int)(108 * S);
    public static int ColGap => (int)(10 * S);

    /// <summary>
    /// 三列时长列(数值居中),返回每列 [x,width]。
    /// 关键约束:**三列必须整体收在“名称列右侧 ~ 操作按钮左侧”之间**,绝不压到按钮上、也不越出行右缘。
    /// 旧实现用 `cw = Math.Max(240, …)` 硬下限,空间不够时不会收缩而是往右溢出 ——
    /// 实测(行宽 900)第三列落到 736~976,而按钮在 775~883,于是数值被按钮压住、总计列被裁掉。
    /// 现在改为“按可用宽度等分 + 逐列兜底夹紧”,空间不足时牺牲列宽(名称列本身有 EndEllipsis 自适应)。
    /// </summary>
    public static (int X, int W)[] Cols(int rowWidth)
    {
        int actionLeft = rowWidth - ActionW - (int)(14 * S);   // 与 AppTableRow.ActionRect 的右边距(12)+2 对齐,列绝不贴到按钮上
        int nameEnd = NameX + (int)(160 * S);          // 应用名称列 +40
        int gap = (int)(Math.Max(4, 10 * S));          // 列间距缩小约20
        int avail = actionLeft - nameEnd - (int)(6 * S) - gap * 2;
        int cw = Math.Max(60, avail / 3);              // 等分可用宽度(不再用 240 硬下限)
        var arr = new (int, int)[3];
        int x = nameEnd + 6;
        for (int i = 0; i < 3; i++)
        {
            int w = Math.Min(cw, Math.Max(48, actionLeft - x));   // 兜底:最后一列不越过按钮左侧
            arr[i] = (x, w);
            x += w + gap;
        }
        return arr;
    }
}

/// <summary>列头:名称 + 三列标题(参考图5:今日前台使用时长 / 今日后台使用时长 / 总计使用时长)。</summary>
internal sealed class ColumnHeader : Control
{
    public ColumnHeader()
    {
        Height = (int)(30 * Theme.Current.UiScale);
        BackColor = Theme.Current.Bg;
        ForeColor = Theme.Current.Text;
        Margin = new Padding(0, (int)(6 * Theme.Current.UiScale), 0, 0);
        // ResizeRedraw 必须有:列位置按 Width 计算,少了它拉窗口时本控件不重绘(保持旧像素),
        // 外观就是“卡片跟着窗口伸缩、列头停在原地” —— 即用户报的 §5.2。
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var cols = RowGeom.Cols(Width);
        using var f = t.Font(9.5f, true);
        string[] titles = { "今日前台使用时长", "今日后台使用时长", "总计使用时长" };
        for (int i = 0; i < 3; i++)
        {
            TextRenderer.DrawText(g, titles[i], f,
                new Rectangle(cols[i].X, 2, cols[i].W, Height - 4), t.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }
}

/// <summary>表格式应用行:深色卡片内 图标+名称 / 三列时长(大号,右对齐)/ 操作按钮。用于局部刷新数值。</summary>
internal sealed class AppTableRow : Control
{
    private Bitmap _icon;
    private readonly string _title;
    private readonly string _subtitle;
    private string _c1, _c2, _c3;
    private readonly string? _actionText;
    private readonly bool _actionDanger;
    private readonly Action? _action;
    private bool _hover;

    public AppTableRow(Bitmap icon, string title, string subtitle, string c1, string c2, string c3,
                       string? actionText = null, bool danger = false, Action? action = null)
    {
        _icon = icon; _title = title; _subtitle = subtitle;
        _c1 = c1; _c2 = c2; _c3 = c3;
        _actionText = actionText; _actionDanger = danger; _action = action;
        Height = Math.Max(84, (int)(96 * Math.Max(1f, Theme.Current.UiScale)));
        Margin = new Padding(0, (int)(4 * Math.Max(1f, Theme.Current.UiScale)), 0, (int)(4 * Math.Max(1f, Theme.Current.UiScale)));
        BackColor = Theme.Current.Bg;
        ForeColor = Theme.Current.Text;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = _action != null ? Cursors.Hand : Cursors.Default;
    }

    public string? Start { get; set; }   // “开始记录时间”独立行显示

    public void UpdateIcon(Bitmap icon) { _icon = icon; Invalidate(); }

    public void Update(string c1, string c2, string c3)
    {
        if (_c1 == c1 && _c2 == c2 && _c3 == c3) return;
        _c1 = c1; _c2 = c2; _c3 = c3;
        Invalidate();
    }

    private RectangleF ActionRect()
    {
        float w = RowGeom.ActionW, h = 30;
        return new RectangleF(Width - w - 12, (Height - h) / 2f, w, h);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && _action != null && ActionRect().Contains(e.Location)) _action();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var card = new RectangleF(2, 1, Width - 6, Height - 2);
        Color cardBg = t.Dark ? Color.FromArgb(58, 64, 86) : Color.FromArgb(244, 246, 250);
        if (_hover) cardBg = ControlPaint.Light(cardBg, t.Dark ? 0.16f : -0.05f);
        DrawUtil.FillRound(g, card, 12, cardBg);

        if (_icon != null) g.DrawImage(_icon, RowGeom.IconX, (Height - RowGeom.IconSize) / 2, RowGeom.IconSize, RowGeom.IconSize);

        int nameRight = RowGeom.Cols(Width)[0].X - 12;
        // 名称区:应用名(1行)+ 次数(1行)+ 开始记录时间(独立1行),整体垂直居中
        float groupTop = card.Y + Math.Max(2f, (card.Height - 82f) / 2f);
        using var f1 = t.Font(11.5f, true);
        TextRenderer.DrawText(g, _title, f1,
            new Rectangle(RowGeom.NameX, (int)groupTop, Math.Max(60, nameRight - RowGeom.NameX), 26), t.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        if (!string.IsNullOrEmpty(_subtitle))
        {
            using var f2 = t.Font(9f);
            TextRenderer.DrawText(g, _subtitle, f2,
                new Rectangle(RowGeom.NameX, (int)(groupTop + 28), Math.Max(80, nameRight - RowGeom.NameX), 20), t.TextDim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
        if (!string.IsNullOrEmpty(Start))
        {
            using var fs = t.Font(9f);
            TextRenderer.DrawText(g, Start, fs,
                new Rectangle(RowGeom.NameX, (int)(groupTop + 50), Math.Max(100, nameRight - RowGeom.NameX), 20), t.TextDim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        var cols = RowGeom.Cols(Width);
        string[] vals = { _c1, _c2, _c3 };
        using var fv = t.Font(12.5f, true);
        for (int i = 0; i < 3; i++)
        {
            TextRenderer.DrawText(g, vals[i], fv,
                new Rectangle(cols[i].X, (int)card.Y, cols[i].W, (int)card.Height), t.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }

        if (_actionText != null)
        {
            var ar = ActionRect();
            Color bg = _actionDanger ? (t.Dark ? Color.FromArgb(90, 229, 72, 77) : Color.FromArgb(50, 225, 60, 60)) : t.AccentSoft;
            if (_hover && ar.Contains(PointToClient(MousePosition))) bg = ControlPaint.Light(bg, 0.12f);
            DrawUtil.FillRound(g, ar, 13, bg);
            using var f3 = t.Font(9.5f);
            TextRenderer.DrawText(g, _actionText, f3,
                new Rectangle((int)ar.X, (int)ar.Y, (int)ar.Width, (int)ar.Height),
                _actionDanger ? (t.Dark ? Color.FromArgb(255, 150, 152) : t.Danger) : t.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }
}
