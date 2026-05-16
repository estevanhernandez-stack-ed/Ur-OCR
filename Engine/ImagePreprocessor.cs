using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace RoRoRo.UrOcr.Engine;

public static class ImagePreprocessor
{
    public static Bitmap Preprocess(Bitmap source)
    {
        var gray = ToGrayscale(source);
        var bin = Binarize(gray);
        gray.Dispose();
        var up = Upscale2x(bin);
        bin.Dispose();
        return up;
    }

    private static Bitmap ToGrayscale(Bitmap src)
    {
        var dst = new Bitmap(src.Width, src.Height);
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
            {
                var px = src.GetPixel(x, y);
                var lum = (int)(0.299 * px.R + 0.587 * px.G + 0.114 * px.B);
                dst.SetPixel(x, y, Color.FromArgb(lum, lum, lum));
            }
        return dst;
    }

    private static Bitmap Binarize(Bitmap gray)
    {
        var dst = new Bitmap(gray.Width, gray.Height);
        const int threshold = 128;
        for (int y = 0; y < gray.Height; y++)
            for (int x = 0; x < gray.Width; x++)
            {
                var v = gray.GetPixel(x, y).R;
                var c = v < threshold ? Color.Black : Color.White;
                dst.SetPixel(x, y, c);
            }
        return dst;
    }

    private static Bitmap Upscale2x(Bitmap src)
    {
        var dst = new Bitmap(src.Width * 2, src.Height * 2);
        using var g = Graphics.FromImage(dst);
        g.InterpolationMode = InterpolationMode.Bilinear;
        g.DrawImage(src, 0, 0, dst.Width, dst.Height);
        return dst;
    }
}
