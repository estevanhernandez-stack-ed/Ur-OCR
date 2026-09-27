using System.Drawing;
using System.Drawing.Imaging;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

public sealed class ColorMatcher : IColorMatchEngine
{
    public ColorMatchResult Evaluate(Bitmap bmp, ColorCriteria c) => EvaluateCore(bmp, c, null);

    public ColorMatchResult Evaluate(Bitmap bmp, ColorCriteria c, RegionRect recordedRegion)
        => EvaluateCore(bmp, c, recordedRegion);

    private static ColorMatchResult EvaluateCore(Bitmap bmp, ColorCriteria c, RegionRect? recordedRegion)
    {
        Rgb sampled;
        if (c.Box is not null && c.Point is not null)
        {
            sampled = AverageBox(bmp, ScalePoint(c.Point, bmp, recordedRegion), c.Box);
        }
        else
        {
            var (r, g, b) = c.SamplingMode switch
            {
                ColorSamplingMode.SinglePixel => SamplePixel(bmp, bmp.Width / 2, bmp.Height / 2),
                ColorSamplingMode.RegionAverage => AverageRect(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height)),
                _ => throw new ArgumentOutOfRangeException()
            };
            sampled = new Rgb(r, g, b);
        }

        var distance = Distance(sampled, c.TargetRgb);
        double? toOther = c.Other is null ? null : Distance(sampled, c.Other);
        var matched = distance <= c.ToleranceRgb && (toOther is null || distance < toOther.Value);
        return new ColorMatchResult(sampled, distance, matched, toOther);
    }

    public bool Matches(Bitmap bmp, ColorCriteria c) => Evaluate(bmp, c).Matched;

    /// <summary>
    /// Average of <paramref name="box"/> around <paramref name="point"/>, clamped
    /// to the bitmap. The picker and the check both call this, so a trigger
    /// checks exactly the pixels it was picked from.
    /// </summary>
    public static Rgb AverageBox(Bitmap bmp, PickPoint point, SampleBox box)
    {
        var x0 = Math.Clamp(point.X + box.OffsetX, 0, bmp.Width - 1);
        var y0 = Math.Clamp(point.Y + box.OffsetY, 0, bmp.Height - 1);
        var x1 = Math.Clamp(point.X + box.OffsetX + box.W, x0 + 1, bmp.Width);
        var y1 = Math.Clamp(point.Y + box.OffsetY + box.H, y0 + 1, bmp.Height);
        var (r, g, b) = AverageRect(bmp, Rectangle.FromLTRB(x0, y0, x1, y1));
        return new Rgb(r, g, b);
    }

    private static double Distance(Rgb a, Rgb b)
    {
        var dr = a.R - b.R;
        var dg = a.G - b.G;
        var db = a.B - b.B;
        return Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    // Client-anchored captures come back scaled to the live client size; the
    // pick point was recorded against the stored region, so scale it too.
    private static PickPoint ScalePoint(PickPoint p, Bitmap bmp, RegionRect? recorded)
    {
        if (recorded is null || recorded.Width < 1 || recorded.Height < 1) return p;
        if (recorded.Width == bmp.Width && recorded.Height == bmp.Height) return p;
        return new PickPoint(
            (int)Math.Round(p.X * (double)bmp.Width / recorded.Width),
            (int)Math.Round(p.Y * (double)bmp.Height / recorded.Height));
    }

    private static (int r, int g, int b) SamplePixel(Bitmap bmp, int x, int y)
    {
        var px = bmp.GetPixel(x, y);
        return (px.R, px.G, px.B);
    }

    private static (int r, int g, int b) AverageRect(Bitmap bmp, Rectangle rect)
    {
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            long sumR = 0, sumG = 0, sumB = 0;
            int stride = data.Stride;
            int bytes = stride * rect.Height;
            var buffer = new byte[bytes];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buffer, 0, bytes);
            for (int y = 0; y < rect.Height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < rect.Width; x++)
                {
                    int i = row + x * 4;
                    sumB += buffer[i];
                    sumG += buffer[i + 1];
                    sumR += buffer[i + 2];
                }
            }
            long pixels = rect.Width * (long)rect.Height;
            return ((int)(sumR / pixels), (int)(sumG / pixels), (int)(sumB / pixels));
        }
        finally { bmp.UnlockBits(data); }
    }
}
