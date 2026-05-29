using System.Drawing;
using System.Drawing.Drawing2D;

namespace Op7PortScanner;

/// <summary>
/// Generates a black-and-white Aster flower illustration in the style of
/// the flower character from Undertale (Flowey), rendered purely with GDI+.
/// </summary>
public static class FlowerArt
{
    public static Bitmap Generate(int size = 200)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.White);

        float cx = size / 2f;
        float cy = size / 2f - 8f;

        // === PETALS (5, Flowey style) ===
        int petalCount = 5;
        float petalLen = size * 0.32f;
        float startAngle = -(float)(Math.PI / 2); // top

        for (int i = 0; i < petalCount; i++)
        {
            float angle = startAngle + i * 2f * (float)Math.PI / petalCount;
            DrawPetal(g, cx, cy, angle, petalLen, size * 0.07f);
        }

        // === STEM ===
        float stemTop = cy + size * 0.095f;
        float stemBot = size - size * 0.04f;
        using (var stemPen = new Pen(Color.Black, size * 0.016f) { LineJoin = LineJoin.Round })
        {
            // Slight curve for organic feel
            using var stemPath = new GraphicsPath();
            stemPath.AddBezier(
                new PointF(cx, stemTop),
                new PointF(cx + size * 0.03f, stemTop + (stemBot - stemTop) * 0.4f),
                new PointF(cx - size * 0.02f, stemTop + (stemBot - stemTop) * 0.7f),
                new PointF(cx, stemBot));
            g.DrawPath(stemPen, stemPath);
        }

        // === LEAF ===
        float leafY = stemTop + (stemBot - stemTop) * 0.42f;
        DrawLeaf(g, cx, leafY, size);

        // === CENTER CIRCLE ===
        float cr = size * 0.11f;
        // Shadow ring
        g.FillEllipse(Brushes.DimGray, cx - cr - 1.5f, cy - cr - 1.5f, (cr + 1.5f) * 2, (cr + 1.5f) * 2);
        g.FillEllipse(Brushes.Black, cx - cr, cy - cr, cr * 2, cr * 2);

        // Texture dots on center
        float dotR = cr * 0.18f;
        for (int i = 0; i < 6; i++)
        {
            float a = i * (float)Math.PI / 3f;
            float dx = (float)Math.Cos(a) * (cr * 0.52f);
            float dy = (float)Math.Sin(a) * (cr * 0.52f);
            g.FillEllipse(Brushes.DimGray, cx + dx - dotR / 2, cy + dy - dotR / 2, dotR, dotR);
        }

        // === FACE ===
        DrawFace(g, cx, cy, cr);

        return bmp;
    }

    private static void DrawPetal(Graphics g, float cx, float cy, float angle, float len, float spread)
    {
        float dx = (float)Math.Cos(angle);
        float dy = (float)Math.Sin(angle);
        float px = (float)Math.Cos(angle + Math.PI / 2);
        float py = (float)Math.Sin(angle + Math.PI / 2);

        float root = len * 0.08f;
        float mid = len * 0.55f;

        // Left bezier curve
        PointF p0 = new(cx + px * root, cy + py * root);
        PointF cp1L = new(cx + px * spread * 1.1f + dx * mid * 0.25f, cy + py * spread * 1.1f + dy * mid * 0.25f);
        PointF cp2L = new(cx + px * spread * 0.7f + dx * mid, cy + py * spread * 0.7f + dy * mid);
        PointF tip = new(cx + dx * len, cy + dy * len);

        // Right bezier curve
        PointF cp2R = new(cx - px * spread * 0.7f + dx * mid, cy - py * spread * 0.7f + dy * mid);
        PointF cp1R = new(cx - px * spread * 1.1f + dx * mid * 0.25f, cy - py * spread * 1.1f + dy * mid * 0.25f);
        PointF p1 = new(cx - px * root, cy - py * root);

        using var path = new GraphicsPath();
        path.AddBezier(p0, cp1L, cp2L, tip);
        path.AddBezier(tip, cp2R, cp1R, p1);
        path.CloseFigure();

        // Fill: white center gradient fading to light-gray
        using var brush = new PathGradientBrush(path)
        {
            CenterColor = Color.White,
            SurroundColors = new[] { Color.FromArgb(190, 190, 190) },
            CenterPoint = new PointF(cx + dx * len * 0.35f, cy + dy * len * 0.35f)
        };
        g.FillPath(brush, path);

        // Outline + vein line
        using var pen = new Pen(Color.Black, 1.4f) { LineJoin = LineJoin.Round };
        g.DrawPath(pen, path);

        // Vein
        using var veinPen = new Pen(Color.FromArgb(90, 90, 90), 0.7f);
        g.DrawLine(veinPen,
            new PointF(cx + dx * root * 2, cy + dy * root * 2),
            new PointF(cx + dx * len * 0.8f, cy + dy * len * 0.8f));
    }

    private static void DrawLeaf(Graphics g, float cx, float leafY, float size)
    {
        float lw = size * 0.18f;
        float lh = size * 0.14f;
        float offsetX = size * 0.18f;

        PointF[] pts = {
            new(cx, leafY),
            new(cx + offsetX, leafY - lh * 0.7f),
            new(cx + lw, leafY - lh * 0.1f),
            new(cx + offsetX * 0.5f, leafY + lh * 0.5f)
        };

        using var leafPath = new GraphicsPath();
        leafPath.AddClosedCurve(pts, 0.7f);
        g.FillPath(Brushes.Black, leafPath);
        g.DrawPath(new Pen(Color.Black, 1f), leafPath);

        // Vein
        using var vp = new Pen(Color.FromArgb(180, 180, 180), 0.8f);
        g.DrawLine(vp, new PointF(cx, leafY), new PointF(cx + lw * 0.75f, leafY - lh * 0.1f));
    }

    private static void DrawFace(Graphics g, float cx, float cy, float cr)
    {
        float ew = cr * 0.38f;   // eye width
        float eh = cr * 0.40f;   // eye height
        float eyeY = cy - cr * 0.30f;
        float pupR = cr * 0.16f;
        float eyeOff = cr * 0.45f;

        // === LEFT EYE ===
        g.FillEllipse(Brushes.White, cx - eyeOff - ew / 2, eyeY - eh / 2, ew, eh);
        g.FillEllipse(Brushes.Black, cx - eyeOff - pupR / 2 + 1, eyeY - pupR / 2, pupR, pupR);

        // === RIGHT EYE ===
        g.FillEllipse(Brushes.White, cx + eyeOff - ew / 2, eyeY - eh / 2, ew, eh);
        g.FillEllipse(Brushes.Black, cx + eyeOff - pupR / 2 - 1, eyeY - pupR / 2, pupR, pupR);

        // === SMILE (Flowey's slightly menacing grin) ===
        using var smilePen = new Pen(Color.White, cr * 0.10f) { LineJoin = LineJoin.Round, EndCap = LineCap.Round, StartCap = LineCap.Round };
        float smileW = cr * 1.0f;
        float smileH = cr * 0.50f;
        float smileY = cy + cr * 0.08f;
        g.DrawArc(smilePen, cx - smileW / 2, smileY, smileW, smileH, 5, 170);

        // Corner dimples
        g.FillEllipse(Brushes.White, cx - smileW / 2 - cr * 0.04f, smileY + smileH * 0.25f, cr * 0.12f, cr * 0.12f);
        g.FillEllipse(Brushes.White, cx + smileW / 2 - cr * 0.08f, smileY + smileH * 0.25f, cr * 0.12f, cr * 0.12f);
    }
}
