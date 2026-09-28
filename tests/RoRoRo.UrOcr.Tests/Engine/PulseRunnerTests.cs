using System.IO;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

public class PulseRunnerTests
{
    private sealed class Front : IForegroundCheck
    {
        public bool IsAlt = true;
        public int Pid = 100;
        /// <summary>Pids handed out first, one per read, before falling back to Pid.</summary>
        public Queue<int> Next { get; } = new();
        public bool IsForegroundAnAlt() => IsAlt;
        public int GetForegroundPid() => Next.Count > 0 ? Next.Dequeue() : Pid;
    }
    private sealed class Elevation : IElevationCheck
    {
        public bool Elevated;
        public bool IsForegroundProcessLikelyElevated(int pid) => Elevated;
    }
    private sealed class Accounts : IAccountLookup
    {
        public Dictionary<int, long> Map { get; } = new() { [100] = 42, [200] = 43 };
        public bool TryGetUserId(int pid, out long userId) => Map.TryGetValue(pid, out userId);
    }
    private sealed class NoRing : ISpotReader
    {
        public IReadOnlyList<SpotReading>? Read(int pid, IReadOnlyList<Trigger> spots) => null;
    }

    private sealed record Rig(PulseRunner Runner, TriggerStore Store, ScriptedMacros Macros, Front Front,
        Elevation Elevation, ActivityLog Log, List<string> Diag, PulseClock Clock);

    private static Rig Build(params PulseConfig[] pulses) => BuildWith(new NoRing(), pulses);

    private static Rig BuildWith(ISpotReader reader, params PulseConfig[] pulses)
    {
        var store = new TriggerStore(Path.Combine(Path.GetTempPath(), "urocr-tests", Guid.NewGuid().ToString("N") + ".json"));
        store.UpsertRing(PulseFixtures.Ring());
        foreach (var spot in PulseFixtures.Spots()) store.Upsert(spot);
        foreach (var p in pulses.Length == 0 ? new[] { PulseFixtures.Config(account: 42), PulseFixtures.Config(account: 43) } : pulses)
            store.UpsertPulse(p);
        var macros = new ScriptedMacros();
        var front = new Front();
        var elevation = new Elevation();
        var log = new ActivityLog(capacity: 1000);
        var diag = new List<string>();
        var clock = new PulseClock();
        var runner = new PulseRunner(store, reader, macros, front, elevation, new Accounts(),
            clock, log, diag.Add);
        return new Rig(runner, store, macros, front, elevation, log, diag, clock);
    }

    private static Task Tick(Rig r) => r.Runner.TickOnceAsync(CancellationToken.None);

    [Fact]
    public async Task Only_the_account_in_front_acts()
    {
        var rig = Build();

        await Tick(rig);
        Assert.Equal(new[] { "42" }, Assert.Single(rig.Macros.Runs).Targets);

        rig.Front.Pid = 200;
        await Tick(rig);

        Assert.Equal(2, rig.Macros.Runs.Count);
        Assert.Equal(new[] { "43" }, rig.Macros.Runs[1].Targets);
        Assert.Single(rig.Macros.Polls);   // 42 still followed its playback from behind
    }

    [Fact]
    public async Task The_front_is_checked_again_right_before_a_macro_starts()
    {
        var rig = Build(PulseFixtures.Config(account: 42));
        rig.Front.Next.Enqueue(100);      // the tick starts with 42 in front
        rig.Front.Pid = 300;              // then Este tabs to a window that is no RoRoRo account

        await Tick(rig);

        Assert.Empty(rig.Macros.Runs);
        Assert.Equal(PulseState.Riding, rig.Runner.LoopFor(42)!.State);
    }

    [Fact]
    public async Task A_window_that_is_not_an_alt_moves_nobody()
    {
        var rig = Build();
        rig.Front.IsAlt = false;

        await Tick(rig);

        Assert.Empty(rig.Macros.Runs);
    }

    [Fact]
    public async Task An_elevated_foreground_moves_nobody()
    {
        var rig = Build();
        rig.Elevation.Elevated = true;

        await Tick(rig);

        Assert.Empty(rig.Macros.Runs);
    }

    [Fact]
    public async Task Hold_stops_everything()
    {
        var rig = Build();
        rig.Runner.Hold = () => true;

        await Tick(rig);

        Assert.Empty(rig.Macros.Runs);
        Assert.Null(rig.Runner.LoopFor(42));
    }

    [Fact]
    public void Owns_the_ring_only_for_an_account_with_an_enabled_pulse()
    {
        var rig = Build(PulseFixtures.Config(account: 42), PulseFixtures.Config(account: 43) with { Enabled = false });

        Assert.True(rig.Runner.OwnsRing(100, "mine8"));
        Assert.True(rig.Runner.OwnsRing(100, "MINE8"));
        Assert.False(rig.Runner.OwnsRing(100, "mine9"));
        Assert.False(rig.Runner.OwnsRing(200, "mine8"));   // pulse turned off
        Assert.False(rig.Runner.OwnsRing(300, "mine8"));   // not a RoRoRo account
        Assert.False(rig.Runner.OwnsRing(0, "mine8"));     // no foreground alt
    }

