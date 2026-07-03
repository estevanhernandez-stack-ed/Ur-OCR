using System;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

public class TriggerCoordinatorWindowTests
{
    // --- reuse the fake shapes from TriggerCoordinatorTests.cs ---
    private sealed class FakeCapture : ICaptureSource
    {
        public RegionRect? LastRegion;
        public System.Drawing.Bitmap Capture(RegionRect region)
        { LastRegion = region; return new System.Drawing.Bitmap(1, 1); }
    }
    private sealed class MatchAll : IColorMatchEngine
    {
        public bool Matches(System.Drawing.Bitmap b, ColorCriteria c) => true;
        public ColorMatchResult Evaluate(System.Drawing.Bitmap b, ColorCriteria c) => new(new Rgb(0,0,0), 0, true);
    }
    private sealed class NoText : ITextMatchEngine
    {
        public System.Threading.Tasks.Task<(bool matched, string text)> RunAsync(System.Drawing.Bitmap b, TextCriteria c) => System.Threading.Tasks.Task.FromResult((false, ""));
        public System.Threading.Tasks.Task<(bool matched, string text)> RunWithPreprocessAsync(System.Drawing.Bitmap b, TextCriteria c) => System.Threading.Tasks.Task.FromResult((false, ""));
    }
    private sealed class Fg : IForegroundCheck { public bool IsAlt = true; public int Pid = 111; public bool IsForegroundAnAlt() => IsAlt; public int GetForegroundPid() => Pid; }
    private sealed class NotElevated : IElevationCheck { public bool IsForegroundProcessLikelyElevated(int pid) => false; }
    private sealed class NoKeys : IKeyPress { public void Press(KeyCombo c) { } }
    private sealed class FixedClock : IClock { public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch; }
    private sealed class FakeMetrics : IWindowMetrics
    {
        public (int X, int Y)? Origin = (100, 200);
        public (int W, int H)? Size = (800, 600);
        public IntPtr HwndForPid(int pid) => new(0x10);
        public (int X, int Y)? ClientOrigin(IntPtr h) => Origin;
        public (int W, int H)? ClientSize(IntPtr h) => Size;
    }

    private static Trigger ClientColorTrigger() => new()
    {
        Id = Guid.NewGuid(), Name = "c", Enabled = true,
        Region = new RegionRect(50, 60, 30, 40), Mode = TriggerMode.Color,
        Color = new ColorCriteria(new Rgb(0,0,0), 10, ColorSamplingMode.SinglePixel),
        Keybind = new KeyCombo("F", Array.Empty<string>()),
        CoordSpace = Trigger.CoordSpaceClient, RecordedClientW = 800, RecordedClientH = 600,
        AccountAware = false, // proves client anchoring is independent of the flag
    };

    private static (TriggerCoordinator, FakeCapture, TriggerStore) Build(Fg fg, FakeMetrics m, Trigger t)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "urocr-tests", Guid.NewGuid().ToString("N") + ".json");
        var store = new TriggerStore(path);
        store.Add(t);
        var cap = new FakeCapture();
        var coord = new TriggerCoordinator(store, cap, new MatchAll(), new NoText(), fg, new NotElevated(), new NoKeys(),
            new ActivityLog(), new FixedClock(), m);
        return (coord, cap, store);
    }

    [Fact]
    public async Task ClientTrigger_CapturesAnchoredRegion_WindowMoved()
    {
        var (coord, cap, _) = Build(new Fg { IsAlt = true, Pid = 111 }, new FakeMetrics { Origin = (100, 200), Size = (800, 600) }, ClientColorTrigger());
        await coord.TickOnceAsync(System.Threading.CancellationToken.None);
        Assert.Equal(new RegionRect(150, 260, 30, 40), cap.LastRegion);
    }

    [Fact]
    public async Task ClientTrigger_Skips_WhenForegroundNotAlt()
    {
        var (coord, cap, _) = Build(new Fg { IsAlt = false, Pid = 999 }, new FakeMetrics(), ClientColorTrigger());
        await coord.TickOnceAsync(System.Threading.CancellationToken.None);
        Assert.Null(cap.LastRegion); // never captured
    }
}
