using System;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

public class TriggerRegionResolverTests
{
    private sealed class FakeMetrics : IWindowMetrics
    {
        public IntPtr Hwnd = new(0x10);
        public (int X, int Y)? Origin = (0, 0);
        public (int W, int H)? Size = (800, 600);
        public IntPtr HwndForPid(int pid) => pid == 0 ? IntPtr.Zero : Hwnd;
        public (int X, int Y)? ClientOrigin(IntPtr h) => h == IntPtr.Zero ? null : Origin;
        public (int W, int H)? ClientSize(IntPtr h) => h == IntPtr.Zero ? null : Size;
    }

    private static Trigger Screen() => new()
    {
        Id = Guid.NewGuid(), Name = "s", Region = new RegionRect(10, 20, 30, 40),
        Mode = TriggerMode.Color, Keybind = new KeyCombo("F", Array.Empty<string>()),
        CoordSpace = Trigger.CoordSpaceScreen,
    };

    private static Trigger Client() => new()
    {
        Id = Guid.NewGuid(), Name = "c", Region = new RegionRect(50, 60, 30, 40),
        Mode = TriggerMode.Color, Keybind = new KeyCombo("F", Array.Empty<string>()),
        CoordSpace = Trigger.CoordSpaceClient, RecordedClientW = 800, RecordedClientH = 600,
    };

    [Fact]
    public void ScreenTrigger_ReturnsAbsoluteRegion_IgnoringMetrics()
    {
        var r = TriggerRegionResolver.Resolve(Screen(), anchorPid: 111, new FakeMetrics());
        Assert.Equal(new RegionRect(10, 20, 30, 40), r);
    }

    [Fact]
    public void ClientTrigger_WindowMoved_OffsetsRegion()
    {
        var m = new FakeMetrics { Origin = (100, 200), Size = (800, 600) };
        var r = TriggerRegionResolver.Resolve(Client(), anchorPid: 111, m);
        Assert.Equal(new RegionRect(150, 260, 30, 40), r);
    }

    [Fact]
    public void ClientTrigger_WindowResized_ScalesRegion()
    {
        var m = new FakeMetrics { Origin = (0, 0), Size = (1600, 1200) };
        var r = TriggerRegionResolver.Resolve(Client(), anchorPid: 111, m);
        Assert.Equal(new RegionRect(100, 120, 60, 80), r);
    }

    [Fact]
    public void ClientTrigger_NoAnchorPid_ReturnsNull()
    {
        var r = TriggerRegionResolver.Resolve(Client(), anchorPid: 0, new FakeMetrics());
        Assert.Null(r);
    }

    [Fact]
    public void ClientTrigger_WindowGone_ReturnsNull()
    {
        var m = new FakeMetrics { Origin = null, Size = null };
        var r = TriggerRegionResolver.Resolve(Client(), anchorPid: 111, m);
        Assert.Null(r);
    }
}
