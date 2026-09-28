using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

/// <summary>
/// The pulse state machine against a scripted Ur Task, a scripted ring and a fake clock. Every
/// unscripted macro finishes on its first poll, so one tick starts a macro and the next sees it end.
/// </summary>
public class PulseLoopTests
{
    private sealed record Rig(PulseLoop Loop, ScriptedMacros Macros, ScriptedReader Reader, PulseClock Clock, List<string> Log);

    private static Rig Build(PulseConfig? config = null, Rgb? everywhere = null)
    {
        var macros = new ScriptedMacros();
        var reader = new ScriptedReader { Next = ScriptedReader.All(everywhere ?? PulseFixtures.Grey) };
        var clock = new PulseClock();
        var log = new List<string>();
        var loop = new PulseLoop(config ?? PulseFixtures.Config(), new[] { PulseFixtures.Ring() },
            PulseFixtures.Spots(), reader, macros, clock, log.Add);
        return new Rig(loop, macros, reader, clock, log);
    }

    private static Task Tick(Rig r, bool front = true) => r.Loop.TickAsync(front, 7, CancellationToken.None);

    /// <summary>Auto Mine on, ride 2 s, Auto Mine off, settle 1 s, read: the first calm read.</summary>
    private static async Task FirstRead(Rig r)
    {
        await Tick(r);            // start Auto Mine on
        await Tick(r);            // it finished: ride timer starts
        r.Clock.Advance(2000);
        await Tick(r);            // ride over: start Auto Mine off
        await Tick(r);            // it finished: settle timer starts
        r.Clock.Advance(1000);
        await Tick(r);            // settled: read, then act on the read
    }

    private static void SkipAllClears(Rig r)
    {
        foreach (var n in MeasuredRing.RingOrder) r.Macros.Script($"id-clear-{n}", ScriptedMacros.Skipped);
    }

    private static async Task Ticks(Rig r, int n)
    {
        for (var i = 0; i < n; i++) await Tick(r);
    }

    [Fact]
    public async Task Starts_by_turning_Auto_Mine_on_for_its_account()
    {
        var rig = Build();

        await Tick(rig);

        var run = Assert.Single(rig.Macros.Runs);
        Assert.Equal("id-on", run.MacroId);
        Assert.Equal(new[] { "42" }, run.Targets);
        Assert.Equal(PulseState.Riding, rig.Loop.State);
        Assert.Contains(rig.Log, l => l.StartsWith("started"));
    }

    [Fact]
    public async Task Rides_burstMs_then_pauses_and_settles_before_reading()
    {
        var rig = Build();
        await Tick(rig);
        await Tick(rig);

        rig.Clock.Advance(1999);
        await Tick(rig);
        Assert.Equal(new[] { "id-on" }, rig.Macros.RunIds);

        rig.Clock.Advance(1);
        await Tick(rig);
        Assert.Equal(new[] { "id-on", "id-off" }, rig.Macros.RunIds);
        Assert.Equal(PulseState.Pausing, rig.Loop.State);

        await Tick(rig);
        rig.Clock.Advance(999);
        await Tick(rig);
        Assert.Equal(0, rig.Reader.Reads);

        rig.Clock.Advance(1);
        await Tick(rig);
        Assert.Equal(1, rig.Reader.Reads);
    }

    [Fact]
    public async Task Above_the_target_rides_on()
    {
        var rig = Build(everywhere: PulseFixtures.Navy);

        await FirstRead(rig);

        Assert.Equal(PulseState.Riding, rig.Loop.State);
        Assert.Equal(new[] { "id-on", "id-off", "id-on" }, rig.Macros.RunIds);
        Assert.Equal("navy", rig.Loop.Layer);
        Assert.Contains(rig.Log, l => l.Contains("above the target"));
    }

    [Fact]
    public async Task At_the_target_clears_ore_first_then_the_rest_in_ring_order()
    {
        var rig = Build();
        rig.Reader.Next = ScriptedReader.All(PulseFixtures.Grey, (5, PulseFixtures.Orange), (2, PulseFixtures.Sky));

        await FirstRead(rig);
        Assert.Equal(PulseState.Clearing, rig.Loop.State);
        await Ticks(rig, 7);

        Assert.Equal(
            new[] { "id-clear-SW", "id-clear-N", "id-clear-NE", "id-clear-E", "id-clear-SE", "id-clear-S", "id-clear-W", "id-clear-NW" },
            rig.Macros.RunIds.Skip(2));
    }

