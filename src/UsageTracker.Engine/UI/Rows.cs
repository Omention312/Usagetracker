using System.Drawing.Drawing2D;

namespace UsageTracker.Engine.UI;

internal static class TimeFmt
{
    public static string Hms(long ms)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
        if (t.TotalMinutes >= 1) return $"{t.Minutes}m {t.Seconds}s";
        return $"{t.Seconds}s";
    }
}

/// <summary>应用条目行:图标 + 名称/副标题 + 右侧时长 + 黑名单按钮(整行自绘)。</summary>
internal sealed class AppRow : Control
{
    private readonly Bitmap _icon;
    private readonly string _title;
    private readonly string _subtitle;
    private string _rightA;
    private string _rightB;
    private readonly string? _actionText;
    private readonly bool _actionDanger;
    private readonly Action? _action;
    private bool _hover;
    private const int H = 56;

    public AppRow(Bitmap icon, string title, string subtitle, string rightA, string rightB,
                  string? actionText = null, bool actionDanger = false, Action? action = null)
    {
        _icon = icon; _title = title; _subtitle = subtitle;
        _rightA = rightA; _rightB = rightB;
        _actionText = actionText; _actionDanger = actionDanger; _action = action;
        // 行高随界面缩放自适应,防内容被裁剪;背景跟随主题(深色下为深色底,文字用白色系,避免黑字/浅块)
        Height = Math.Max(60, (int)(64 * Math.Max(1f, Theme.Current.Scale)));
        Margin = new Padding(2, 4, 2, 4);
        BackColor = Theme.Current.Bg;
        ForeColor = Theme.Current.Text;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = _action != null ? Cursors.Hand : Cursors.Default;
    }

    /// <summary>局部刷新:仅更新右侧时长文本(不重建控件,避免整页闪烁)。</summary>
    public void UpdateDurations(string rightA, string rightB)
    {
        if (_rightA == rightA && _rightB == rightB) return;
        _rightA = rightA;
        _rightB = rightB;
        Invalidate();
    }

    private float ActionW => _actionText == null ? 0 : 96;
    private RectangleF ActionRect()
    {
        float w = ActionW, h = 26;
        return new RectangleF(Width - w - 14, (Height - h) / 2f, w, h);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && _action != null && ActionRect().Contains(e.Location))
            _action();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // 每行 = 深色卡片(参考图4:卡片底 + 左图标 + 右侧时长列)——加强对比,使卡片明显可辨
        var card = new RectangleF(6, 2, Width - 14, Height - 4);
        Color cardBg = t.Dark ? Color.FromArgb(58, 64, 86) : Color.FromArgb(244, 246, 250);
        if (_hover) cardBg = ControlPaint.Light(cardBg, t.Dark ? 0.16f : -0.05f);
        DrawUtil.FillRound(g, card, 12, cardBg);
        DrawUtil.DrawRound(g, card, 12, Theme.Alpha(t.Dark ? Color.White : Color.Black, t.Dark ? 26 : 18), 1f);

        int iy = (Height - 36) / 2;
        float iconX = card.X + 14;
        if (_icon != null) g.DrawImage(_icon, iconX, iy, 36, 36);

        float tx = iconX + 46;                 // 名称起点
        float actionW = ActionW;
        float durW = 200;
        float durX = card.Right - actionW - durW - 10;
        if (durX < tx + 70) durX = tx + 70;
        float textW = Math.Max(60, durX - tx - 8);

        using var f1 = t.Font(11f, true);
        TextRenderer.DrawText(g, _title, f1, new Rectangle((int)tx, (int)(card.Y + 6), (int)textW, 26), t.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        if (!string.IsNullOrEmpty(_subtitle))
        {
            using var f2 = t.Font(9f);
            TextRenderer.DrawText(g, _subtitle, f2, new Rectangle((int)tx, (int)(card.Y + 34), (int)textW, 22), t.TextDim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        // 时长列(右侧)
        if (!string.IsNullOrEmpty(_rightA))
        {
            using var fa = t.Font(12f, true);
            TextRenderer.DrawText(g, _rightA, fa, new Rectangle((int)durX, (int)(card.Y + 6), (int)(durW - 4), 28), t.Text,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (!string.IsNullOrEmpty(_rightB))
            {
                using var fb = t.Font(9f);
                TextRenderer.DrawText(g, _rightB, fb, new Rectangle((int)durX, (int)(card.Y + 36), (int)(durW - 4), 20), t.TextDim,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        if (_actionText != null)
        {
            var ar = ActionRect();
            Color bg = _actionDanger ? (t.Dark ? Color.FromArgb(80, 229, 72, 77) : Color.FromArgb(40, 225, 60, 60)) : t.AccentSoft;
            if (_hover && ar.Contains(PointToClient(MousePosition))) bg = ControlPaint.Light(bg, 0.12f);
            DrawUtil.FillRound(g, ar, 13, bg);
            using var f3 = t.Font(9f);
            TextRenderer.DrawText(g, _actionText, f3,
                new Rectangle((int)ar.X, (int)ar.Y, (int)ar.Width, (int)ar.Height),
                _actionDanger ? (t.Dark ? Color.FromArgb(255, 150, 152) : t.Danger) : t.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }
}

/// <summary>可折叠区块头(参考需求 5:默认收起)。点击切换 target.Visible,显示 ▸/▾。</summary>
internal sealed class ToggleSection : Control
{
    private readonly string _title;
    private bool _open;
    private Control? _target;

    public event Action? Toggled;

    public ToggleSection(string title)
    {
        _title = title;
        Height = (int)(36 * Math.Max(1f, Theme.Current.UiScale));
        Cursor = Cursors.Hand;
        BackColor = Theme.Current.Bg;
        ForeColor = Theme.Current.Text;
        // 该控件按 Width 画分隔线,同样需要 ResizeRedraw 才会跟着窗口重绘
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public void Bind(Control body) => _target = body;

    public bool Open { get => _open; set { _open = value; if (_target != null) _target.Visible = value; UpdateText(); Invalidate(); } }

    private Label? _countLabel;

    public void SetCountLabel(Label? l) => _countLabel = l;

    private void UpdateText()
    {
        if (_countLabel != null) _countLabel.Text = (_open ? "收起" : "展开") + $" ▾";
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        _open = !_open;
        if (_target != null) _target.Visible = _open;
        Invalidate();
        Toggled?.Invoke();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var tri = new RectangleF(8, (Height - 10) / 2f + 1, 10, 10);
        Glyphs.Draw(g, _open ? GlyphKind.ChevronDown : GlyphKind.ChevronRight, tri, t.TextDim);
        using var f = t.Font(10f, true);
        TextRenderer.DrawText(g, _title, f, new Rectangle(24, 0, Width - 40, Height), t.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        if (_open)
            using (var p = new Pen(Theme.Alpha(t.TextFaint, 90), 1)) g.DrawLine(p, 4, Height - 1, Width - 4, Height - 1);
    }
}
