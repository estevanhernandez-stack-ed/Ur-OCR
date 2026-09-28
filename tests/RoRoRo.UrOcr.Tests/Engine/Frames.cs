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
}
