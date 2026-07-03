using System.Drawing;
using System.IO;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

public class TriggerCoordinatorTests
{
    private sealed class FakeClock : IClock { public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow; }
    private sealed class FakeCapture : ICaptureSource { public Bitmap Capture(RegionRect r) => new(r.Width, r.Height); }
    private sealed class FakeColor : IColorMatchEngine
    {
        public bool Result;
        public bool Matches(Bitmap b, ColorCriteria c) => Result;
        public ColorMatchResult Evaluate(Bitmap b, ColorCriteria c) => new(new RoRoRo.UrOcr.Storage.Rgb(0, 0, 0), 0, Result);
    }
    private sealed class FakeText : ITextMatchEngine
    {
        public bool Result;
        public Task<(bool, string)> RunAsync(Bitmap b, TextCriteria c) => Task.FromResult((Result, ""));
        public Task<(bool, string)> RunWithPreprocessAsync(Bitmap b, TextCriteria c) => Task.FromResult((Result, ""));
    }
    private sealed class FakeFg : IForegroundCheck { public bool IsAlt; public bool IsForegroundAnAlt() => IsAlt; public int GetForegroundPid() => 1; }
    private sealed class FakeElev : IElevationCheck { public bool Elev; public bool IsForegroundProcessLikelyElevated(int pid) => Elev; }
    private sealed class FakeKeys : IKeyPress { public int Pressed; public void Press(KeyCombo c) => Pressed++; }
    private sealed class FakeMetrics : IWindowMetrics
    {
        public (int X, int Y)? Origin = (100, 200);
        public (int W, int H)? Size = (800, 600);
        public IntPtr HwndForPid(int pid) => new(0x10);
        public (int X, int Y)? ClientOrigin(IntPtr h) => Origin;
        public (int W, int H)? ClientSize(IntPtr h) => Size;
    }

    private (TriggerCoordinator, FakeClock, FakeColor, FakeFg, FakeElev, FakeKeys, ActivityLog, TriggerStore) Make()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tc-{Guid.NewGuid()}.json");
        var store = new TriggerStore(path);
        var clock = new FakeClock();
        var color = new FakeColor();
        var text = new FakeText();
        var fg = new FakeFg();
        var elev = new FakeElev();
        var keys = new FakeKeys();
        var log = new ActivityLog();
        var c = new TriggerCoordinator(store, new FakeCapture(), color, text, fg, elev, keys, log, clock, new FakeMetrics());
        return (c, clock, color, fg, elev, keys, log, store);
    }

    private static Trigger ColorTrigger(bool accountAware = false) => new()
    {
        Id = Guid.NewGuid(),
        Name = "T",
        Region = new RegionRect(0, 0, 10, 10),
        Mode = TriggerMode.Color,
        Color = new ColorCriteria(new Rgb(0, 0, 0), 5, ColorSamplingMode.SinglePixel),
        Keybind = new KeyCombo("A", Array.Empty<string>()),
        CooldownMs = 100,
        AccountAware = accountAware,
    };

    [Fact]
    public async Task Edge_trigger_fires_only_on_transition()
    {
        var (c, _, color, _, _, keys, _, store) = Make();
        store.Add(ColorTrigger());
        color.Result = true;
        await c.TickOnceAsync(CancellationToken.None);
        Assert.Equal(1, keys.Pressed);
        await c.TickOnceAsync(CancellationToken.None);
        Assert.Equal(1, keys.Pressed);
    }

    [Fact]
    public async Task No_match_then_match_fires()
    {
        var (c, clock, color, _, _, keys, _, store) = Make();
        store.Add(ColorTrigger());
        color.Result = false;
        await c.TickOnceAsync(CancellationToken.None);
        color.Result = true;
        clock.Now = clock.Now.AddMilliseconds(200);
        await c.TickOnceAsync(CancellationToken.None);
        Assert.Equal(1, keys.Pressed);
    }

    [Fact]
    public async Task Cooldown_blocks_refire()
    {
        var (c, clock, color, _, _, keys, _, store) = Make();
        store.Add(ColorTrigger());
        color.Result = true;
        await c.TickOnceAsync(CancellationToken.None);
        color.Result = false;
        await c.TickOnceAsync(CancellationToken.None);
        color.Result = true;
        clock.Now = clock.Now.AddMilliseconds(50);
        await c.TickOnceAsync(CancellationToken.None);
        Assert.Equal(1, keys.Pressed);
        clock.Now = clock.Now.AddMilliseconds(100);
        color.Result = false;
        await c.TickOnceAsync(CancellationToken.None);
        color.Result = true;
        await c.TickOnceAsync(CancellationToken.None);
        Assert.Equal(2, keys.Pressed);
    }

    [Fact]
    public async Task Account_aware_skips_when_not_alt_foreground()
    {
        var (c, _, color, fg, _, keys, log, store) = Make();
        store.Add(ColorTrigger(accountAware: true));
        color.Result = true;
        fg.IsAlt = false;
        await c.TickOnceAsync(CancellationToken.None);
        Assert.Equal(0, keys.Pressed);
        Assert.Contains(log.Snapshot(), e => e.Kind == ActivityKind.SkippedNotAlt);
    }

    [Fact]
    public async Task Account_aware_blocks_when_foreground_elevated()
    {
        var (c, _, color, fg, elev, keys, log, store) = Make();
        store.Add(ColorTrigger(accountAware: true));
        color.Result = true;
        fg.IsAlt = true;
        elev.Elev = true;
        await c.TickOnceAsync(CancellationToken.None);
        Assert.Equal(0, keys.Pressed);
        Assert.Contains(log.Snapshot(), e => e.Kind == ActivityKind.BlockedElevated);
    }

    [Fact]
    public async Task Paused_no_fires()
    {
        var (c, _, color, _, _, keys, _, store) = Make();
        store.Add(ColorTrigger());
        color.Result = true;
        c.Paused = true;
        await c.TickOnceAsync(CancellationToken.None);
        Assert.Equal(0, keys.Pressed);
    }
}