    [Fact]
    public async Task A_pulse_on_a_missing_ring_logs_why_it_stopped()
    {
        var rig = Build(PulseFixtures.Config(account: 42) with { RingId = "mine9" });

        await Tick(rig);

        Assert.Equal(PulseState.Stopped, rig.Runner.LoopFor(42)!.State);
        Assert.Empty(rig.Macros.Runs);
        Assert.Contains(rig.Log.Snapshot(), e => e.Kind == ActivityKind.Pulse && e.TriggerName == "(pulse 42)"
                                                && e.Detail!.Contains("Ring mine9 is not defined"));
        Assert.Contains(rig.Diag, l => l.StartsWith("pulse 42: stopped: Ring mine9 is not defined"));
    }

    [Fact]
    public async Task A_disabled_pulse_gets_no_loop()
    {
        var rig = Build(PulseFixtures.Config(account: 42) with { Enabled = false });

        await Tick(rig);

        Assert.Null(rig.Runner.LoopFor(42));
        Assert.Empty(rig.Macros.Runs);
    }

    // Controller rulings: a hold never acts, but a loop already in flight keeps being polled, and
    // time held or behind does not count toward the rock cap (NoteBehind), the same as
    // PulseLoopTests.Time_behind_...

    [Fact]
    public async Task A_hold_never_acts_but_keeps_polling_a_loop_it_already_built()
    {
        var rig = Build(PulseFixtures.Config(account: 42));
        await Tick(rig);                                  // Auto Mine on started as pb1
        Assert.Single(rig.Macros.Runs);

        rig.Runner.Hold = () => true;
        await Tick(rig);                                  // held: polls pb1 (unscripted: finishes)
        await Tick(rig);                                  // held: nothing left in flight to poll

        Assert.Single(rig.Macros.Runs);                   // never acts: no new macro started
        Assert.Single(rig.Macros.Polls);                  // but the in-flight one was still followed
        Assert.Equal(PulseState.Riding, rig.Runner.LoopFor(42)!.State);   // no Act: never left Riding
    }

    [Fact]
    public async Task F9_is_re_checked_right_before_a_macro_starts()
    {
        var rig = Build(PulseFixtures.Config(account: 42));
        var calls = 0;
        // Hold's top-of-tick read (once) says "not held"; by the time the loop is about to
        // start its macro, F9 has been pressed. Every call from there on says "held".
        rig.Runner.Hold = () => calls++ > 0;

        await Tick(rig);

        Assert.Empty(rig.Macros.Runs);                                  // F9 caught it before RunMacro
        Assert.Equal(PulseState.Riding, rig.Runner.LoopFor(42)!.State);  // never got past Begin()
    }

    /// <summary>Through the runner: the first calm read on grey (the aim layer), a clear pass that
    /// skips everything, and Auto Mine on for the burst, finished; the rock cap clock started at 3 s.</summary>
    private static async Task<Rig> OnTheAimLayerWithNothingCleared()
    {
        var rig = BuildWith(new ScriptedReader { Next = ScriptedReader.All(PulseFixtures.Grey) },
            PulseFixtures.Config(account: 42));
        foreach (var n in MeasuredRing.RingOrder) rig.Macros.Script($"id-clear-{n}", ScriptedMacros.Skipped);
        await Tick(rig);                                  // Auto Mine on
        await Tick(rig);                                  // ride timer
        rig.Clock.Advance(2000);
        await Tick(rig);                                  // Auto Mine off
        await Tick(rig);                                  // settle timer
        rig.Clock.Advance(1000);
        await Tick(rig);                                  // t = 3 s: read grey, clearing
        for (var i = 0; i < 8; i++) await Tick(rig);      // pass skipped everything: Auto Mine on
        await Tick(rig);                                  // on finished: ride timer
        return rig;
    }

    private static async Task ReadAgain(Rig rig)
    {
        await Tick(rig);                                  // ride over: Auto Mine off
        await Tick(rig);                                  // off finished: settle
        rig.Clock.Advance(1000);
        await Tick(rig);                                  // read at t = 304 s
    }

    [Fact]
    public async Task Time_held_does_not_count_toward_the_rock_cap()
    {
        var rig = await OnTheAimLayerWithNothingCleared();

        rig.Runner.Hold = () => true;
        await Tick(rig);                                  // held from t = 3 s
        rig.Clock.Advance(300_000);
        rig.Runner.Hold = () => false;
        await ReadAgain(rig);

        Assert.Equal(PulseState.Clearing, rig.Runner.LoopFor(42)!.State);
        Assert.DoesNotContain("id-top", rig.Macros.RunIds);
    }

    [Fact]
    public async Task Time_behind_through_the_runner_does_not_count_toward_the_rock_cap()
    {
        var rig = await OnTheAimLayerWithNothingCleared();

        rig.Front.Pid = 300;                              // a window that is no RoRoRo account
        await Tick(rig);                                  // behind from t = 3 s
        rig.Clock.Advance(300_000);
        rig.Front.Pid = 100;
        await ReadAgain(rig);

        Assert.Equal(PulseState.Clearing, rig.Runner.LoopFor(42)!.State);
        Assert.DoesNotContain("id-top", rig.Macros.RunIds);
    }

    [Fact]
    public async Task Without_the_hold_the_same_time_trips_the_rock_cap()
    {
        var rig = await OnTheAimLayerWithNothingCleared();

        rig.Clock.Advance(300_000);
        await ReadAgain(rig);

        Assert.Equal(PulseState.GoingToTop, rig.Runner.LoopFor(42)!.State);
        Assert.Equal("id-top", rig.Macros.RunIds.Last());
    }
}
