using System.Drawing;
using System.Drawing.Imaging;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

public sealed class ColorMatcher : IColorMatchEngine
{
    public ColorMatchResult Evaluate(Bitmap bmp, ColorCriteria c) => Judge(Sample(bmp, c, null), c);

    public ColorMatchResult Evaluate(Bitmap bmp, ColorCriteria c, RegionRect recordedRegion)
        => Judge(Sample(bmp, c, recordedRegion), c);

    public bool Matches(Bitmap bmp, ColorCriteria c) => Evaluate(bmp, c).Matched;

    /// <summary>
    /// The colour a check sees: the box around the pick point when the criteria
    /// have one, else legacy SinglePixel (region centre) or RegionAverage (whole
    /// region). recordedRegion scales the pick point for client-anchored captures.
    /// </summary>
    public static Rgb Sample(Bitmap bmp, ColorCriteria c, RegionRect? recordedRegion)
    {
        if (c.Box is not null && c.Point is not null)
            return AverageBox(bmp, ScalePoint(c.Point, bmp, recordedRegion), c.Box);

        var (r, g, b) = c.SamplingMode switch
        {
            ColorSamplingMode.SinglePixel => SamplePixel(bmp, bmp.Width / 2, bmp.Height / 2),
            ColorSamplingMode.RegionAverage => AverageRect(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height)),
            _ => throw new ArgumentOutOfRangeException()
        };
        return new Rgb(r, g, b);
    }

    /// <summary>
    /// Judges a sampled colour. Target check: within tolerance of the target (and
    /// closer to it than to Other, when set). None-of check (NoneOf set): more than
    /// tolerance from every listed colour and every colour in
    /// <paramref name="extraNoneOf"/>; Distance is to the nearest of them and
    /// Nearest names it. An empty combined list never matches: with nothing to
    /// compare against, "none of them" means nothing. extraNoneOf is ignored by a
    /// target check.
    /// </summary>
    public static ColorMatchResult Judge(Rgb sampled, ColorCriteria c, IReadOnlyList<Rgb>? extraNoneOf = null)
    {
        if (c.NoneOf is not null)
        {
            var nearest = double.PositiveInfinity;
            Rgb? nearestColour = null;
            foreach (var listed in c.NoneOf.Concat(extraNoneOf ?? Array.Empty<Rgb>()))
            {
                var d = Distance(sampled, listed);
                if (d < nearest) { nearest = d; nearestColour = listed; }
            }
            var noneNear = nearestColour is not null && nearest > c.ToleranceRgb;
            return new ColorMatchResult(sampled, nearest, noneNear, Nearest: nearestColour);
        }

        var distance = Distance(sampled, c.TargetRgb);
        double? toOther = c.Other is null ? null : Distance(sampled, c.Other);
        var matched = distance <= c.ToleranceRgb && (toOther is null || distance < toOther.Value);
        return new ColorMatchResult(sampled, distance, matched, toOther);
    }

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

    /// <summary>Euclidean RGB distance, 0 to about 441.7.</summary>
    public static double Distance(Rgb a, Rgb b)
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
