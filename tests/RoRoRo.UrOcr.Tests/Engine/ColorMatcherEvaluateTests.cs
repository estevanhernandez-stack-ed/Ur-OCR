// tests/RoRoRo.UrOcr.Tests/Engine/ColorMatcherEvaluateTests.cs
using System.Drawing;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

public class ColorMatcherEvaluateTests
{
    private static Bitmap Solid(int r, int g, int b)
    {
        var bmp = new Bitmap(4, 4);
        using var gfx = Graphics.FromImage(bmp);
        gfx.Clear(Color.FromArgb(r, g, b));
        return bmp;
    }

    [Fact]
    public void Evaluate_ExactColor_ZeroDistance_Matches()
    {
        var matcher = new ColorMatcher();
        using var bmp = Solid(255, 17, 95); // 0xFF115F
        var crit = new ColorCriteria(new Rgb(255, 17, 95), ToleranceRgb: 10, ColorSamplingMode.SinglePixel);

        var r = matcher.Evaluate(bmp, crit);

        Assert.Equal(new Rgb(255, 17, 95), r.Sampled);
        Assert.True(r.Distance < 0.001);
        Assert.True(r.Matched);
    }

    [Fact]
    public void Evaluate_OutsideTolerance_DoesNotMatch_ButReportsDistance()
    {
        var matcher = new ColorMatcher();
        using var bmp = Solid(255, 17, 95);
        var crit = new ColorCriteria(new Rgb(0, 0, 0), ToleranceRgb: 10, ColorSamplingMode.SinglePixel);

        var r = matcher.Evaluate(bmp, crit);

        Assert.False(r.Matched);
        Assert.True(r.Distance > 10);
    }

    [Fact]
    public void Matches_StillAgreesWithEvaluate()
    {
        var matcher = new ColorMatcher();
        using var bmp = Solid(100, 100, 100);
        var crit = new ColorCriteria(new Rgb(100, 100, 100), 5, ColorSamplingMode.RegionAverage);
        Assert.Equal(matcher.Evaluate(bmp, crit).Matched, matcher.Matches(bmp, crit));
    }
}
