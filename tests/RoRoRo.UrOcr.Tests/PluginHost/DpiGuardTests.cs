using System;
using System.IO;
using Xunit;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Tests.PluginHost;

public class DpiGuardTests : IDisposable
{
    private readonly string _tempPath = Path.Combine(Path.GetTempPath(), $"urocr-dpi-{Guid.NewGuid()}.json");
    public void Dispose() { if (File.Exists(_tempPath)) File.Delete(_tempPath); }

    [Fact]
    public void First_run_writes_fingerprint_returns_FirstRun()
    {
        var g = new DpiGuard(_tempPath);
        var fp = new DisplayFingerprint(1920, 1080, 96);
        Assert.Equal(DisplayCheckResult.FirstRun, g.Check(fp, Array.Empty<RegionRect>()));
        Assert.True(File.Exists(_tempPath));
    }

    [Fact]
    public void Match_when_unchanged()
    {
        var g = new DpiGuard(_tempPath);
        var fp = new DisplayFingerprint(1920, 1080, 96);
        g.Check(fp, Array.Empty<RegionRect>());
        Assert.Equal(DisplayCheckResult.Match, g.Check(fp, Array.Empty<RegionRect>()));
    }

    [Fact]
    public void Changed_with_stale_region_returns_stale()
    {
        var g = new DpiGuard(_tempPath);
        g.Check(new DisplayFingerprint(3840, 1080, 96), Array.Empty<RegionRect>());
        var region = new RegionRect(2000, 500, 100, 100);
        Assert.Equal(DisplayCheckResult.ChangedWithStaleRegions,
            g.Check(new DisplayFingerprint(1920, 1080, 96), new[] { region }));
    }

    [Fact]
    public void Changed_but_regions_still_inside_returns_clean()
    {
        var g = new DpiGuard(_tempPath);
        g.Check(new DisplayFingerprint(3840, 1080, 96), Array.Empty<RegionRect>());
        var region = new RegionRect(100, 100, 100, 100);
        Assert.Equal(DisplayCheckResult.ChangedRegionsStillInside,
            g.Check(new DisplayFingerprint(1920, 1080, 96), new[] { region }));
    }
}
