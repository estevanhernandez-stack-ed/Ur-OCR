using System.Drawing;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

public class FramePixelsTests
{
    [Fact]
    public void FromBitmap_reads_every_pixel_as_rgb()
    {
        using var bmp = new Bitmap(3, 2);
        bmp.SetPixel(2, 1, Color.FromArgb(255, 200, 100, 50));

        var frame = FramePixels.FromBitmap(bmp);

        Assert.Equal((3, 2), (frame.Width, frame.Height));
        Assert.Equal(new Rgb(200, 100, 50), frame.At(2, 1));
        Assert.Equal(new Rgb(0, 0, 0), frame.At(0, 0));
    }
}
