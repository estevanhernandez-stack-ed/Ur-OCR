using Xunit;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Tests.Engine;

public class CaptureEngineSmokeTests
{
    [Fact]
    public void Capture_returns_bitmap_of_requested_size()
    {
        var e = new CaptureEngine();
        using var bmp = e.Capture(new RegionRect(0, 0, 10, 10));
        Assert.Equal(10, bmp.Width);
        Assert.Equal(10, bmp.Height);
    }
}
