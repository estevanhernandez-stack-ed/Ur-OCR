using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Tests.Engine;

/// <summary>Hand-painted frames for the ore finder: a solid colour with rectangles over it.</summary>
internal static class Frames
{
    public static int[] Solid(int w, int h, Rgb c) => Enumerable.Repeat(FramePixels.Pack(c), w * h).ToArray();

    public static void Fill(int[] px, int w, int x, int y, int bw, int bh, Rgb c)
    {
        var p = FramePixels.Pack(c);
        for (var yy = y; yy < y + bh; yy++)
            for (var xx = x; xx < x + bw; xx++)
                px[yy * w + xx] = p;
    }

    /// <summary>A block grid: <paramref name="block"/> everywhere, with <paramref name="lineWidth"/> px
    /// lines of <paramref name="line"/> every <paramref name="spacingX"/> px across and every
    /// <paramref name="spacingY"/> px down, the first at <paramref name="offset"/>.</summary>
    public static int[] Grid(int w, int h, int spacingX, int spacingY, Rgb block, Rgb line, int lineWidth = 2, int offset = 7)
    {
        var px = Solid(w, h, block);
        for (var x = offset; x < w; x += spacingX) Fill(px, w, x, 0, Math.Min(lineWidth, w - x), h, line);
        for (var y = offset; y < h; y += spacingY) Fill(px, w, 0, y, w, Math.Min(lineWidth, h - y), line);
        return px;
    }
}
