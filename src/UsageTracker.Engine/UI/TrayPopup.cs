using System.Drawing.Drawing2D;

namespace UsageTracker.Engine.UI;

/// <summary>
/// 托盘右键菜单(参考 1.png FluentFlyout):深灰 #303030、圆角 10、图标+文字、
/// 悬停浅灰胶囊、无分割线。显示于托盘图标上方(Shell_NotifyIconGetRect),不再固定右下角。
/// </summary>
internal sealed class TrayMenuPopup : Form
{
    private readonly (GlyphKind Glyph, string Label, Action Action)[] _items;
    private int _hover = -1;
    private const int ItemH = 38;
    private const int Pad = 7;
    private const int Corner = 11;

    public TrayMenuPopup((GlyphKind, string, Action)[] items)
    {
        _items = items;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        Width = 232;
        Height = Pad * 2 + _items.Length * ItemH;
        Font = new Font("Microsoft YaHei UI", 9.5f);
    }

    public void ShowAt(Point p)
    {
        // 确保不越出工作区
        var wa = Screen.GetWorkingArea(new Rectangle(p, Size));
        int x = Math.Clamp(p.X, wa.Left + 4, wa.Right - Width - 4);
        int y = Math.Clamp(p.Y - Height - 8, wa.Top + 4, wa.Bottom - Height - 4);
        Location = new Point(x, y);
        Show();
        Activate();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        UpdateRegion();
    }

    private void UpdateRegion()
    {
        using var path = DrawUtil.Rounded(new RectangleF(0, 0, Width, Height), Corner);
        Region = new Region(path);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = (e.Y - Pad) / ItemH;
        if (i >= _items.Length || i < 0) i = -1;
        if (i != _hover) { _hover = i; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        int i = (e.Y - Pad) / ItemH;
        if (i >= 0 && i < _items.Length)
        {
            var action = _items[i].Action;
            Close();
            action();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) Close();
        base.OnKeyDown(e);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // 底色
        DrawUtil.FillRound(g, new RectangleF(0, 0, Width - 1, Height - 1), Corner, Color.FromArgb(48, 48, 48));
        DrawUtil.DrawRound(g, new RectangleF(0.5f, 0.5f, Width - 2, Height - 2), Corner, Color.FromArgb(90, 90, 90), 1f);

        for (int i = 0; i < _items.Length; i++)
        {
            var row = new RectangleF(5, Pad + i * ItemH + 2, Width - 10, ItemH - 4);
            if (i == _hover)
                DrawUtil.FillRound(g, row, 8, Color.FromArgb(79, 79, 79));

            var iconR = new RectangleF(row.X + 12, row.Y + (row.Height - 16) / 2f, 16, 16);
            Color iconColor = i == _items.Length - 1 ? Color.FromArgb(240, 110, 110) : Color.White;
            Glyphs.Draw(g, _items[i].Glyph, iconR, iconColor);

            TextRenderer.DrawText(g, _items[i].Label,
                new Font("Microsoft YaHei UI", 9.5f),
                new Rectangle((int)(row.X + 40), (int)row.Y, (int)(row.Width - 48), (int)row.Height),
                Color.FromArgb(240, 240, 244),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
