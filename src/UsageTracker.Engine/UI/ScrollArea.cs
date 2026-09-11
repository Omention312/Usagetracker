using System.Drawing.Drawing2D;

namespace UsageTracker.Engine.UI;

/// <summary>
/// 自绘垂直滚动区域:内容 Flow 置于其中,隐藏系统细滚动条;
/// 滚动条:固定宽度 12px,常态灰(与页面文字一致),悬停/拖动时高亮为强调色,不使用 OS 悬停放大效果。
/// </summary>
internal sealed class ScrollArea : Panel
{
    private readonly FlowLayoutPanel _content;
    private int _offset;
    private bool _over;          // 鼠标在滚动条上
    private bool _drag;
    private int _dragStartMouseY, _dragStartOffset;
    private int SbW => (int)(24 * Math.Max(1f, Theme.Current.UiScale)); // 滚动条宽度(随 DPI/字号缩放)

    public ScrollArea(FlowLayoutPanel content)
    {
        _content = content;
        _content.AutoSize = true;
        _content.AutoScroll = false;
        _content.Margin = new Padding(0);
        content.Location = new Point(0, 0);
        Controls.Add(_content);
        AutoScroll = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Resize += (_, _) => { LayoutContent(); };
    }

    /// <summary>内容高度(Flow 自动尺寸)。每次 Build 后调用。</summary>
    public void ContentChanged()
    {
        _content.PerformLayout();
        LayoutContent();
    }

    private void PerformLayoutDeep(Control c)
    {
        c.PerformLayout();
        foreach (Control child in c.Controls)
        {
            if (child is FlowLayoutPanel || child is Panel) PerformLayoutDeep(child);
            else if (child.Controls.Count > 0) PerformLayoutDeep(child);
        }
    }

    /// <summary>诊断用:把量测值拼成一行(滚动条是否绘制取决于 ContentHeight/MaxOffset 的真实取值)。</summary>
    public string GeometryInfo()
        => $"\n[L] scroll: content={_content.Width}x{_content.Height} contentHeight={ContentHeight} client={ClientSize.Width}x{ClientSize.Height} " +
           $"maxOffset={MaxOffset} offset={_offset} sbW={SbW} thumbH={ThumbRect.Height} drawBar={(ContentHeight > ClientSize.Height)}";

    private int ContentHeight => Math.Max(_content.Height, ClientSize.Height);
    private int MaxOffset => Math.Max(0, _content.Height - ClientSize.Height + 8);

    private void LayoutContent()
    {
        // 必须为自绘滚动条预留右侧一条 SbW 宽的带子:
        // WinForms 的子控件永远画在父控件之上,若内容铺满整宽(旧实现是 ClientSize.Width-4),
        // 父控件 OnPaint 画的 thumb 会被内容 Flow 盖掉 —— 表现为“量测全对(drawBar=True)但滚动条永远看不见”,
        // 同时 OnMouseDown 的 thumb 命中区也被子控件吃掉(拖动失效)。留出带子后 thumb 区无人覆盖。
        int cw = Math.Max(80, ClientSize.Width - SbW - 4);
        _content.Width = cw;
        foreach (Control c in _content.Controls) SetChildWidth(c, cw);
        PerformLayoutDeep(_content);
        PerformLayoutDeep(_content); // 二次布局,消除“缓慢缩短/滞后”
        int max = MaxOffset;
        if (_offset > max) _offset = max;
        if (_offset < 0) _offset = 0;
        _content.Location = new Point(0, -_offset);
        Invalidate();
    }

    private void SetChildWidth(Control c, int w)
    {
        if (c is FlowLayoutPanel nested)
        {
            // 顺序很重要:先按目标宽度铺满子项,再设容器自身宽度。
            // 反过来(旧实现:先设宽 → PerformLayout → 再用 ClientSize 去设子项)会因 AutoSize 把容器顶回“首选宽度”:
            // 实测内层列表被钉死在 900(来自 ColumnHeader 的初始 Width=900),于是卡片只有 900 宽、右侧空出约 300px,
            // 列几何再按这个窄行算就把数值挤到按钮上。
            int iw = Math.Max(40, w - nested.Padding.Horizontal);
            foreach (Control n in nested.Controls) SetChildWidth(n, iw);
            c.Width = w;
            nested.PerformLayout();
        }
        else
        {
            c.Width = Math.Max(20, w);
        }
    }

    // ---------- 滚轮(事件自子控件冒泡至此) ----------
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        int delta = e.Delta > 0 ? -48 : 48;
        _offset = Math.Clamp(_offset + delta, 0, MaxOffset);
        _content.Location = new Point(0, -_offset);
        Invalidate();
        base.OnMouseWheel(e);
    }

    protected override bool IsInputKey(Keys keyData) => base.IsInputKey(keyData);

    // ---------- 滚动条几何 ----------
    private Rectangle TrackRect => new(ClientSize.Width - SbW, 0, SbW, ClientSize.Height);
    private Rectangle ThumbRect
    {
        get
        {
            int track = ClientSize.Height;
            double ratio = track / (double)ContentHeight;
            int th = Math.Max(28, (int)(track * ratio));
            int range = Math.Max(1, track - th);
            int y = _offset * range / Math.Max(1, MaxOffset);
            return new Rectangle(ClientSize.Width - SbW, y, SbW, th);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        bool overThumb = ThumbRect.Contains(e.Location);
        if (_over != overThumb || (_drag && overThumb))
        {
            _over = overThumb || _drag;
            Cursor = overThumb || _drag ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
        if (_drag)
        {
            int dy = e.Y - _dragStartMouseY;
            double ratio = (ClientSize.Height) / (double)Math.Max(1, ContentHeight);
            _offset = Math.Clamp(_dragStartOffset + (int)(dy / Math.Max(0.05, ratio)), 0, MaxOffset);
            _content.Location = new Point(0, -_offset);
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && ThumbRect.Contains(e.Location))
        {
            _drag = true;
            _dragStartMouseY = e.Y;
            _dragStartOffset = _offset;
            Invalidate();
        }
        else if (e.Button == MouseButtons.Left && TrackRect.Contains(e.Location))
        {
            int mid = ThumbRect.Y + ThumbRect.Height / 2;
            _offset = Math.Clamp(_offset + (e.Y < mid ? -ClientSize.Height / 2 : ClientSize.Height / 2), 0, MaxOffset);
            _content.Location = new Point(0, -_offset);
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _drag = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (!_drag) _over = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        g.Clear(t.Bg);
        if (ContentHeight <= ClientSize.Height) return; // 内容不足,不显示滚动条
        var thumb = ThumbRect;
        // 仅在原灰色上提亮(不变蓝/不变主题色)
        Color baseC = t.Dark ? Color.FromArgb(110, 114, 128) : Color.FromArgb(150, 154, 168);
        Color c = _drag ? ControlPaint.Light(baseC, 0.45f) : _over ? ControlPaint.Light(baseC, 0.22f) : baseC;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(c)) g.FillRectangle(b, thumb.X + 4, thumb.Y, SbW - 8, thumb.Height);
    }
}
