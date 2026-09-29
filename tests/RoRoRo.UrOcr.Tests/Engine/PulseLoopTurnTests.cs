using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

/// <summary>
/// Looking around before riding on (Task 10): a pass with nothing in reach turns the camera up to 3
/// times ("Camera turn left") before the growing burst. Every unscripted playback finishes on its
/// first poll.
/// </summary>
public class PulseLoopTurnTests
{
    private const string TurnId = "id-turn";

    private sealed record Rig(PulseLoop Loop, ScriptedMacros Macros, ScriptedReader Reader, PulseClock Clock, List<string> Log);

    private static PulseConfig Turning(int target = 3) =>
        PulseFixtures.Config(target) with { Macros = PulseFixtures.Macros() with { CameraTurnLeft = TurnId } };

    private static Rig Build(PulseConfig? config = null, RingDefinition? ring = null, FramePixels? frame = null)
    {
        var macros = new ScriptedMacros();
        var reader = new ScriptedReader { Next = ScriptedReader.All(PulseFixtures.Grey), Frame = frame ?? PulseFixtures.Calm() };
        var clock = new PulseClock();
        var log = new List<string>();
        var loop = new PulseLoop(config ?? Turning(), new[] { ring ?? PulseFixtures.RingWithFinder() },
            PulseFixtures.Spots(), reader, macros, clock, log.Add);
        return new Rig(loop, macros, reader, clock, log);
    }

    private static Task Tick(Rig r) => r.Loop.TickAsync(true, 7, CancellationToken.None);

    /// <summary>Auto Mine on, ride 2 s, Auto Mine off, settle 1 s, read: t = 3 s, the first clear started.</summary>
    private static async Task FirstRead(Rig r)
    {
        await Tick(r);
        await Tick(r);
        r.Clock.Advance(2000);
        await Tick(r);
        await Tick(r);
        r.Clock.Advance(1000);
        await Tick(r);
    }

    /// <summary>From a turn just started: it finishes, the settle runs, the frame is read again and
    /// the next ClearAt ends with the given reply.</summary>
    private static async Task TurnThenClear(Rig r, GetPlaybackResponse next)
    {
        Assert.Equal(PulseState.Turning, r.Loop.State);
        Assert.Equal(TurnId, r.Macros.RunIds.Last());
        await Tick(r);                    // the turn finished: settle
        r.Clock.Advance(999);
        await Tick(r);
        Assert.Equal(PulseState.Turning, r.Loop.State);   // settleMs applies as usual
        r.Clock.Advance(1);
        r.Macros.Script(ScriptedMacros.ClearAtId, next);
        await Tick(r);                    // read, ClearAt started
        await Tick(r);                    // ClearAt ended
    }

    /// <summary>From a burst just started: rides it out in 500 ms steps and returns its length, then
    /// settles, reads again and ends the next ClearAt with the given reply.</summary>
    private static async Task<int> RideBurstThenClear(Rig r, GetPlaybackResponse next)
    {
        Assert.Equal(PulseState.Bursting, r.Loop.State);
        await Tick(r);
        var ms = 0;
        while (r.Loop.State == PulseState.Bursting)
        {
            r.Clock.Advance(500);
            ms += 500;
            await Tick(r);
        }
        await Tick(r);
        r.Clock.Advance(1000);
        r.Macros.Script(ScriptedMacros.ClearAtId, next);
        await Tick(r);
        await Tick(r);
        return ms;
    }

    [Fact]
    public async Task Three_empty_passes_turn_the_camera_then_the_fourth_rides_a_burst_and_the_next_round_rides_twice_as_long()
    {
        var rig = Build();
        rig.Macros.Script(ScriptedMacros.ClearAtId, ScriptedMacros.Skipped);
        await FirstRead(rig);
        await Tick(rig);                  // pass 1 skipped: turn 1

        await TurnThenClear(rig, ScriptedMacros.Skipped);   // pass 2: turn 2
        await TurnThenClear(rig, ScriptedMacros.Skipped);   // pass 3: turn 3
        await TurnThenClear(rig, ScriptedMacros.Skipped);   // pass 4: a full circle looked at, burst
        var first = await RideBurstThenClear(rig, ScriptedMacros.Skipped);   // pass 5: turn 1 again
        await TurnThenClear(rig, ScriptedMacros.Skipped);
        await TurnThenClear(rig, ScriptedMacros.Skipped);
        await TurnThenClear(rig, ScriptedMacros.Skipped);   // pass 8: burst, 2x
        var second = await RideBurstThenClear(rig, ScriptedMacros.Skipped);

        Assert.Equal(2000, first);
        Assert.Equal(4000, second);
        Assert.Equal(7, rig.Macros.RunIds.Count(id => id == TurnId));
        Assert.Equal(new[]
        {
            "nothing in reach to clear (all 13 points skipped): turning the camera (1 of 3)",
            "nothing in reach to clear (all 13 points skipped): turning the camera (2 of 3)",
            "nothing in reach to clear (all 13 points skipped): turning the camera (3 of 3)",
            "nothing in reach to clear (all 13 points skipped): riding a burst",
            "nothing in reach to clear (all 13 points skipped): turning the camera (1 of 3)",
            "nothing in reach to clear (all 13 points skipped): turning the camera (2 of 3)",
            "nothing in reach to clear (all 13 points skipped): turning the camera (3 of 3)",
            "nothing in reach to clear (all 13 points skipped): riding a burst of 4 s",
            "nothing in reach to clear (all 13 points skipped): turning the camera (1 of 3)",
        }, rig.Log.Where(l => l.StartsWith("nothing in reach")));
    }

