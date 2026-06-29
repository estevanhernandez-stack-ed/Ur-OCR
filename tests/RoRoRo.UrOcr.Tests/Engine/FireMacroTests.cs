using System.Drawing;
using System.IO;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Ipc;
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
        var c = new TriggerCoordinator(store, new FakeCapture(), color, text, fg, elev, keys, log, clock,
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
}
