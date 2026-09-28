using System.Drawing;
using System.IO;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

/// <summary>
/// Ring spots end to end: real ColorMatcher, a capture that paints each spot a
/// colour, the ring tracker picking the layer, and ring order deciding who fires.
/// </summary>
public class RingCoordinatorTests
{
    private static readonly Rgb Navy = new(30, 30, 90);
    private static readonly Rgb Grey = new(120, 120, 120);
    private static readonly Rgb Sky = new(135, 206, 235);
    private static readonly Rgb Orange = new(240, 160, 40);
    private static readonly Rgb Violet = new(170, 60, 220);

    private sealed class FakeClock : IClock { public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch; }

    /// <summary>Paints the whole capture one colour, chosen by the region's X.</summary>
    private sealed class PaintedCapture : ICaptureSource
    {
        public Rgb Default = Navy;
        public Dictionary<int, Rgb> ByX { get; } = new();
        public Bitmap Capture(RegionRect r)
        {
            var c = ByX.TryGetValue(r.X, out var v) ? v : Default;
            var bmp = new Bitmap(Math.Max(1, r.Width), Math.Max(1, r.Height));
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.FromArgb(c.R, c.G, c.B));
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
        public RunMacroResponse Response { get; set; } = new(true, "pb", false, null, null);
        public Task<RunMacroResponse> RunAsync(string macroId, IReadOnlyList<string>? targets, CancellationToken ct)
        {
            Calls.Add(macroId);
            return Task.FromResult(Response);
        }
    }

    private static readonly RunMacroResponse Busy = new(false, null, false, "busy", "A sequence is already running.");
    private static readonly RunMacroResponse Accepted = new(true, "pb", false, null, null);

    private static RingDefinition Ring() => new("mine8", "Mine #8", new[]
    {
        new LayerDefinition("navy", new[] { Navy }),
        new LayerDefinition("grey", new[] { Grey }),
    }, MinLayerSpots: 3);

    private static int X(int order) => 100 + order * 20;

    private static Trigger Spot(int order, bool accountAware, string ringId = "mine8") => new()
    {
        Id = Guid.NewGuid(),
        Name = $"spot {order}",
        Region = new RegionRect(X(order), 100, 9, 9),
        Mode = TriggerMode.Color,
        Color = new ColorCriteria(new Rgb(0, 0, 0), 20, ColorSamplingMode.SinglePixel,
            Point: new PickPoint(4, 4), Box: new SampleBox(), NoneOf: new[] { Sky }),
        Keybind = new KeyCombo("F13", Array.Empty<string>()),
        AccountAware = accountAware,
        CooldownMs = 100,
        Action = TriggerAction.RunMacro,
        MacroId = $"mine-{order}",
        Ring = new RingSpot(ringId, order),
    };

    private sealed record Rig(TriggerCoordinator C, PaintedCapture Paint, RecordingMacros Macros,
        ActivityLog Log, FakeClock Clock, Fg Fg, List<string> Diag, TriggerStore Store);

    private static Rig Build(bool accountAware = false)
    {
        var store = new TriggerStore(Path.Combine(Path.GetTempPath(), "urocr-tests", Guid.NewGuid().ToString("N") + ".json"));
        store.UpsertRing(Ring());
        // Added out of ring order on purpose: firing order must come from RingSpot.Order.
        foreach (var order in new[] { 7, 3, 0, 5, 1, 6, 2, 4 }) store.Add(Spot(order, accountAware));
        var paint = new PaintedCapture();
        var macros = new RecordingMacros();
        var log = new ActivityLog(capacity: 1000);
        var clock = new FakeClock();
        var fg = new Fg();
        var diag = new List<string>();
        var c = new TriggerCoordinator(store, paint, new ColorMatcher(), new NoText(), fg, new NotElevated(),
            new NoKeys(), log, clock, new FakeMetrics(), macroClient: macros, diag: diag.Add);
        return new Rig(c, paint, macros, log, clock, fg, diag, store);
    }

    private static Task Tick(Rig rig) => rig.C.TickOnceAsync(CancellationToken.None);

