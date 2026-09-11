using System.Drawing.Drawing2D;

namespace UsageTracker.Engine.UI;

/// <summary>左侧导航栏(参考 Clash Verge 深色侧栏):图标+文字,选中高亮胶囊+左强调条。</summary>
internal sealed class NavMenu : Control
{
    public record Item(string Key, string Label, GlyphKind Glyph);

    private readonly List<Item> _items = new();
    private string _selected = "";
    private string _hover = "";

    public NavMenu()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public void SetItems(IEnumerable<Item> items, string selectedKey)
    {
        _items.Clear();
        _items.AddRange(items);
        _selected = selectedKey;
        Invalidate();
    }

    public void Select(string key) { _selected = key; Invalidate(); }

    public Action<string>? Navigate;

    private Rectangle ItemRect(int i) => new(0, TopPad + i * ItemH, Width, ItemH);

    private int ItemH => (int)(42 * Math.Max(1f, Theme.Current.UiScale));
    private int TopPad => (int)(16 * Math.Max(1f, Theme.Current.UiScale));
    private int BottomPad => (int)(16 * Math.Max(1f, Theme.Current.UiScale));

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(t.Sidebar);

        int y = TopPad;
        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            bool selected = item.Key == _selected;
            bool hover = item.Key == _hover;
            var row = new RectangleF(8, y, Width - 16, ItemH - 8);
            if (selected || hover)
                DrawUtil.FillRound(g, row, 9, selected ? t.AccentSoft : (t.Dark ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(60, 0, 0, 0)));
            if (selected)
                DrawUtil.FillRound(g, new RectangleF(row.X + 4, y + (ItemH - 8) / 2f - 9, 3.5f, 18), 2, t.Accent); // 左侧强调条(参考图)

            var iconR = new RectangleF(16, y + (ItemH - 8) / 2f - 8, 16, 16);
            Glyphs.Draw(g, item.Glyph, iconR, selected ? t.Accent : (hover ? t.Text : t.TextDim));
            using var f = t.Font(10f, selected);
            TextRenderer.DrawText(g, item.Label, f,
                new Rectangle(42, y - 2, Width - 56, ItemH - 4),
                selected ? t.Text : t.TextDim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            y += ItemH;
        }

        // 底部:数据目录说明(弱化)
        using var f2 = t.Font(7.5f);
        TextRenderer.DrawText(g, "UsageTracker v3", f2, new Rectangle(10, Height - BottomPad - 12, Width - 20, 16),
            t.TextFaint, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        string h = Hit(e.Location);
        if (h != _hover) { _hover = h; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = ""; Invalidate();
        base.OnMouseLeave(e);
    }

    private string Hit(Point p)
    {
        if (p.X < 0 || p.X >= Width) return "";
        int i = (p.Y - TopPad) / ItemH;
        if (i >= 0 && i < _items.Count) return _items[i].Key;
        return "";
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left)
        {
            string k = Hit(e.Location);
            if (k.Length > 0) Navigate?.Invoke(k);
        }
    }
}
