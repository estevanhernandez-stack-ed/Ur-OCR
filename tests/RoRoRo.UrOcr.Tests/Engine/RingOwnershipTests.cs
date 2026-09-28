using System.Drawing;
using System.IO;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

/// <summary>The 0.5.0 ring triggers stand down for an account whose pulse owns the ring.</summary>
public class RingOwnershipTests
{
    private sealed class Clock : IClock { public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch; }
    private sealed class PaintedCapture : ICaptureSource
    {
        public Dictionary<int, Rgb> ByX { get; } = new();
        public Bitmap Capture(RegionRect r)
        {
            var c = ByX.TryGetValue(r.X, out var v) ? v : PulseFixtures.Grey;
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
    private sealed class Fg : IForegroundCheck { public bool IsForegroundAnAlt() => true; public int GetForegroundPid() => 1; }
    private sealed class NotElevated : IElevationCheck { public bool IsForegroundProcessLikelyElevated(int pid) => false; }
    private sealed class NoKeys : IKeyPress { public void Press(KeyCombo c) { } }
    private sealed class Metrics : IWindowMetrics
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

    private static (TriggerCoordinator C, RecordingMacros Macros) Build(Func<int, string, bool> owner, bool accountAware = true)
    {
        var store = new TriggerStore(Path.Combine(Path.GetTempPath(), "urocr-tests", Guid.NewGuid().ToString("N") + ".json"));
        store.UpsertRing(PulseFixtures.Ring());
        foreach (var spot in PulseFixtures.Spots())
        {
            spot.AccountAware = accountAware;
            store.Upsert(spot);
        }
        var paint = new PaintedCapture();
        paint.ByX[100 + 3 * 20] = PulseFixtures.Orange;   // spot 3 (SE) shows ore
        var macros = new RecordingMacros();
        var c = new TriggerCoordinator(store, paint, new ColorMatcher(), new NoText(), new Fg(), new NotElevated(),
            new NoKeys(), new ActivityLog(capacity: 1000), new Clock(), new Metrics(),
            macroClient: macros, ringOwner: owner);
        return (c, macros);
    }

    [Fact]
    public async Task A_ring_owned_by_a_pulse_loop_does_not_fire()
    {
        var (c, macros) = Build((pid, ring) => pid == 1 && ring == "mine8");

        await c.TickOnceAsync(CancellationToken.None);

        Assert.Empty(macros.Calls);
    }

    [Fact]
    public async Task The_same_ring_fires_for_an_account_without_a_pulse()
    {
        var (c, macros) = Build((pid, _) => pid == 2);

        await c.TickOnceAsync(CancellationToken.None);

        Assert.Equal(new[] { "mine-3" }, macros.Calls);
    }

    [Fact]
    public async Task A_trigger_without_the_account_gate_is_never_owned()
    {
        var (c, macros) = Build((_, _) => true, accountAware: false);

        await c.TickOnceAsync(CancellationToken.None);

        Assert.Equal(new[] { "mine-3" }, macros.Calls);
    }
}
