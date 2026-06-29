// tests/RoRoRo.UrOcr.Tests/Engine/PreviewEvaluatorTests.cs
using System.Drawing;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

public class PreviewEvaluatorTests
{
    private sealed class FakeCapture : ICaptureSource
    {
        private readonly Color _fill;
        public FakeCapture(int r, int g, int b) => _fill = Color.FromArgb(r, g, b);
        public Bitmap Capture(RegionRect region)
        {
            var bmp = new Bitmap(Math.Max(1, region.Width), Math.Max(1, region.Height));
            using var gfx = Graphics.FromImage(bmp);
            gfx.Clear(_fill);
            return bmp;
        }
    }

    [Fact]
    public void EvaluateOnce_ReturnsMatchResult_ForCapturedColor()
    {
        var pe = new PreviewEvaluator(new FakeCapture(255, 17, 95), new ColorMatcher());
        var region = new RegionRect(0, 0, 4, 4);
        var crit = new ColorCriteria(new Rgb(255, 17, 95), 10, ColorSamplingMode.SinglePixel);

        var r = pe.EvaluateOnce(region, crit);

        Assert.NotNull(r);
        Assert.True(r!.Matched);
        Assert.True(r.Distance < 0.001);
    }

    [Fact]
    public void EvaluateOnce_DegenerateRegion_ReturnsNull()
    {
        var pe = new PreviewEvaluator(new FakeCapture(0, 0, 0), new ColorMatcher());
        Assert.Null(pe.EvaluateOnce(new RegionRect(0, 0, 0, 0), new ColorCriteria(new Rgb(0, 0, 0), 5, ColorSamplingMode.SinglePixel)));
    }
}