    [Fact]
    public async Task Plain_rock_all_round_fires_nothing_and_reads_the_layer()
    {
        var rig = Build();

        await Tick(rig);

        Assert.Empty(rig.Macros.Calls);
        var s = rig.C.Rings.Get("mine8");
        Assert.Equal(RingStatus.OnLayer, s.Status);
        Assert.Equal("navy", s.Layer);
        Assert.Equal(8, s.Votes);
    }

    [Fact]
    public async Task One_ore_spot_fires_its_own_macro()
    {
        var rig = Build();
        rig.Paint.ByX[X(3)] = Orange;

        await Tick(rig);

        Assert.Equal(new[] { "mine-3" }, rig.Macros.Calls);
    }

    [Fact]
    public async Task Several_ore_spots_fire_in_ring_order_one_per_tick()
    {
        var rig = Build();
        rig.Paint.ByX[X(5)] = Orange;
        rig.Paint.ByX[X(2)] = Violet;

        await Tick(rig);
        Assert.Equal(new[] { "mine-2" }, rig.Macros.Calls);
        Assert.Contains(rig.Log.Snapshot(), e => e.Kind == ActivityKind.Deferred && e.TriggerName == "spot 5");

        await Tick(rig);
        Assert.Equal(new[] { "mine-2", "mine-5" }, rig.Macros.Calls);
    }

    [Fact]
    public async Task A_spot_waiting_on_busy_keeps_its_turn()
    {
        var rig = Build();
        rig.Paint.ByX[X(5)] = Orange;
        rig.Paint.ByX[X(2)] = Orange;
        rig.Macros.Response = Busy;

        await Tick(rig);                                  // spot 2 busy, spot 5 waits its turn
        await Tick(rig);                                  // spot 2 still waiting out its cooldown
        Assert.Equal(new[] { "mine-2" }, rig.Macros.Calls);

        rig.Macros.Response = Accepted;
        rig.Clock.Now = rig.Clock.Now.AddMilliseconds(100);
        await Tick(rig);                                  // spot 2 retries and fires
        await Tick(rig);                                  // then spot 5
        Assert.Equal(new[] { "mine-2", "mine-2", "mine-5" }, rig.Macros.Calls);
    }

    [Fact]
    public async Task No_layer_means_no_ore()
    {
        var rig = Build();
        rig.Paint.Default = Orange;                       // six spots orange...
        rig.Paint.ByX[X(0)] = Navy;                       // ...two navy: below minLayerSpots
        rig.Paint.ByX[X(1)] = Navy;

        await Tick(rig);

        Assert.Empty(rig.Macros.Calls);
        Assert.Equal(RingStatus.NoLayer, rig.C.Rings.Get("mine8").Status);
        Assert.Contains(rig.Log.Snapshot(), e => e.Kind == ActivityKind.NoMatch && (e.Detail ?? "").Contains("no layer"));
    }

    [Fact]
    public async Task An_ignore_colour_is_not_ore()
    {
        var rig = Build();
        rig.Paint.ByX[X(4)] = Sky;

        await Tick(rig);

        Assert.Empty(rig.Macros.Calls);
    }

    [Fact]
    public async Task A_neighbouring_layers_rock_reads_as_ore()
    {
        // Deliberate: only the current layer's rock is "rock". At a boundary the other
        // layer's rock costs one short mine, never a stall.
        var rig = Build();
        rig.Paint.Default = Grey;
        rig.Paint.ByX[X(6)] = Navy;
        rig.Paint.ByX[X(7)] = Navy;

        await Tick(rig);

        Assert.Equal("grey", rig.C.Rings.Get("mine8").Layer);
        Assert.Equal(new[] { "mine-6" }, rig.Macros.Calls);
    }

