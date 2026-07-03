using System.Drawing;
using System.IO;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

public class DryRunTests
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

    private (TriggerCoordinator, FakeColor, FakeKeys, ActivityLog, TriggerStore) Make()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dr-{Guid.NewGuid()}.json");
        var store = new TriggerStore(path);
        var clock = new FakeClock();
        var color = new FakeColor();
        var text = new FakeText();
        var fg = new FakeFg();
        var elev = new FakeElev();
        var keys = new FakeKeys();
        var log = new ActivityLog();
        var c = new TriggerCoordinator(store, new FakeCapture(), color, text, fg, elev, keys, log, clock, new FakeMetrics());
        return (c, color, keys, log, store);
    }

    private static Trigger ColorTrigger() => new()
    {
        Id = Guid.NewGuid(),
        Name = "T",
        Region = new RegionRect(0, 0, 10, 10),
        Mode = TriggerMode.Color,
        Color = new ColorCriteria(new Rgb(0, 0, 0), 5, ColorSamplingMode.SinglePixel),
        Keybind = new KeyCombo("A", Array.Empty<string>()),
        CooldownMs = 100,
        AccountAware = false,
    };

    [Fact]
    public async Task DryRun_true_logs_WouldFire_and_does_not_press_key()
    {
        var (c, color, keys, log, store) = Make();
        store.Add(ColorTrigger());
        color.Result = true;
        c.DryRun = true;

        await c.TickOnceAsync(CancellationToken.None);

        Assert.Equal(0, keys.Pressed);
        Assert.Contains(log.Snapshot(), e => e.Kind == ActivityKind.WouldFire);
    }

    [Fact]
    public async Task DryRun_false_presses_key_and_logs_Fired()
    {
        var (c, color, keys, log, store) = Make();
        store.Add(ColorTrigger());
        color.Result = true;
        c.DryRun = false;

        await c.TickOnceAsync(CancellationToken.None);

        Assert.Equal(1, keys.Pressed);
        Assert.Contains(log.Snapshot(), e => e.Kind == ActivityKind.Fired);
    }
}
