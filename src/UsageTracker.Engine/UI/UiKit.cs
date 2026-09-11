using System.Drawing.Drawing2D;

namespace UsageTracker.Engine.UI;

/// <summary>绘制工具 + 矢量小图标(无字体依赖,稳定跨系统)。</summary>
internal enum GlyphKind { Clock, Grid, Ban, Sliders, Power, ChevronDown, ChevronRight, Close }

internal static class DrawUtil
{
    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        if (d > r.Width) d = r.Width;
        if (d > r.Height) d = r.Height;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void FillRound(Graphics g, RectangleF r, float rad, Color c)
    {
        using var path = Rounded(r, rad);
        using var b = new SolidBrush(c);
        g.FillPath(b, path);
    }

    public static void DrawRound(Graphics g, RectangleF r, float rad, Color c, float width = 1f)
    {
        using var path = Rounded(r, rad);
        using var p = new Pen(c, width);
        g.DrawPath(p, path);
    }
}

internal static class Glyphs
{
    public static void Draw(Graphics g, GlyphKind kind, RectangleF r, Color c)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
        float w = r.Width, h = r.Height;
        using var pen = new Pen(c, Math.Max(1.2f, w * 0.09f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var brush = new SolidBrush(c);
        switch (kind)
        {
            case GlyphKind.Clock:
            {
                float rad = Math.Min(w, h) * 0.36f;
                g.DrawEllipse(pen, cx - rad, cy - rad, rad * 2, rad * 2);
                g.DrawLine(pen, cx, cy, cx, cy - rad * 0.62f);
                g.DrawLine(pen, cx, cy, cx + rad * 0.5f, cy + rad * 0.16f);
                break;
            }
            case GlyphKind.Grid:
            {
                float s = Math.Min(w, h) * 0.42f, x0 = cx - s, y0 = cy - s;
                float cell = s * 0.9f;
                g.FillRectangle(brush, x0, y0, cell, cell);
                g.FillRectangle(brush, x0 + s * 0.9f, y0, cell, cell);
                g.FillRectangle(brush, x0, y0 + s * 0.9f, cell, cell);
                g.FillRectangle(brush, x0 + s * 0.9f, y0 + s * 0.9f, cell, cell);
                break;
            }
            case GlyphKind.Ban:
            {
                float rad = Math.Min(w, h) * 0.38f;
                g.DrawEllipse(pen, cx - rad, cy - rad, rad * 2, rad * 2);
                g.DrawLine(pen, cx - rad * 0.7f, cy - rad * 0.7f, cx + rad * 0.7f, cy + rad * 0.7f);
                break;
            }
            case GlyphKind.Sliders:
            {
                float x0 = r.X + w * 0.12f, x1 = r.Right - w * 0.12f;
                float[] ys = { cy - h * 0.24f, cy, cy + h * 0.24f };
                foreach (float y in ys)
                {
                    g.DrawLine(pen, x0, y, x1, y);
                    float kx = cx + (y == cy ? w * 0.14f : (y < cy ? -w * 0.12f : w * 0.10f));
                    float kr = Math.Max(1.8f, w * 0.10f);
                    g.FillEllipse(brush, kx - kr, y - kr, kr * 2, kr * 2);
                }
                break;
            }
            case GlyphKind.Power:
            {
                float rad = Math.Min(w, h) * 0.36f;
                g.DrawArc(pen, cx - rad, cy - rad * 0.9f, rad * 2, rad * 2, 130, 280);
                g.DrawLine(pen, cx, cy - rad * 0.45f, cx, cy - rad * 1.05f);
                break;
            }
            case GlyphKind.ChevronDown:
                g.DrawLines(pen, new[] { new PointF(cx - w * 0.2f, cy - h * 0.08f), new PointF(cx, cy + h * 0.14f), new PointF(cx + w * 0.2f, cy - h * 0.08f) });
                break;
            case GlyphKind.ChevronRight:
                g.DrawLines(pen, new[] { new PointF(cx - w * 0.12f, cy - h * 0.2f), new PointF(cx + w * 0.12f, cy), new PointF(cx - w * 0.12f, cy + h * 0.2f) });
                break;
            case GlyphKind.Close:
                g.DrawLine(pen, r.Left + w * 0.25f, r.Top + h * 0.25f, r.Right - w * 0.25f, r.Bottom - h * 0.25f);
                g.DrawLine(pen, r.Right - w * 0.25f, r.Top + h * 0.25f, r.Left + w * 0.25f, r.Bottom - h * 0.25f);
                break;
        }
    }
}
