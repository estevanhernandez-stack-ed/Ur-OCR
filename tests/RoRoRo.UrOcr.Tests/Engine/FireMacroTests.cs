using System.Drawing;
using System.IO;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

public class FireMacroTests
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

    private sealed class FakeMacroClient : IMacroRunClient
    {
        public List<(string MacroId, IReadOnlyList<string>? Targets)> Calls { get; } = new();
        public RunMacroResponse Response { get; set; } = new(Ok: true, PlaybackId: "pbid", Queued: false, Reason: null, Detail: null);
        public Task<RunMacroResponse> RunAsync(string macroId, IReadOnlyList<string>? targets, CancellationToken ct)
        {
            Calls.Add((macroId, targets));
            return Task.FromResult(Response);
        }
    }

    private (TriggerCoordinator, FakeColor, FakeKeys, ActivityLog, TriggerStore, FakeMacroClient) Make(bool dryRun = false)
    {
        var path = Path.Combine(Path.GetTempPath(), $"fm-{Guid.NewGuid()}.json");
        var store = new TriggerStore(path);
        var clock = new FakeClock();
        var color = new FakeColor();
        var text = new FakeText();
        var fg = new FakeFg();
        var elev = new FakeElev();
        var keys = new FakeKeys();
        var log = new ActivityLog();
        var macroClient = new FakeMacroClient();
        var c = new TriggerCoordinator(store, new FakeCapture(), color, text, fg, elev, keys, log, clock, new FakeMetrics(),
            onFirstFire: null, macroClient: macroClient);
        c.DryRun = dryRun;
        return (c, color, keys, log, store, macroClient);
    }

    private static Trigger RunMacroTrigger(string macroId = "macro-abc") => new()
    {
        Id = Guid.NewGuid(),
        Name = "MacroTrigger",
        Region = new RegionRect(0, 0, 10, 10),
        Mode = TriggerMode.Color,
        Color = new ColorCriteria(new Rgb(0, 0, 0), 5, ColorSamplingMode.SinglePixel),
        Keybind = new KeyCombo("A", Array.Empty<string>()),
        CooldownMs = 100,
        AccountAware = false,
        Action = TriggerAction.RunMacro,
        MacroId = macroId,
    };

    private static Trigger KeyChordTrigger() => new()
    {
        Id = Guid.NewGuid(),
        Name = "KeyChordTrigger",
        Region = new RegionRect(0, 0, 10, 10),
        Mode = TriggerMode.Color,
        Color = new ColorCriteria(new Rgb(0, 0, 0), 5, ColorSamplingMode.SinglePixel),
        Keybind = new KeyCombo("A", Array.Empty<string>()),
        CooldownMs = 100,
        AccountAware = false,
        Action = TriggerAction.KeyChord,
    };

    [Fact]
    public async Task RunMacro_trigger_calls_macroClient_not_keys_and_logs_Fired()
    {
        var (c, color, keys, log, store, macroClient) = Make(dryRun: false);
        var trig = RunMacroTrigger("macro-abc");
        store.Add(trig);
        color.Result = true;

        await c.TickOnceAsync(CancellationToken.None);

        Assert.Single(macroClient.Calls);
        Assert.Equal("macro-abc", macroClient.Calls[0].MacroId);
        Assert.Equal(0, keys.Pressed);
        Assert.Contains(log.Snapshot(), e => e.Kind == ActivityKind.Fired);
    }

    [Fact]
    public async Task KeyChord_trigger_calls_keys_not_macroClient()
    {
        var (c, color, keys, log, store, macroClient) = Make(dryRun: false);
        store.Add(KeyChordTrigger());
        color.Result = true;

        await c.TickOnceAsync(CancellationToken.None);

        Assert.Equal(1, keys.Pressed);
        Assert.Empty(macroClient.Calls);
    }

    [Fact]
    public async Task DryRun_RunMacro_trigger_calls_neither_client_nor_keys_and_logs_WouldFire()
    {
        var (c, color, keys, log, store, macroClient) = Make(dryRun: true);
        store.Add(RunMacroTrigger("macro-xyz"));
        color.Result = true;

        await c.TickOnceAsync(CancellationToken.None);

        Assert.Empty(macroClient.Calls);
        Assert.Equal(0, keys.Pressed);
        Assert.Contains(log.Snapshot(), e => e.Kind == ActivityKind.WouldFire);
    }

    [Fact]
    public async Task RefusedMacro_response_logs_Fired_with_refused_detail()
    {
        var (c, color, keys, log, store, macroClient) = Make(dryRun: false);
        macroClient.Response = new RunMacroResponse(false, null, false, "unknown-macro", "No macro with id 'macro-refused'.");
        var trig = RunMacroTrigger("macro-refused");
        store.Add(trig);
        color.Result = true;

        await c.TickOnceAsync(CancellationToken.None);

        Assert.Single(macroClient.Calls);
        Assert.Equal(0, keys.Pressed);
        var entry = Assert.Single(log.Snapshot(), e => e.Kind == ActivityKind.Fired);
        Assert.Contains("refused", entry.Detail, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly RunMacroResponse BusyResponse = new(false, null, false, "busy", "A sequence is already running.");
    private static readonly RunMacroResponse AcceptedResponse = new(true, "pb-1", false, null, null);

    private (TriggerCoordinator C, FakeColor Color, TriggerStore Store, FakeMacroClient Macros, FakeClock Clock, ActivityLog Log) MakeTimed()
    {
        var store = new TriggerStore(Path.Combine(Path.GetTempPath(), $"fm-{Guid.NewGuid()}.json"));
        var clock = new FakeClock { Now = DateTimeOffset.UnixEpoch };
        var color = new FakeColor();
        var log = new ActivityLog();
        var macros = new FakeMacroClient();
        var c = new TriggerCoordinator(store, new FakeCapture(), color, new FakeText(), new FakeFg(), new FakeElev(),
            new FakeKeys(), log, clock, new FakeMetrics(), onFirstFire: null, macroClient: macros);
        return (c, color, store, macros, clock, log);
    }

    [Fact]
    public async Task Busy_refusal_keeps_the_trigger_armed_and_retries_after_cooldown()
    {
        var (c, color, store, macros, clock, log) = MakeTimed();
        store.Add(RunMacroTrigger("mine-e"));   // CooldownMs = 100
        color.Result = true;
        macros.Response = BusyResponse;

        await c.TickOnceAsync(CancellationToken.None);
        Assert.Single(macros.Calls);
        Assert.Contains(log.Snapshot(), e => e.Kind == ActivityKind.Busy);

        clock.Now = clock.Now.AddMilliseconds(50);
        await c.TickOnceAsync(CancellationToken.None);
        Assert.Single(macros.Calls);                     // waiting out the cooldown

        macros.Response = AcceptedResponse;
        clock.Now = clock.Now.AddMilliseconds(60);       // 110 ms after the refusal
        await c.TickOnceAsync(CancellationToken.None);
        Assert.Equal(2, macros.Calls.Count);
        Assert.Contains(log.Snapshot(), e => e.Kind == ActivityKind.Fired);

        clock.Now = clock.Now.AddMilliseconds(500);
        await c.TickOnceAsync(CancellationToken.None);
        Assert.Equal(2, macros.Calls.Count);             // accepted: the edge is spent
    }

    [Fact]
    public async Task Busy_refusal_is_not_counted_as_a_fire()
    {
        var (c, color, store, macros, _, _) = MakeTimed();
        store.Add(RunMacroTrigger());
        color.Result = true;
        macros.Response = BusyResponse;

        await c.TickOnceAsync(CancellationToken.None);

        var t = Assert.Single(store.All);
        Assert.Equal(0, t.HitCount);
        Assert.Null(t.LastFiredAt);
    }

    [Fact]
    public async Task Busy_retry_lapses_when_the_spot_stops_matching()
    {
        var (c, color, store, macros, clock, _) = MakeTimed();
        store.Add(RunMacroTrigger());
        color.Result = true;
        macros.Response = BusyResponse;
        await c.TickOnceAsync(CancellationToken.None);

        color.Result = false;
        clock.Now = clock.Now.AddMilliseconds(200);
        await c.TickOnceAsync(CancellationToken.None);
        Assert.Single(macros.Calls);                     // no retry while it does not match

        macros.Response = AcceptedResponse;
        color.Result = true;
        await c.TickOnceAsync(CancellationToken.None);
        Assert.Equal(2, macros.Calls.Count);             // a fresh edge fires at once
    }

    [Fact]
    public async Task Busy_retry_fires_when_due_even_if_the_last_fire_cooldown_is_not()
    {
        var (c, color, store, macros, clock, log) = MakeTimed();
        var trig = RunMacroTrigger("mine-e");   // CooldownMs = 100
        store.Add(trig);
        color.Result = true;

        await c.TickOnceAsync(CancellationToken.None);   // a real fire: LastFiredAt set
        Assert.Single(macros.Calls);

        color.Result = false;
        clock.Now = clock.Now.AddMilliseconds(150);
        await c.TickOnceAsync(CancellationToken.None);   // edge ends

        color.Result = true;
        macros.Response = BusyResponse;
        await c.TickOnceAsync(CancellationToken.None);   // new edge, Ur Task busy: retry at +100
        Assert.Equal(2, macros.Calls.Count);

        // The cooldown grows while the retry waits (an edit), so the last-fire
        // cooldown is not ready when the retry falls due.
        trig.CooldownMs = 1000;
        macros.Response = AcceptedResponse;
        clock.Now = clock.Now.AddMilliseconds(110);
        await c.TickOnceAsync(CancellationToken.None);

        Assert.Equal(3, macros.Calls.Count);             // the busy retry fires, not a cooldown skip
        Assert.Equal(2, log.Snapshot().Count(e => e.Kind == ActivityKind.Fired));
    }

    [Fact]
    public async Task Other_refusals_spend_the_edge()
    {
        var (c, color, store, macros, clock, _) = MakeTimed();
        store.Add(RunMacroTrigger());
        color.Result = true;
        macros.Response = new RunMacroResponse(false, null, false, "ur-task-not-running", "Ur Task is not running.");

        await c.TickOnceAsync(CancellationToken.None);
        clock.Now = clock.Now.AddMilliseconds(500);
        await c.TickOnceAsync(CancellationToken.None);

        Assert.Single(macros.Calls);
    }
}
