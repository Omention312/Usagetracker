using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace IconGen;

/// <summary>生成 UsageTracker.ico:蓝色渐变圆角方块 + 白色时钟指针(多尺寸 PNG 组装 ICO)。</summary>
internal static class Program
{
    private static readonly int[] Sizes = { 16, 24, 32, 48, 64, 128, 256 };

    [STAThread]
    private static void Main(string[] args)
    {
        string outPath = args.Length > 0 ? args[0] : @"assets\UsageTracker.ico";
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        var pngs = new Dictionary<int, byte[]>();
        foreach (int size in Sizes) pngs[size] = Render(size);
        WriteIco(outPath, pngs);
        Console.WriteLine("ICO written: " + outPath + " (" + new FileInfo(outPath).Length + " bytes)");
    }

    private static byte[] Render(int px)
    {
        using var bmp = new Bitmap(px, px);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            float s = px;
            float r = s * 0.20f;                 // 圆角
            var bgRect = new RectangleF(0, 0, s, s);
            using var path = Rounded(bgRect, r);
            using var grad = new LinearGradientBrush(bgRect, Color.FromArgb(255, 46, 150, 255), Color.FromArgb(255, 6, 96, 209), 60f);
            g.FillPath(grad, path);

            // 时针/分针
            float cx = s * 0.5f, cy = s * 0.5f;
            float outer = s * 0.30f;             // 表盘半径
            float inner = s * 0.21f;             // 内圆(空心环效果:画粗白圈内再抠?) 简化:细圆环 + 指针
            using var white = new Pen(Color.White, Math.Max(1.6f, s * 0.065f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawEllipse(white, cx - outer, cy - outer, outer * 2, outer * 2);

            // 分针朝 12,时针朝 2~3 点方向
            DrawHand(g, white, cx, cy, s * 0.20f, -90f);     // 分针
            DrawHand(g, white, cx, cy, s * 0.125f, -30f);    // 时针
            using var dot = new SolidBrush(Color.White);
            float d = s * 0.045f;
            g.FillEllipse(dot, cx - d, cy - d, d * 2, d * 2);
        }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static void DrawHand(Graphics g, Pen pen, float cx, float cy, float len, float deg)
    {
        double rad = deg * Math.PI / 180.0;
        float ex = cx + (float)(Math.Cos(rad) * len);
        float ey = cy + (float)(Math.Sin(rad) * len);
        g.DrawLine(pen, cx, cy, ex, ey);
    }

    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static void WriteIco(string path, Dictionary<int, byte[]> pngs)
    {
        using var fs = new FileStream(path, FileMode.Create);
        using var bw = new BinaryWriter(fs);
        int count = pngs.Count;
        bw.Write((short)0);   // reserved
        bw.Write((short)1);   // type: icon
        bw.Write((short)count);
        int offset = 6 + 16 * count;
        foreach (var kv in pngs.OrderBy(k => k.Key))
        {
            byte sizeByte = (byte)(kv.Key >= 256 ? 0 : kv.Key);
            bw.Write(sizeByte);              // width
            bw.Write(sizeByte);              // height
            bw.Write((byte)0);               // palette
            bw.Write((byte)0);               // reserved
            bw.Write((short)1);              // planes
            bw.Write((short)32);             // bpp
            bw.Write(kv.Value.Length);
            bw.Write(offset);
            offset += kv.Value.Length;
        }
        foreach (var kv in pngs.OrderBy(k => k.Key)) bw.Write(kv.Value);
    }
}
