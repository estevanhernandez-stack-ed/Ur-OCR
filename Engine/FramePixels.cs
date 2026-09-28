using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

/// <summary>A captured frame as packed 0xRRGGBB pixels, row by row. Pure, so the ore finder runs on
/// a test's hand-painted frame exactly as on a capture.</summary>
public sealed class FramePixels
{
    private readonly int[] _rgb;

    public FramePixels(int width, int height, int[] rgb)
    {
        if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width), "A frame needs at least one pixel.");
        if (rgb.Length != width * height)
            throw new ArgumentException($"Expected {width * height} pixels, got {rgb.Length}.", nameof(rgb));
        Width = width;
        Height = height;
        _rgb = rgb;
    }

    public int Width { get; }
    public int Height { get; }

    public Rgb At(int x, int y)
    {
        var p = _rgb[y * Width + x];
        return new Rgb((p >> 16) & 0xFF, (p >> 8) & 0xFF, p & 0xFF);
    }

    public static int Pack(Rgb c) => (c.R << 16) | (c.G << 8) | c.B;

    public static FramePixels FromBitmap(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[data.Stride * bmp.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var rgb = new int[bmp.Width * bmp.Height];
            for (var y = 0; y < bmp.Height; y++)
                for (var x = 0; x < bmp.Width; x++)
                {
                    var i = y * data.Stride + x * 4;   // B, G, R, A
                    rgb[y * bmp.Width + x] = (bytes[i + 2] << 16) | (bytes[i + 1] << 8) | bytes[i];
                }
            return new FramePixels(bmp.Width, bmp.Height, rgb);
        }
        finally { bmp.UnlockBits(data); }
    }
}
