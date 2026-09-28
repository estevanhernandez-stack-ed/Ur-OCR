using System.Drawing;
using System.IO;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

/// <summary>One trigger's failed read does not stop the tick, and every trigger sees one foreground decision per tick.</summary>
public class TickIsolationTests
{
    private sealed class FakeClock : IClock { public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch; }
    private sealed class FakeCapture : ICaptureSource { public Bitmap Capture(RegionRect r) => new(r.Width, r.Height); }
    private sealed class MatchingColor : IColorMatchEngine
    {
        public bool Matches(Bitmap b, ColorCriteria c) => true;
        public ColorMatchResult Evaluate(Bitmap b, ColorCriteria c) => new(new Rgb(0, 0, 0), 0, true);
    }
    private sealed class ThrowingText : ITextMatchEngine
    {
        public Task<(bool matched, string text)> RunAsync(Bitmap b, TextCriteria c) => throw new InvalidOperationException("OCR engine gone");
        public Task<(bool matched, string text)> RunWithPreprocessAsync(Bitmap b, TextCriteria c) => throw new InvalidOperationException("OCR engine gone");
    }
    /// <summary>The foreground is an alt on the first ask of each tick only; a later ask in the same tick says no.</summary>
    private sealed class FlakyFg : IForegroundCheck
    {
        public int Asks;
        public bool IsForegroundAnAlt() => Asks++ == 0;
        public int GetForegroundPid() => 1;
    }
    private sealed class AltFg : IForegroundCheck { public bool IsForegroundAnAlt() => true; public int GetForegroundPid() => 1; }
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

    private static Trigger ColorTrigger(string name, bool accountAware) => new()
    {
        Id = Guid.NewGuid(), Name = name,
        Region = new RegionRect(0, 0, 10, 10), Mode = TriggerMode.Color,
        Color = new ColorCriteria(new Rgb(0, 0, 0), 5, ColorSamplingMode.SinglePixel),
        Keybind = new KeyCombo("F13", Array.Empty<string>()),
        AccountAware = accountAware, CooldownMs = 100,
        Action = TriggerAction.RunMacro, MacroId = name,
    };

    private static Trigger TextTrigger(string name) => new()
    {
        Id = Guid.NewGuid(), Name = name,
        Region = new RegionRect(0, 0, 10, 10), Mode = TriggerMode.Text,
        Text = new TextCriteria("ore", false, TextMatchType.Contains),
        Keybind = new KeyCombo("F13", Array.Empty<string>()),
        AccountAware = false, CooldownMs = 100,
        Action = TriggerAction.RunMacro, MacroId = name,
    };

    private static TriggerStore Store(params Trigger[] triggers)
    {
        var store = new TriggerStore(Path.Combine(Path.GetTempPath(), "urocr-tests", Guid.NewGuid().ToString("N") + ".json"));
        foreach (var t in triggers) store.Add(t);
        return store;
    }

    [Fact]
    public async Task A_trigger_whose_read_throws_does_not_stop_the_others()
    {
        var store = Store(TextTrigger("ocr"), ColorTrigger("colour", accountAware: false));
        var macros = new RecordingMacros();
        var log = new ActivityLog();
        var clock = new FakeClock();
        var c = new TriggerCoordinator(store, new FakeCapture(), new MatchingColor(), new ThrowingText(), new AltFg(),
            new NotElevated(), new NoKeys(), log, clock, new FakeMetrics(), macroClient: macros);

        await c.TickOnceAsync(CancellationToken.None);
        clock.Now = clock.Now.AddMilliseconds(500);
        await c.TickOnceAsync(CancellationToken.None);

        Assert.Equal(new[] { "colour" }, macros.Calls);
        // Logged once, not every tick.
        Assert.Single(log.Snapshot(), e => e.Kind == ActivityKind.Error && e.TriggerName == "ocr");
    }

    [Fact]
    public async Task Every_trigger_sees_the_same_foreground_decision_within_a_tick()
    {
        var store = Store(ColorTrigger("a", accountAware: true), ColorTrigger("b", accountAware: true));
        var macros = new RecordingMacros();
        var fg = new FlakyFg();
        var c = new TriggerCoordinator(store, new FakeCapture(), new MatchingColor(), new ThrowingText(), fg,
            new NotElevated(), new NoKeys(), new ActivityLog(), new FakeClock(), new FakeMetrics(), macroClient: macros);

        await c.TickOnceAsync(CancellationToken.None);

        Assert.Equal(new[] { "a", "b" }, macros.Calls);
        Assert.Equal(1, fg.Asks);
    }
}
