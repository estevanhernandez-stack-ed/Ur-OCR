using System.Drawing;
using System.Drawing.Imaging;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

public sealed class ColorMatcher : IColorMatchEngine
{
    public ColorMatchResult Evaluate(Bitmap bmp, ColorCriteria c)
    {
        var (r, g, b) = c.SamplingMode switch
        {
            ColorSamplingMode.SinglePixel => SamplePixel(bmp, bmp.Width / 2, bmp.Height / 2),
            ColorSamplingMode.RegionAverage => SampleAverage(bmp),
            _ => throw new ArgumentOutOfRangeException()
        };

        var dr = r - c.TargetRgb.R;
        var dg = g - c.TargetRgb.G;
        var db = b - c.TargetRgb.B;
        var distance = Math.Sqrt(dr * dr + dg * dg + db * db);
        return new ColorMatchResult(new Rgb(r, g, b), distance, distance <= c.ToleranceRgb);
    }

    public bool Matches(Bitmap bmp, ColorCriteria c) => Evaluate(bmp, c).Matched;

    private static (int r, int g, int b) SamplePixel(Bitmap bmp, int x, int y)
    {
        var px = bmp.GetPixel(x, y);
        return (px.R, px.G, px.B);
    }

    private static (int r, int g, int b) SampleAverage(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            long sumR = 0, sumG = 0, sumB = 0;
            int stride = data.Stride;
            int bytes = stride * bmp.Height;
            var buffer = new byte[bytes];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buffer, 0, bytes);
            for (int y = 0; y < bmp.Height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < bmp.Width; x++)
                {
                    int i = row + x * 4;
                    sumB += buffer[i];
                    sumG += buffer[i + 1];
                    sumR += buffer[i + 2];
                }
            }
            long pixels = bmp.Width * (long)bmp.Height;
            return ((int)(sumR / pixels), (int)(sumG / pixels), (int)(sumB / pixels));
        }
        finally { bmp.UnlockBits(data); }
    }
}