    [Fact]
    public async Task Below_the_target_goes_to_top_then_rides()
    {
        var rig = Build(PulseFixtures.Config(target: 1));

        await FirstRead(rig);
        Assert.Equal(PulseState.GoingToTop, rig.Loop.State);
        Assert.Equal("id-top", rig.Macros.RunIds.Last());
        Assert.Contains(rig.Log, l => l.Contains("past the target"));

        await Tick(rig);
        Assert.Equal(PulseState.Riding, rig.Loop.State);
        Assert.Equal(new[] { "id-on", "id-off", "id-top", "id-on" }, rig.Macros.RunIds);
    }

    [Fact]
    public async Task One_above_clears_on_the_layer_above_the_target()
    {
        var rig = Build(PulseFixtures.Config(target: 3, mode: PulseMode.OneAbove), PulseFixtures.Black);

        await FirstRead(rig);

        Assert.Equal(PulseState.Clearing, rig.Loop.State);
        Assert.Equal("id-clear-N", rig.Macros.RunIds.Last());
    }

    [Fact]
    public async Task A_pass_that_clears_nothing_rides_a_burst()
    {
        var rig = Build();
        SkipAllClears(rig);

        await FirstRead(rig);
        await Ticks(rig, 8);

        Assert.Equal(PulseState.Bursting, rig.Loop.State);
        Assert.Equal("id-on", rig.Macros.RunIds.Last());
        Assert.Contains(rig.Log, l => l.Contains("nothing in reach to clear"));
    }

    [Fact]
    public async Task A_pass_that_clears_something_reads_again_without_riding()
    {
        var rig = Build();

        await FirstRead(rig);
        await Ticks(rig, 8);
        Assert.Equal(PulseState.Pausing, rig.Loop.State);
        Assert.Equal(10, rig.Macros.Runs.Count);

        rig.Clock.Advance(999);
        await Tick(rig);
        Assert.Equal(1, rig.Reader.Reads);

        rig.Clock.Advance(1);
        await Tick(rig);
        Assert.Equal(2, rig.Reader.Reads);
        Assert.Equal("id-clear-N", rig.Macros.RunIds.Last());
        Assert.Single(rig.Macros.RunIds, id => id == "id-off");
        Assert.Contains(rig.Log, l => l.StartsWith("cleared N NE E SE S SW W NW"));
    }

    [Fact]
    public async Task A_failed_check_on_a_clear_stops_the_loop()
    {
        var rig = Build();
        rig.Macros.Script("id-clear-N", ScriptedMacros.CheckFailed);

        await FirstRead(rig);             // Clear spot N started
        await Tick(rig);                  // its check could not run

        Assert.Equal(PulseState.Stopped, rig.Loop.State);
        Assert.Contains("could not check", rig.Loop.StopReason);
        Assert.Contains("Auto Mine is off", rig.Loop.StopReason);   // Clearing always follows Auto Mine off completing
        await Tick(rig);
        Assert.Equal(3, rig.Macros.Runs.Count);
    }

    [Fact]
    public async Task Rock_cap_goes_to_top_and_says_why()
    {
        var rig = Build();
        SkipAllClears(rig);
        await FirstRead(rig);
        await Ticks(rig, 8);              // pass skipped everything: Auto Mine on started
        await Tick(rig);                  // on finished: ride timer

        rig.Clock.Advance(300_000);
        await Tick(rig);                  // ride over: Auto Mine off
        await Tick(rig);                  // off finished: settle
        rig.Clock.Advance(1000);
        await Tick(rig);                  // read: same layer, 5 minutes without a clear

        Assert.Equal(PulseState.GoingToTop, rig.Loop.State);
        Assert.Equal("id-top", rig.Macros.RunIds.Last());
        Assert.Contains(rig.Log, l => l == "Went to top: 5 minutes on the grey layer");
    }

    [Fact]
    public async Task Time_behind_does_not_count_toward_the_rock_cap()
    {
        var rig = Build();
        SkipAllClears(rig);
        await FirstRead(rig);             // t = 3 s, the grey layer starts
        await Ticks(rig, 8);              // pass skipped everything: Auto Mine on started
        await Tick(rig);                  // on finished: ride timer

        await Tick(rig, front: false);    // behind from t = 3 s
        rig.Clock.Advance(300_000);
        await Tick(rig);                  // back at t = 303 s: the ride is over, Auto Mine off
        await Tick(rig);                  // off finished: settle
        rig.Clock.Advance(1000);
        await Tick(rig);                  // read at t = 304 s: 1 s in front since the layer started

        Assert.Equal(PulseState.Clearing, rig.Loop.State);
        Assert.DoesNotContain("id-top", rig.Macros.RunIds);
    }