    [Fact]
    public async Task A_layer_change_is_logged_to_the_panel_and_the_file()
    {
        var rig = Build();
        await Tick(rig);
        rig.Paint.Default = Grey;
        rig.Clock.Now = rig.Clock.Now.AddSeconds(30);

        await Tick(rig);

        Assert.Equal("grey", rig.C.Rings.Get("mine8").Layer);
        Assert.Contains(rig.Log.Snapshot(), e => e.Kind == ActivityKind.LayerChanged && e.Detail == "layer grey (8 of 8 spots)");
        Assert.Contains("ring Mine #8: layer grey (8 of 8 spots)", rig.Diag);
    }

    [Fact]
    public async Task A_ring_fire_is_written_to_the_file()
    {
        var rig = Build();
        rig.Paint.ByX[X(3)] = Orange;

        await Tick(rig);

        Assert.Contains(rig.Diag, l => l.StartsWith("trigger \"spot 3\": macro mine-3"));
    }

    [Fact]
    public async Task Not_the_foreground_alt_leaves_the_ring_unknown()
    {
        var rig = Build(accountAware: true);
        rig.Paint.Default = Orange;
        rig.Fg.IsAlt = false;

        await Tick(rig);

        Assert.Empty(rig.Macros.Calls);
        Assert.Equal(RingStatus.Unknown, rig.C.Rings.Get("mine8").Status);
    }

    [Fact]
    public async Task A_spot_on_a_missing_ring_logs_one_error()
    {
        var rig = Build();
        var orphan = Spot(0, accountAware: false, ringId: "nope");
        orphan.Name = "orphan";
        rig.Store.Add(orphan);

        await Tick(rig);
        await Tick(rig);

        Assert.Single(rig.Log.Snapshot(), e => e.Kind == ActivityKind.Error && e.TriggerName == "orphan");
        Assert.Single(rig.Diag, l => l.Contains("orphan"));
    }

    [Fact]
    public async Task A_duplicate_ring_id_with_a_broken_copy_does_not_stop_other_triggers()
    {
        // Hand-edit only: UpsertRing dedupes, so this shape only comes from a hand-edited
        // triggers.json. A second "mine8" with null layers must not throw and take every
        // other trigger's tick down with it (Vote would dereference the null list).
        var path = Path.Combine(Path.GetTempPath(), "urocr-tests", Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var file = new TriggersFile();
        file.Rings.Add(Ring());
        file.Rings.Add(new RingDefinition("mine8", "Mine #8 (broken copy)", null!, MinLayerSpots: 3));
        foreach (var order in new[] { 7, 3, 0, 5, 1, 6, 2, 4 }) file.Triggers.Add(Spot(order, accountAware: false));
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(file, TriggerJsonOptions.Default));

        var store = new TriggerStore(path);
        var paint = new PaintedCapture();
        var macros = new RecordingMacros();
        var log = new ActivityLog(capacity: 1000);
        var clock = new FakeClock();
        var fg = new Fg();
        var diag = new List<string>();
        var c = new TriggerCoordinator(store, paint, new ColorMatcher(), new NoText(), fg, new NotElevated(),
            new NoKeys(), log, clock, new FakeMetrics(), macroClient: macros, diag: diag.Add);
        paint.ByX[X(3)] = Orange;

        await c.TickOnceAsync(CancellationToken.None);
        await c.TickOnceAsync(CancellationToken.None);   // must not throw, and must not repeat every tick

        Assert.Equal(new[] { "mine-3" }, macros.Calls);
        Assert.Single(log.Snapshot(), e => e.Kind == ActivityKind.Error);
    }

    [Fact]
    public void An_empty_none_of_list_writes_none_not_infinity()
    {
        // Judge with an empty combined list reports Distance = +Infinity; logs and
        // ur-ocr.log must never carry it raw.
        var j = ColorMatcher.Judge(Orange, new ColorCriteria(new Rgb(0, 0, 0), 20, ColorSamplingMode.SinglePixel,
            NoneOf: Array.Empty<Rgb>()));
        Assert.True(double.IsPositiveInfinity(j.Distance));

        var detail = TriggerCoordinator.ColorDetail(j);

        Assert.Contains("d=none", detail);
        Assert.DoesNotContain("∞", detail);
        Assert.DoesNotContain("Infinity", detail);
    }
}