    [Fact]
    public async Task A_turn_goes_straight_back_to_reading_without_riding()
    {
        var rig = Build();
        rig.Macros.Script(ScriptedMacros.ClearAtId, ScriptedMacros.Skipped);
        await FirstRead(rig);
        await Tick(rig);

        await TurnThenClear(rig, ScriptedMacros.Finished);

        Assert.Equal(new[] { "id-on", "id-off", ScriptedMacros.ClearAtId, TurnId, ScriptedMacros.ClearAtId }, rig.Macros.RunIds);
        Assert.Equal(2, rig.Reader.FramePids.Count);
    }

    [Fact]
    public async Task A_cleared_pass_resets_the_turns_and_the_burst()
    {
        var rig = Build();
        rig.Macros.Script(ScriptedMacros.ClearAtId, ScriptedMacros.Skipped);
        await FirstRead(rig);
        await Tick(rig);                  // pass 1 skipped: turn 1

        await TurnThenClear(rig, ScriptedMacros.Finished);  // pass 2 clears: settle, read
        Assert.Equal(PulseState.Pausing, rig.Loop.State);
        rig.Clock.Advance(1000);
        rig.Macros.Script(ScriptedMacros.ClearAtId, ScriptedMacros.Skipped);
        await Tick(rig);                  // read, ClearAt started
        await Tick(rig);                  // pass 3 skipped: turn 1 again

        Assert.Equal(new[]
        {
            "nothing in reach to clear (all 13 points skipped): turning the camera (1 of 3)",
            "nothing in reach to clear (all 13 points skipped): turning the camera (1 of 3)",
        }, rig.Log.Where(l => l.StartsWith("nothing in reach")));
    }

    [Fact]
    public async Task Without_a_turn_macro_it_rides_the_burst_as_before()
    {
        var rig = Build(PulseFixtures.Config());
        rig.Macros.Script(ScriptedMacros.ClearAtId, ScriptedMacros.Skipped);
        await FirstRead(rig);
        await Tick(rig);

        Assert.Equal(PulseState.Bursting, rig.Loop.State);
        Assert.Equal(2000, await RideBurstThenClear(rig, ScriptedMacros.Skipped));
        Assert.Equal(PulseState.Bursting, rig.Loop.State);   // pass 2 rides again, no turn
        Assert.DoesNotContain(rig.Log, l => l.Contains("turning the camera"));
        Assert.DoesNotContain(TurnId, rig.Macros.RunIds);
    }

    [Fact]
    public async Task The_eight_spot_path_turns_too()
    {
        var rig = Build(ring: PulseFixtures.Ring());
        foreach (var n in MeasuredRing.RingOrder) rig.Macros.Script($"id-clear-{n}", ScriptedMacros.Skipped);
        await FirstRead(rig);
        for (var i = 0; i < 8; i++) await Tick(rig);

        Assert.Equal(PulseState.Turning, rig.Loop.State);
        Assert.Equal(TurnId, rig.Macros.RunIds.Last());
        Assert.Contains(rig.Log, l => l == "nothing in reach to clear (8 spots skipped): turning the camera (1 of 3)");
    }

    [Fact]
    public async Task A_turn_stopped_in_Ur_Task_stops_the_loop_with_Auto_Mine_off()
    {
        var rig = Build();
        rig.Macros.Script(ScriptedMacros.ClearAtId, ScriptedMacros.Skipped);
        rig.Macros.Script(TurnId, ScriptedMacros.Stopped);
        await FirstRead(rig);
        await Tick(rig);                  // pass 1 skipped: turn started
        await Tick(rig);                  // the turn was stopped

        Assert.Equal(PulseState.Stopped, rig.Loop.State);
        Assert.Contains("Camera turn left", rig.Loop.StopReason);
        Assert.EndsWith("Auto Mine is off.", rig.Loop.StopReason);
    }

    [Fact]
    public async Task Going_to_top_resets_the_turns()
    {
        // Clears on black (layer 2): a grey frame is past it.
        var black = PulseFixtures.Calm((0, 0, 800, 599, PulseFixtures.Black));
        var rig = Build(Turning(target: 2), PulseFixtures.RingWithFinder("black"), black);
        rig.Macros.Script(ScriptedMacros.ClearAtId, ScriptedMacros.Skipped);
        await FirstRead(rig);
        await Tick(rig);                  // turn 1
        await TurnThenClear(rig, ScriptedMacros.Skipped);   // turn 2

        await Tick(rig);                  // turn 2 finished: settle
        rig.Clock.Advance(1000);
        rig.Reader.Frame = PulseFixtures.Calm();            // grey: past the target
        await Tick(rig);                  // read: going to top, Go to Top started
        await Tick(rig);                  // Go to Top finished: riding
        Assert.Equal(PulseState.Riding, rig.Loop.State);
        await Tick(rig);                  // Auto Mine on finished
        rig.Clock.Advance(2000);
        await Tick(rig);
        await Tick(rig);
        rig.Clock.Advance(1000);
        rig.Reader.Frame = black;
        await Tick(rig);                  // read on black, ClearAt started
        await Tick(rig);                  // skipped: turn 1 again

        Assert.EndsWith("turning the camera (1 of 3)", rig.Log.Last(l => l.StartsWith("nothing in reach")));
    }
}