    [Fact]
    public async Task Riding_through_an_upper_layer_never_trips_the_rock_cap()
    {
        var rig = Build(everywhere: PulseFixtures.Navy);
        await FirstRead(rig);             // t = 3 s: navy, above the target, Auto Mine on started
        await Tick(rig);                  // on finished: ride timer

        rig.Clock.Advance(300_000);
        await Tick(rig);                  // ride over: Auto Mine off
        await Tick(rig);                  // off finished: settle
        rig.Clock.Advance(1000);
        await Tick(rig);                  // read: still navy, 301 s later

        Assert.Equal(PulseState.Riding, rig.Loop.State);
        Assert.Equal("id-on", rig.Macros.RunIds.Last());
        Assert.DoesNotContain("id-top", rig.Macros.RunIds);
    }

    [Fact]
    public async Task A_cleared_block_restarts_the_rock_cap()
    {
        var rig = Build();
        SkipAllClears(rig);
        await FirstRead(rig);             // t = 3 s, the grey layer starts
        await Ticks(rig, 8);
        await Tick(rig);
        rig.Clock.Advance(240_000);
        await Tick(rig);
        await Tick(rig);
        rig.Clock.Advance(1000);
        await Tick(rig);                  // t = 244 s: read, Clear spot N started
        rig.Macros.Script("id-clear-N", ScriptedMacros.Finished);
        await Ticks(rig, 8);              // N cleared at 244 s, the rest skipped

        rig.Clock.Advance(60_000);
        await Tick(rig);                  // t = 304 s: 60 s since the clear, not 301 s since the layer

        Assert.Equal(PulseState.Clearing, rig.Loop.State);
        Assert.DoesNotContain("id-top", rig.Macros.RunIds);
    }

    [Fact]
    public async Task Waits_while_the_account_is_not_in_front()
    {
        var rig = Build();

        await Tick(rig, front: false);
        Assert.Empty(rig.Macros.Runs);

        await Tick(rig);
        await Tick(rig, front: false);    // follows the started playback from behind
        Assert.Single(rig.Macros.Polls);

        rig.Clock.Advance(5000);
        await Tick(rig, front: false);
        Assert.Equal(new[] { "id-on" }, rig.Macros.RunIds);

        await Tick(rig);                  // back in front: the ride is long over, so pause now
        Assert.Equal(new[] { "id-on", "id-off" }, rig.Macros.RunIds);
    }

    [Fact]
    public async Task Missing_Ur_Task_stops_with_a_log_line()
    {
        var rig = Build();
        rig.Macros.RunReplies.Enqueue(ScriptedMacros.Refusal("ur-task-not-running"));

        await Tick(rig);
        await Tick(rig);

        Assert.Equal(PulseState.Stopped, rig.Loop.State);
        Assert.Contains("Ur Task is not running", rig.Loop.StopReason);
        Assert.Contains(rig.Log, l => l.StartsWith("stopped: Ur Task is not running"));
        Assert.Single(rig.Macros.Runs);
    }

    [Fact]
    public async Task A_failed_check_on_Auto_Mine_off_stops_the_loop()
    {
        var rig = Build();
        rig.Macros.Script("id-off", ScriptedMacros.CheckFailed);

        await Tick(rig);
        await Tick(rig);
        rig.Clock.Advance(2000);
        await Tick(rig);
        await Tick(rig);

        Assert.Equal(PulseState.Stopped, rig.Loop.State);
        Assert.Contains("popup or captcha", rig.Loop.StopReason);
        // The off macro never completed: a stop here always catches it mid-flight (see the Stop()
        // comment), so this groups with "may still be on", not with the states downstream of it.
        Assert.Contains("Auto Mine may still be on", rig.Loop.StopReason);
    }

    [Fact]
    public async Task A_failed_check_on_Auto_Mine_on_says_Auto_Mine_may_still_be_on()
    {
        var rig = Build();
        rig.Macros.Script("id-on", ScriptedMacros.CheckFailed);

        await Tick(rig);                  // Auto Mine on started
        await Tick(rig);                  // its check could not run

        Assert.Equal(PulseState.Stopped, rig.Loop.State);
        Assert.Contains("Auto Mine may still be on", rig.Loop.StopReason);
    }

    [Fact]
    public async Task A_failed_check_going_to_top_says_Auto_Mine_may_still_be_on()
    {
        var rig = Build(PulseFixtures.Config(target: 1), everywhere: PulseFixtures.Grey);
        rig.Macros.Script("id-top", ScriptedMacros.CheckFailed);

        await FirstRead(rig);             // grey is past the target (navy): Go to Top started
        await Tick(rig);                  // its check could not run

        Assert.Equal(PulseState.Stopped, rig.Loop.State);
        Assert.Equal("id-top", rig.Macros.RunIds.Last());
        Assert.Contains("Auto Mine may still be on", rig.Loop.StopReason);
    }

