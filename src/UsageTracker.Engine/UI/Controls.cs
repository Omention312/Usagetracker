using System.Drawing.Drawing2D;

namespace UsageTracker.Engine.UI;

/// <summary>自定义圆角按钮(主/幽灵/危险/静默),无系统边框观感。</summary>
internal sealed class ActionButton : Control
{
    public enum Kind { Accent, Ghost, Danger, Quiet }

    private Kind _kind = Kind.Accent;
    private bool _hover;
    private bool _down;

    public Kind Style { get => _kind; set { _kind = value; Invalidate(); } }
    public event EventHandler? Clicked;

    public ActionButton(string text, Kind kind = Kind.Accent)
    {
        Text = text;
        _kind = kind;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Height = 28;
        TabStop = true;
        BackColor = Theme.Current.Bg;
        ForeColor = Theme.Current.Text;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _down = false; Invalidate(); } base.OnMouseUp(e); }
    protected override void OnClick(EventArgs e) { base.OnClick(e); Clicked?.Invoke(this, e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            Clicked?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0, 0, Width - 1, Height - 1);
        Color bg, fg;
        switch (_kind)
        {
            case Kind.Accent: bg = t.Accent; fg = Color.White; break;
            case Kind.Ghost: bg = t.Surface; fg = t.Text; break;
            case Kind.Danger: bg = _hover ? Color.FromArgb(235, 80, 84) : t.Danger; fg = Color.White; break;
            default: bg = Color.Transparent; fg = t.TextDim; break;
        }
        if (_hover && _kind != Kind.Quiet)
            bg = ControlPaint.Light(bg, _kind == Kind.Accent || _kind == Kind.Danger ? 0.15f : 0.10f);
        if (!bg.Equals(Color.Transparent))
            DrawUtil.FillRound(g, r, 8, bg);
        if (_down)
            DrawUtil.FillRound(g, r, 8, Color.FromArgb(40, Color.Black));

        TextRenderer.DrawText(g, Text, t.Font(9.5f, _kind == Kind.Quiet), ClientRectangle with { X = 0 }, fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

/// <summary>圆形轨道开关(参考系统开关观感)。</summary>
internal sealed class SwitchControl : Control
{
    private bool _value;
    public bool Value { get => _value; set { if (_value != value) { _value = value; Invalidate(); } } }
    public event EventHandler? Changed;

    public SwitchControl()
    {
        float us = Math.Max(1f, Theme.Current.UiScale);
        Size = new Size((int)(46 * us), (int)(26 * us));
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Cursor = Cursors.Hand;
        TabStop = true;
        BackColor = Theme.Current.Bg;
        ForeColor = Theme.Current.Text;
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        _value = !_value;
        Invalidate();
        Changed?.Invoke(this, e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            _value = !_value;
            Invalidate();
            Changed?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Color track = _value ? t.Accent : (t.Dark ? Color.FromArgb(90, 92, 106) : Color.FromArgb(200, 202, 210));
        DrawUtil.FillRound(g, r, Height / 2f, track);
        float pad = 2.5f;
        float d = Height - pad * 2;
        float kx = _value ? Width - pad - d : pad;
        using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, kx, pad, d, d);
    }
}

/// <summary>分段选择(参考“浅色/深色/系统”控件)。</summary>
internal sealed class SegmentedControl : Control
{
    private int _selected;
    public string[] Items { get; }
    public int SelectedIndex { get => _selected; set { _selected = Math.Clamp(value, 0, Items.Length - 1); Invalidate(); } }
    public event EventHandler? SelectionChanged;

    public SegmentedControl(params string[] items)
    {
        Items = items;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Cursor = Cursors.Hand;
        TabStop = true;
        BackColor = Theme.Current.Bg;
        ForeColor = Theme.Current.Text;
        Height = (int)(36 * Math.Max(1f, Theme.Current.UiScale));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || Width <= 0) return;
        int idx = (int)(e.X / (Width / (float)Items.Length));
        SelectByIndex(idx);
    }

    private void SelectByIndex(int idx)
    {
        idx = Math.Clamp(idx, 0, Items.Length - 1);
        if (idx != _selected) { _selected = idx; Invalidate(); SelectionChanged?.Invoke(this, EventArgs.Empty); }
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Left) { SelectByIndex(_selected - 1); e.Handled = true; }
        else if (e.KeyCode == Keys.Right) { SelectByIndex(_selected + 1); e.Handled = true; }
        else if (e.KeyCode is Keys.Up or Keys.Down) { e.Handled = true; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var outer = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        DrawUtil.FillRound(g, outer, Height / 2f, t.Dark ? Color.FromArgb(52, 54, 68) : Color.FromArgb(226, 228, 235));
        float segW = Width / (float)Items.Length;
        for (int i = 0; i < Items.Length; i++)
        {
            var seg = new RectangleF(segW * i + 2, 2, segW - 4, Height - 4);
            if (i == _selected)
                DrawUtil.FillRound(g, seg, (Height - 4) / 2f, t.Accent);
            using var f = t.Font(9f, i == _selected);
            TextRenderer.DrawText(g, Items[i], f,
                new Rectangle((int)seg.X, (int)seg.Y, (int)seg.Width, (int)seg.Height),
                i == _selected ? Color.White : t.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
