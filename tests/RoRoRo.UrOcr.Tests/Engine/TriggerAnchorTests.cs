using System;
using System.Collections.Generic;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

public class TriggerAnchorTests
{
    // Alt pid 111 → window at origin (100,200), client 800x600 (covers 100..900, 200..800).
    private sealed class FakeMetrics : IWindowMetrics
    {
        public IntPtr HwndForPid(int pid) => pid == 111 ? new(0x11) : IntPtr.Zero;
        public (int X, int Y)? ClientOrigin(IntPtr h) => h == new IntPtr(0x11) ? (100, 200) : null;
        public (int W, int H)? ClientSize(IntPtr h) => h == new IntPtr(0x11) ? (800, 600) : null;
    }

    [Fact]
    public void RegionOverAlt_ProducesClientSpace_WithOffsetAndRecordedSize()
    {
        // Region centered at (250, 300) — inside 100..900 / 200..800 → anchors to pid 111.
        var res = TriggerAnchor.ForPickedRegion(new RegionRect(200, 280, 100, 40), new[] { 111 }, new FakeMetrics());
        Assert.Equal(Trigger.CoordSpaceClient, res.CoordSpace);
        Assert.Equal(new RegionRect(100, 80, 100, 40), res.Region); // (200-100, 280-200)
        Assert.Equal(800, res.RecordedClientW);
        Assert.Equal(600, res.RecordedClientH);
    }

    [Fact]
    public void RegionOverNoAlt_StaysScreen()
    {
        // Region far outside the alt window → screen.
        var res = TriggerAnchor.ForPickedRegion(new RegionRect(2000, 2000, 20, 20), new[] { 111 }, new FakeMetrics());
        Assert.Equal(Trigger.CoordSpaceScreen, res.CoordSpace);
        Assert.Equal(new RegionRect(2000, 2000, 20, 20), res.Region);
        Assert.Null(res.RecordedClientW);
    }

    [Fact]
    public void NoAltsRunning_StaysScreen()
    {
        var res = TriggerAnchor.ForPickedRegion(new RegionRect(200, 280, 10, 10), Array.Empty<int>(), new FakeMetrics());
        Assert.Equal(Trigger.CoordSpaceScreen, res.CoordSpace);
    }
}