    [Fact]
    public async Task A_playback_stopped_in_Ur_Task_stops_the_loop()
    {
        var rig = Build();
        rig.Macros.Script("id-clear-N", ScriptedMacros.Stopped);

        await FirstRead(rig);
        await Tick(rig);

        Assert.Equal(PulseState.Stopped, rig.Loop.State);
        Assert.Contains("stopped in Ur Task", rig.Loop.StopReason);
        await Tick(rig);
        Assert.Equal(3, rig.Macros.Runs.Count);
    }

    [Fact]
    public async Task An_interrupted_clear_runs_the_same_spot_again()
    {
        var rig = Build();
        rig.Macros.Script("id-clear-N", ScriptedMacros.Aborted, ScriptedMacros.Finished);

        await FirstRead(rig);
        await Tick(rig, front: false);    // aborted while the account was behind
        rig.Clock.Advance(MacroCall.RetryMs);   // the rerun is paced, same as a busy retry
        await Tick(rig);                  // in front again: N once more

        Assert.Equal(2, rig.Macros.RunIds.Count(id => id == "id-clear-N"));
        Assert.Equal(PulseState.Clearing, rig.Loop.State);
    }

    [Fact]
    public async Task A_lost_playback_stops_the_loop()
    {
        var rig = Build();
        rig.Macros.Script("id-off", ScriptedMacros.Unknown);

        await Tick(rig);
        await Tick(rig);
        rig.Clock.Advance(2000);
        await Tick(rig);
        await Tick(rig);                  // Ur Task lost the off playback: stop, never run it again

        Assert.Equal(PulseState.Stopped, rig.Loop.State);
        Assert.Contains("no longer knows", rig.Loop.StopReason);
        rig.Clock.Advance(1000);
        await Tick(rig);
        Assert.Equal(new[] { "id-on", "id-off" }, rig.Macros.RunIds);
        Assert.Equal(0, rig.Reader.Reads);
    }

    [Fact]
    public async Task A_lost_clear_stops_the_loop_without_running_it_again()
    {
        var rig = Build();
        rig.Macros.Script("id-clear-N", ScriptedMacros.Unknown);

        await FirstRead(rig);             // Clear spot N started
        await Tick(rig);                  // Ur Task lost it

        Assert.Equal(PulseState.Stopped, rig.Loop.State);
        Assert.Contains("no longer knows", rig.Loop.StopReason);
        await Ticks(rig, 3);
        Assert.Equal(new[] { "id-on", "id-off", "id-clear-N" }, rig.Macros.RunIds);
    }

    [Fact]
    public async Task No_layer_on_a_calm_frame_rides_a_burst()
    {
        var rig = Build(everywhere: PulseFixtures.Orange);

        await FirstRead(rig);

        Assert.Equal(PulseState.Bursting, rig.Loop.State);
        Assert.Equal(new[] { "id-on", "id-off", "id-on" }, rig.Macros.RunIds);
        Assert.Contains(rig.Log, l => l.StartsWith("no layer on a calm frame"));
    }

    [Fact]
    public async Task An_unreadable_window_waits_in_Reading()
    {
        var rig = Build();
        rig.Reader.Next = null;

        await FirstRead(rig);
        await Tick(rig);

        Assert.Equal(PulseState.Reading, rig.Loop.State);
        Assert.Equal(2, rig.Reader.Reads);
        Assert.Equal(2, rig.Macros.Runs.Count);
        Assert.Single(rig.Log, l => l.Contains("could not read the ring"));
    }

    [Fact]
    public async Task A_bad_config_starts_stopped_and_does_nothing()
    {
        var rig = Build(PulseFixtures.Config(target: 4));

        await Tick(rig);

        Assert.Equal(PulseState.Stopped, rig.Loop.State);
        Assert.Contains("targetLayer must be 1 to 3", rig.Loop.StopReason);
        Assert.Empty(rig.Macros.Runs);
    }

    [Fact]
    public async Task In_front_at_the_tick_but_not_right_before_the_macro_starts_nothing()
    {
        var rig = Build();

        await rig.Loop.TickAsync(true, 7, CancellationToken.None, inFront: () => false);
        Assert.Empty(rig.Macros.Runs);

        await rig.Loop.TickAsync(true, 7, CancellationToken.None, inFront: () => true);
        Assert.Equal("id-on", Assert.Single(rig.Macros.Runs).MacroId);
    }
}
