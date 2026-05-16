using System.Drawing;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

public class ColorMatcherTests
{
    private static Bitmap SolidBitmap(int w, int h, Color c)
    {
        var b = new Bitmap(w, h);
        using var g = Graphics.FromImage(b);
        g.Clear(c);
        return b;
    }

    [Fact]
    public void SinglePixel_matches_within_tolerance()
    {
        using var bmp = SolidBitmap(10, 10, Color.FromArgb(255, 215, 0));
        var m = new ColorMatcher();
        Assert.True(m.Matches(bmp, new ColorCriteria(new Rgb(255, 215, 0), 5, ColorSamplingMode.SinglePixel)));
    }

    [Fact]
    public void SinglePixel_misses_outside_tolerance()
    {
        using var bmp = SolidBitmap(10, 10, Color.FromArgb(255, 215, 0));
        var m = new ColorMatcher();
        Assert.False(m.Matches(bmp, new ColorCriteria(new Rgb(0, 0, 0), 10, ColorSamplingMode.SinglePixel)));
    }

    [Fact]
    public void RegionAverage_matches_solid()
    {
        using var bmp = SolidBitmap(20, 20, Color.FromArgb(100, 100, 100));
        var m = new ColorMatcher();
        Assert.True(m.Matches(bmp, new ColorCriteria(new Rgb(100, 100, 100), 5, ColorSamplingMode.RegionAverage)));
    }

    [Fact]
    public void Zero_tolerance_exact_match()
    {
        using var bmp = SolidBitmap(5, 5, Color.FromArgb(50, 60, 70));
        var m = new ColorMatcher();
        Assert.True(m.Matches(bmp, new ColorCriteria(new Rgb(50, 60, 70), 0, ColorSamplingMode.SinglePixel)));
        Assert.False(m.Matches(bmp, new ColorCriteria(new Rgb(51, 60, 70), 0, ColorSamplingMode.SinglePixel)));
    }
}
