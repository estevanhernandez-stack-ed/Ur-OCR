using System.Drawing;
using System.IO;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

public class DwellTests
{
    private sealed class FakeClock : IClock { public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch; }
    private sealed class FakeCapture : ICaptureSource { public Bitmap Capture(RegionRect r) => new(r.Width, r.Height); }
    private sealed class FakeColor : IColorMatchEngine
    {
        public bool Result;
        public bool Matches(Bitmap b, ColorCriteria c) => Result;
        public ColorMatchResult Evaluate(Bitmap b, ColorCriteria c) => new(new Rgb(0, 0, 0), 0, Result);
    }
    private sealed class NoText : ITextMatchEngine
    {
        public Task<(bool matched, string text)> RunAsync(Bitmap b, TextCriteria c) => Task.FromResult((false, ""));
        public Task<(bool matched, string text)> RunWithPreprocessAsync(Bitmap b, TextCriteria c) => Task.FromResult((false, ""));
    }
    private sealed class Fg : IForegroundCheck { public bool IsForegroundAnAlt() => true; public int GetForegroundPid() => 1; }
    private sealed class NotElevated : IElevationCheck { public bool IsForegroundProcessLikelyElevated(int pid) => false; }
    private sealed class NoKeys : IKeyPress { public void Press(KeyCombo c) { } }
    private sealed class FakeMetrics : IWindowMetrics
    {
        public IntPtr HwndForPid(int pid) => new(0x10);
        public (int X, int Y)? ClientOrigin(IntPtr h) => (0, 0);
        public (int W, int H)? ClientSize(IntPtr h) => (800, 600);
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

    private static Trigger Held(int holdMs) => new()
    {
        Id = Guid.NewGuid(), Name = "held",
        Region = new RegionRect(0, 0, 10, 10), Mode = TriggerMode.Color,
        Color = new ColorCriteria(new Rgb(0, 0, 0), 5, ColorSamplingMode.SinglePixel),
        Keybind = new KeyCombo("F13", Array.Empty<string>()),
        AccountAware = false, CooldownMs = 100, HoldForMs = holdMs,
        Action = TriggerAction.RunMacro, MacroId = "held-macro",
    };

    private static (TriggerCoordinator C, FakeColor Color, FakeClock Clock, RecordingMacros Macros, ActivityLog Log) Make(Trigger t)
    {
        var store = new TriggerStore(Path.Combine(Path.GetTempPath(), "urocr-tests", Guid.NewGuid().ToString("N") + ".json"));
        store.Add(t);
        var color = new FakeColor();
        var clock = new FakeClock();
        var macros = new RecordingMacros();
        var log = new ActivityLog();
        var c = new TriggerCoordinator(store, new FakeCapture(), color, new NoText(), new Fg(), new NotElevated(),
            new NoKeys(), log, clock, new FakeMetrics(), macroClient: macros);
        return (c, color, clock, macros, log);
    }

    private static Task Tick(TriggerCoordinator c) => c.TickOnceAsync(CancellationToken.None);

    [Fact]
    public async Task A_held_match_fires_only_after_the_hold()
    {
        var (c, color, clock, macros, log) = Make(Held(1000));
        color.Result = true;

        await Tick(c);
        clock.Now = clock.Now.AddMilliseconds(500);
        await Tick(c);
        Assert.Empty(macros.Calls);
        Assert.Contains(log.Snapshot(), e => e.Kind == ActivityKind.Holding);

        clock.Now = clock.Now.AddMilliseconds(500);
        await Tick(c);
        Assert.Single(macros.Calls);
    }

    [Fact]
    public async Task A_break_in_the_match_restarts_the_hold()
    {
        var (c, color, clock, macros, _) = Make(Held(1000));
        color.Result = true;
        await Tick(c);                                            // t = 0

        color.Result = false;
        clock.Now = clock.Now.AddMilliseconds(600);
        await Tick(c);                                            // t = 600, broken

        color.Result = true;
        clock.Now = clock.Now.AddMilliseconds(100);
        await Tick(c);                                            // t = 700, hold starts again
        clock.Now = clock.Now.AddMilliseconds(900);
        await Tick(c);                                            // t = 1600, held 900
        Assert.Empty(macros.Calls);

        clock.Now = clock.Now.AddMilliseconds(100);
        await Tick(c);                                            // t = 1700, held 1000
        Assert.Single(macros.Calls);
    }

    [Fact]
    public async Task A_held_trigger_fires_again_after_another_full_hold()
    {
        var (c, color, clock, macros, _) = Make(Held(1000));
        color.Result = true;
        await Tick(c);
        clock.Now = clock.Now.AddMilliseconds(1000);
        await Tick(c);                                            // fires at 1000
        clock.Now = clock.Now.AddMilliseconds(500);
        await Tick(c);                                            // 1500: new hold at 500
        Assert.Single(macros.Calls);

        clock.Now = clock.Now.AddMilliseconds(500);
        await Tick(c);                                            // 2000: new hold complete
        Assert.Equal(2, macros.Calls.Count);
    }

    [Fact]
    public async Task Hold_zero_still_fires_on_the_edge()
    {
        var (c, color, _, macros, _) = Make(Held(0));
        color.Result = true;

        await Tick(c);

        Assert.Single(macros.Calls);
    }

    [Fact]
    public async Task A_hold_that_completes_inside_the_cooldown_fires_when_the_cooldown_ends()
    {
        var t = Held(50);
        t.CooldownMs = 100;                                       // the hold is shorter than the cooldown
        var (c, color, clock, macros, _) = Make(t);
        color.Result = true;
        await Tick(c);                                            // t = 0: hold starts
        clock.Now = clock.Now.AddMilliseconds(50);
        await Tick(c);                                            // t = 50: fires, fresh hold from 50
        Assert.Single(macros.Calls);

        clock.Now = clock.Now.AddMilliseconds(50);
        await Tick(c);                                            // t = 100: held again, 50 ms of cooldown left
        Assert.Single(macros.Calls);

        clock.Now = clock.Now.AddMilliseconds(50);
        await Tick(c);                                            // t = 150: cooldown over, still armed
        Assert.Equal(2, macros.Calls.Count);
    }
}
