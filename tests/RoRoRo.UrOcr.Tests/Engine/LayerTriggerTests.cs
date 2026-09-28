using System.Drawing;
using System.IO;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

/// <summary>The rock cap (same layer for 5 minutes) and the camera rule (no layer for 10 s).</summary>
public class LayerTriggerTests
{
    private static readonly Rgb Navy = new(30, 30, 90);
    private static readonly Rgb Grey = new(120, 120, 120);
    private static readonly Rgb Sky = new(135, 206, 235);
    private static readonly Rgb Orange = new(240, 160, 40);

    private sealed class FakeClock : IClock { public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch; }
    private sealed class PaintedCapture : ICaptureSource
    {
        public Rgb Default = Navy;
        public Bitmap Capture(RegionRect r)
        {
            var bmp = new Bitmap(Math.Max(1, r.Width), Math.Max(1, r.Height));
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.FromArgb(Default.R, Default.G, Default.B));
            return bmp;
        }
    }
    private sealed class NoText : ITextMatchEngine
    {
        public Task<(bool matched, string text)> RunAsync(Bitmap b, TextCriteria c) => Task.FromResult((false, ""));
        public Task<(bool matched, string text)> RunWithPreprocessAsync(Bitmap b, TextCriteria c) => Task.FromResult((false, ""));
    }
    private sealed class Fg : IForegroundCheck { public bool IsAlt = true; public bool IsForegroundAnAlt() => IsAlt; public int GetForegroundPid() => 1; }
    private sealed class NotElevated : IElevationCheck { public bool IsForegroundProcessLikelyElevated(int pid) => false; }
    private sealed class NoKeys : IKeyPress { public void Press(KeyCombo c) { } }
    private sealed class FakeMetrics : IWindowMetrics
    {
        public IntPtr HwndForPid(int pid) => new(0x10);
        public (int X, int Y)? ClientOrigin(IntPtr h) => (0, 0);
        public (int W, int H)? ClientSize(IntPtr h) => (800, 599);
    }
    private sealed class RecordingMacros : IMacroRunClient
    {
        public List<string> Calls { get; } = new();
        public Task<RunMacroResponse> RunAsync(string macroId, IReadOnlyList<string>? targets, CancellationToken ct)
        {
            Calls.Add(macroId);
            return Task.FromResult(new RunMacroResponse(true, "pb", false, null, null));
        }
    }

    private static RingDefinition Ring() => new("mine8", "Mine #8", new[]
    {
        new LayerDefinition("navy", new[] { Navy }),
        new LayerDefinition("grey", new[] { Grey }),
    }, MinLayerSpots: 3);

    private static Trigger Spot(int order, bool accountAware) => new()
    {
        Id = Guid.NewGuid(), Name = $"spot {order}",
        Region = new RegionRect(100 + order * 20, 100, 9, 9), Mode = TriggerMode.Color,
        Color = new ColorCriteria(new Rgb(0, 0, 0), 20, ColorSamplingMode.SinglePixel,
            Point: new PickPoint(4, 4), Box: new SampleBox(), NoneOf: new[] { Sky }),
        Keybind = new KeyCombo("F13", Array.Empty<string>()),
        AccountAware = accountAware, CooldownMs = 100,
        Action = TriggerAction.RunMacro, MacroId = $"mine-{order}",
        Ring = new RingSpot("mine8", order),
    };

    private static Trigger LayerTrigger(string name, LayerCondition condition, int holdMs, string macro, bool accountAware) => new()
    {
        Id = Guid.NewGuid(), Name = name,
        Region = new RegionRect(0, 0, 1, 1), Mode = TriggerMode.Layer,
        Layer = new LayerCriteria("mine8", condition), HoldForMs = holdMs,
        Keybind = new KeyCombo("F13", Array.Empty<string>()),
        AccountAware = accountAware, CooldownMs = 100,
        Action = TriggerAction.RunMacro, MacroId = macro,
    };

    private sealed record Rig(TriggerCoordinator C, PaintedCapture Paint, RecordingMacros Macros, FakeClock Clock, Fg Fg, List<string> Diag);

    private static Rig Build(bool accountAware, params Trigger[] layerTriggers)
    {
        var store = new TriggerStore(Path.Combine(Path.GetTempPath(), "urocr-tests", Guid.NewGuid().ToString("N") + ".json"));
        store.UpsertRing(Ring());
        for (var order = 0; order < 8; order++) store.Add(Spot(order, accountAware));
        foreach (var t in layerTriggers) store.Add(t);
        var paint = new PaintedCapture();
        var macros = new RecordingMacros();
        var clock = new FakeClock();
        var fg = new Fg();
        var diag = new List<string>();
        var c = new TriggerCoordinator(store, paint, new ColorMatcher(), new NoText(), fg, new NotElevated(),
            new NoKeys(), new ActivityLog(), clock, new FakeMetrics(), macroClient: macros, diag: diag.Add);
        return new Rig(c, paint, macros, clock, fg, diag);
    }

    private static Task Tick(Rig rig) => rig.C.TickOnceAsync(CancellationToken.None);
    private static void Advance(Rig rig, int ms) => rig.Clock.Now = rig.Clock.Now.AddMilliseconds(ms);

    [Fact]
    public async Task Same_layer_for_the_hold_runs_the_rock_cap()
    {
        var rig = Build(false, LayerTrigger("rock cap", LayerCondition.SameLayer, 300_000, "go-to-top", false));

        await Tick(rig);
        Advance(rig, 299_999);
        await Tick(rig);
        Assert.Empty(rig.Macros.Calls);

        Advance(rig, 1);
        await Tick(rig);
        Assert.Equal(new[] { "go-to-top" }, rig.Macros.Calls);
        // Spec decision 7 (amended): the reason goes to ur-ocr.log.
        Assert.Contains("trigger \"rock cap\": macro go-to-top (Went to top: 5 minutes on the navy layer)", rig.Diag);
    }

    [Fact]
    public async Task A_layer_change_restarts_the_rock_cap()
    {
        var rig = Build(false, LayerTrigger("rock cap", LayerCondition.SameLayer, 300_000, "go-to-top", false));
        await Tick(rig);                                  // navy from t = 0

        Advance(rig, 200_000);
        rig.Paint.Default = Grey;
        await Tick(rig);                                  // grey from t = 200000
        Advance(rig, 200_000);
        await Tick(rig);                                  // t = 400000: 200 s on grey
        Assert.Empty(rig.Macros.Calls);

        Advance(rig, 100_000);
        await Tick(rig);                                  // t = 500000: 300 s on grey
        Assert.Equal(new[] { "go-to-top" }, rig.Macros.Calls);
    }

    [Fact]
    public async Task The_rock_cap_repeats_after_another_full_hold()
    {
        var rig = Build(false, LayerTrigger("rock cap", LayerCondition.SameLayer, 300_000, "go-to-top", false));
        await Tick(rig);
        Advance(rig, 300_000);
        await Tick(rig);
        Advance(rig, 300_000);
        await Tick(rig);

        Assert.Equal(new[] { "go-to-top", "go-to-top" }, rig.Macros.Calls);
    }

    [Fact]
    public async Task No_layer_for_ten_seconds_runs_the_camera_macro()
    {
        var rig = Build(false, LayerTrigger("camera", LayerCondition.NoLayer, 10_000, "camera-top-down", false));
        rig.Paint.Default = Orange;                       // no spot matches any rock: no layer

        await Tick(rig);
        Advance(rig, 9_999);
        await Tick(rig);
        Assert.Empty(rig.Macros.Calls);                   // ore spots do not fire without a layer either

        Advance(rig, 1);
        await Tick(rig);
        Assert.Equal(new[] { "camera-top-down" }, rig.Macros.Calls);
    }

    [Fact]
    public async Task Tabbing_away_is_not_no_layer()
    {
        var rig = Build(true, LayerTrigger("camera", LayerCondition.NoLayer, 10_000, "camera-top-down", true));
        rig.Paint.Default = Orange;
        rig.Fg.IsAlt = false;

        await Tick(rig);
        Advance(rig, 20_000);
        await Tick(rig);
        Assert.Empty(rig.Macros.Calls);
        Assert.Equal(RingStatus.Unknown, rig.C.Rings.Get("mine8").Status);

        rig.Fg.IsAlt = true;
        await Tick(rig);                                  // t = 20000: no layer starts now
        Advance(rig, 9_999);
        await Tick(rig);
        Assert.Empty(rig.Macros.Calls);

        Advance(rig, 1);
        await Tick(rig);
        Assert.Equal(new[] { "camera-top-down" }, rig.Macros.Calls);
    }
}
