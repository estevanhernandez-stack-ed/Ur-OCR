using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

public class WindowSpaceMathTests
{
    [Fact]
    public void ToClientRegion_SubtractsOrigin()
    {
        var r = WindowSpaceMath.ToClientRegion(new RegionRect(150, 260, 30, 40), (100, 200));
        Assert.Equal(new RegionRect(50, 60, 30, 40), r);
    }

    [Fact]
    public void ToScreenRegion_SameSize_AddsOrigin()
    {
        var r = WindowSpaceMath.ToScreenRegion(new RegionRect(50, 60, 30, 40), (100, 200), (800, 600), (800, 600));
        Assert.Equal(new RegionRect(150, 260, 30, 40), r);
    }

    [Fact]
    public void ToScreenRegion_ScalesUp_WhenWindowLarger()
    {
        // Recorded 800x600, now 1600x1200 (2x). Client region (50,60,30,40) doubles.
        var r = WindowSpaceMath.ToScreenRegion(new RegionRect(50, 60, 30, 40), (0, 0), (800, 600), (1600, 1200));
        Assert.Equal(new RegionRect(100, 120, 60, 80), r);
    }

    [Fact]
    public void ToScreenRegion_ScalesDown_AndOffsets()
    {
        // Recorded 800x600, now 400x300 (0.5x), origin (10,20).
        var r = WindowSpaceMath.ToScreenRegion(new RegionRect(50, 60, 30, 40), (10, 20), (800, 600), (400, 300));
        Assert.Equal(new RegionRect(10 + 25, 20 + 30, 15, 20), r);
    }

    [Fact]
    public void ToScreenRegion_ZeroRecordedSize_FallsBackToOffsetOnly()
    {
        // Guard div-by-zero: no scale, just offset.
        var r = WindowSpaceMath.ToScreenRegion(new RegionRect(50, 60, 30, 40), (10, 20), (0, 0), (400, 300));
        Assert.Equal(new RegionRect(60, 80, 30, 40), r);
    }
}
