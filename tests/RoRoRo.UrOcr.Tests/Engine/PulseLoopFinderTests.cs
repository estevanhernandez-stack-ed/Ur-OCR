using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

/// <summary>
/// The pulse loop with an ore finder on its aim layer (grey, layer 3): one ClearAt per pass instead
/// of the eight Clear spots. Every unscripted playback finishes on its first poll.
/// </summary>
public class PulseLoopFinderTests
{
    private sealed record Rig(PulseLoop Loop, ScriptedMacros Macros, ScriptedReader Reader, PulseClock Clock, List<string> Log);

    private static Rig Build(RingDefinition? ring = null, FramePixels? frame = null)
    {
        var macros = new ScriptedMacros();
        var reader = new ScriptedReader { Next = ScriptedReader.All(PulseFixtures.Grey), Frame = frame ?? PulseFixtures.Calm() };
        var clock = new PulseClock();
        var log = new List<string>();
        var loop = new PulseLoop(PulseFixtures.Config(), new[] { ring ?? PulseFixtures.RingWithFinder() },
            PulseFixtures.Spots(), reader, macros, clock, log.Add);
        return new Rig(loop, macros, reader, clock, log);
    }

    private static Task Tick(Rig r, bool front = true) => r.Loop.TickAsync(front, 7, CancellationToken.None);

    private static async Task Ticks(Rig r, int n)
    {
        for (var i = 0; i < n; i++) await Tick(r);
    }

    /// <summary>Auto Mine on, ride 2 s, Auto Mine off, settle 1 s, read: t = 3 s, ClearAt started.</summary>
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

    [Fact]
    public async Task Starts_saying_it_clears_with_the_ore_finder()
    {
        var rig = Build();

        await Tick(rig);

        Assert.Contains(rig.Log, l => l.StartsWith("started") && l.Contains("with the ore finder (50 px blocks, radius 2)"));
    }

    [Fact]
    public async Task At_the_target_one_ClearAt_replaces_the_eight_Clear_spots()
    {
        var rig = Build();

        await FirstRead(rig);

        Assert.Equal(PulseState.Clearing, rig.Loop.State);
        Assert.Equal(new[] { "id-on", "id-off", ScriptedMacros.ClearAtId }, rig.Macros.RunIds);
        var req = Assert.Single(rig.Macros.ClearAts);
        Assert.Equal("42", req.Target);
        Assert.Equal(new ClearAtClient(800, 599), req.Client);          // the measured client, not the live one
        Assert.Equal(new ClearAtOutline(50, 50, 60, 225), req.Outline);
        Assert.Equal(13, req.Points.Count);
        Assert.Equal(new ClearAtPoint(390, 340, "stone 1"), req.Points[0]);
        Assert.Equal(new[] { 7 }, rig.Reader.FramePids);
    }

    [Fact]
    public async Task Ore_goes_first_in_the_points()
    {
        var rig = Build(frame: PulseFixtures.Calm((465, 315, 50, 50, PulseFixtures.Cyan)));

        await FirstRead(rig);

        var req = Assert.Single(rig.Macros.ClearAts);
        Assert.Equal(new ClearAtPoint(492, 336, "ore 1"), req.Points[0]);
        Assert.Equal(13, req.Points.Count);
        Assert.DoesNotContain(req.Points, p => (p.X, p.Y) == (490, 340));    // the grid point inside the ore
        Assert.Contains(rig.Log, l => l.EndsWith("is the target: clearing at 13 points (1 ore, 12 stone)"));
    }

    [Fact]
    public async Task A_finished_ClearAt_reads_again_without_riding()
    {
        var rig = Build();
        await FirstRead(rig);

        await Tick(rig);                  // ClearAt finished: settle, Auto Mine stays off
        Assert.Equal(PulseState.Pausing, rig.Loop.State);
        Assert.Contains(rig.Log, l => l == "cleared at the ore finder's points (13 tried): reading again");

        rig.Clock.Advance(1000);
        await Tick(rig);
        Assert.Equal(2, rig.Reader.Reads);
        Assert.Equal(new[] { "id-on", "id-off", ScriptedMacros.ClearAtId, ScriptedMacros.ClearAtId }, rig.Macros.RunIds);
    }

    [Fact]
    public async Task A_skipped_ClearAt_rides_a_burst()
    {
        var rig = Build();
        rig.Macros.Script(ScriptedMacros.ClearAtId, ScriptedMacros.Skipped);
        await FirstRead(rig);

        await Tick(rig);

        Assert.Equal(PulseState.Bursting, rig.Loop.State);
        Assert.Equal("id-on", rig.Macros.RunIds.Last());
        Assert.Contains(rig.Log, l => l == "nothing in reach to clear (all 13 points skipped): riding a burst");
        Assert.DoesNotContain(rig.Macros.RunIds, id => id.StartsWith("id-clear-"));
    }

    [Fact]
    public async Task A_finished_ClearAt_restarts_the_rock_cap()
    {
        var rig = Build();
        rig.Macros.Script(ScriptedMacros.ClearAtId, ScriptedMacros.Skipped);
        await FirstRead(rig);             // t = 3 s: the grey layer starts, ClearAt started
        await Tick(rig);                  // skipped: Auto Mine on started
        await Tick(rig);                  // on finished: ride timer
        rig.Clock.Advance(240_000);
        await Tick(rig);                  // Auto Mine off started
        await Tick(rig);                  // off finished: settle
        rig.Clock.Advance(1000);
        await Tick(rig);                  // t = 244 s: read, ClearAt started
        rig.Macros.Script(ScriptedMacros.ClearAtId, ScriptedMacros.Finished);
        await Tick(rig);                  // it cleared something at 244 s: settle

        rig.Clock.Advance(60_000);
        await Tick(rig);                  // t = 304 s: 60 s since the clear, not 301 s since the layer

        Assert.Equal(PulseState.Clearing, rig.Loop.State);
        Assert.DoesNotContain("id-top", rig.Macros.RunIds);
        Assert.Equal(3, rig.Macros.ClearAts.Count);
    }

    [Fact]
    public async Task Skipped_ClearAts_still_trip_the_rock_cap()
    {
        var rig = Build();
        rig.Macros.Script(ScriptedMacros.ClearAtId, ScriptedMacros.Skipped);
        await FirstRead(rig);             // t = 3 s
        await Tick(rig);                  // skipped: Auto Mine on started
        await Tick(rig);                  // on finished: ride timer

        rig.Clock.Advance(300_000);
        await Tick(rig);                  // Auto Mine off
        await Tick(rig);                  // settle
        rig.Clock.Advance(1000);
        await Tick(rig);                  // t = 304 s: 5 minutes on grey without clearing

        Assert.Equal(PulseState.GoingToTop, rig.Loop.State);
        Assert.Equal("id-top", rig.Macros.RunIds.Last());
        Assert.Contains(rig.Log, l => l == "Went to top: 5 minutes on the grey layer");
    }

    [Fact]
    public async Task Without_a_finder_for_the_aim_layer_it_clears_the_eight_spots()
    {
        var rig = Build(PulseFixtures.RingWithFinder("navy"));

        await FirstRead(rig);

        Assert.Equal("id-clear-N", rig.Macros.RunIds.Last());
        Assert.Empty(rig.Macros.ClearAts);
        Assert.Empty(rig.Reader.FramePids);
        Assert.Contains(rig.Log, l => l.StartsWith("started") && l.Contains("with the 8 Clear spot macros"));
    }

    [Fact]
    public async Task An_Ur_Task_without_ClearAt_stops_the_loop_and_says_so()
    {
        var rig = Build();
        rig.Macros.ClearAtReplies.Enqueue(new RunMacroResponse(false, null, false, "refused", "Unknown method 'ClearAt'."));

        await FirstRead(rig);

        Assert.Equal(PulseState.Stopped, rig.Loop.State);
        Assert.Contains("Unknown method 'ClearAt'", rig.Loop.StopReason);
        Assert.Contains("Auto Mine is off", rig.Loop.StopReason);
        await Ticks(rig, 3);
        Assert.Single(rig.Macros.ClearAts);
    }

    [Fact]
    public async Task A_ClearAt_whose_check_could_not_run_stops_the_loop()
    {
        var rig = Build();
        rig.Macros.Script(ScriptedMacros.ClearAtId, ScriptedMacros.CheckFailed);

        await FirstRead(rig);
        await Tick(rig);

        Assert.Equal(PulseState.Stopped, rig.Loop.State);
        Assert.Contains("could not check", rig.Loop.StopReason);
        Assert.Contains("Auto Mine is off", rig.Loop.StopReason);
    }

    [Fact]
    public async Task A_ClearAt_stopped_in_Ur_Task_stops_the_loop()
    {
        var rig = Build();
        rig.Macros.Script(ScriptedMacros.ClearAtId, ScriptedMacros.Stopped);

        await FirstRead(rig);
        await Tick(rig);

        Assert.Equal(PulseState.Stopped, rig.Loop.State);
        Assert.Contains("stopped in Ur Task", rig.Loop.StopReason);
    }

    [Fact]
    public async Task A_window_it_cannot_capture_waits_in_Reading_and_logs_once()
    {
        var rig = Build();
        rig.Reader.Frame = null;

        await FirstRead(rig);
        await Tick(rig);

        Assert.Equal(PulseState.Reading, rig.Loop.State);
        Assert.Equal(2, rig.Reader.FramePids.Count);
        Assert.Equal(2, rig.Macros.Runs.Count);
        Assert.Single(rig.Log, l => l.Contains("could not capture the window"));
    }

    [Fact]
    public async Task A_lost_ClearAt_stops_without_running_it_again()
    {
        var rig = Build();
        rig.Macros.Script(ScriptedMacros.ClearAtId, ScriptedMacros.Unknown);

        await FirstRead(rig);
        await Tick(rig);

        Assert.Equal(PulseState.Stopped, rig.Loop.State);
        Assert.Contains("no longer knows", rig.Loop.StopReason);
        await Ticks(rig, 3);
        Assert.Equal(new[] { "id-on", "id-off", ScriptedMacros.ClearAtId }, rig.Macros.RunIds);
    }

    /// <summary>A calm frame of dark blocks with bright 2 px seams every <paramref name="spacing"/> px.</summary>
    private static FramePixels Blocks(int spacing, int w = 800, int h = 599) =>
        new(w, h, Frames.Grid(w, h, spacing, spacing, new Rgb(40, 45, 60), new Rgb(225, 230, 240)));

    [Fact]
    public async Task The_block_size_comes_from_the_frame_not_the_layer()
    {
        var rig = Build(frame: Blocks(32));                 // the layer says 50

        await FirstRead(rig);

        var req = Assert.Single(rig.Macros.ClearAts);
        Assert.Equal(new ClearAtOutline(32, 32, 60, 225), req.Outline);
        Assert.Equal(13, req.Points.Count);
        Assert.Equal(new ClearAtPoint(390, 340, "stone 1"), req.Points[0]);
        Assert.Contains(req.Points, p => (p.X, p.Y) == (422, 340));
        Assert.Contains(req.Points, p => (p.X, p.Y) == (390, 276));
        Assert.Contains(rig.Log, l => l == "block size 32 px (read from the frame)");
    }

    [Fact]
    public async Task A_frame_with_no_clear_pattern_uses_the_layer_block_size()
    {
        var rig = Build();                                   // plain grey rock

        await FirstRead(rig);

        var req = Assert.Single(rig.Macros.ClearAts);
        Assert.Equal(new ClearAtOutline(50, 50, 60, 225), req.Outline);
        Assert.Contains(req.Points, p => (p.X, p.Y) == (440, 340));
        Assert.Contains(rig.Log, l => l == "block size 50 px (layer default; no clear pattern)");
    }

    [Fact]
    public async Task A_frame_at_another_size_gives_the_block_size_in_measured_pixels()
    {
        var rig = Build(frame: Blocks(40, 1000, 749));      // 125%: 40 live px is 32 measured

        await FirstRead(rig);

        var req = Assert.Single(rig.Macros.ClearAts);
        Assert.Equal(new ClearAtOutline(32, 32, 60, 225), req.Outline);
        Assert.Contains(req.Points, p => (p.X, p.Y) == (422, 340));
    }

    [Fact]
    public async Task The_block_size_is_logged_only_when_it_changes()
    {
        var rig = Build(frame: Blocks(32));
        await FirstRead(rig);
        await Tick(rig);                  // finished: settle
        rig.Clock.Advance(1000);
        await Tick(rig);                  // second read, same frame
        Assert.Equal(2, rig.Macros.ClearAts.Count);
        Assert.Single(rig.Log, l => l.StartsWith("block size"));

        rig.Reader.Frame = PulseFixtures.Calm();
        await Tick(rig);                  // finished: settle
        rig.Clock.Advance(1000);
        await Tick(rig);                  // third read: no pattern

        Assert.Equal(3, rig.Macros.ClearAts.Count);
        Assert.Equal(new[] { "block size 32 px (read from the frame)", "block size 50 px (layer default; no clear pattern)" },
            rig.Log.Where(l => l.StartsWith("block size")));
    }

    [Fact]
    public async Task A_small_block_keeps_minCount_inside_its_box()
    {
        var finder = PulseFixtures.Finder() with { Outline = new OutlineBox(50, 50, MinCount: 400) };
        var rig = Build(PulseFixtures.Ring() with { Finders = new[] { finder } }, Blocks(18));

        await FirstRead(rig);

        var req = Assert.Single(rig.Macros.ClearAts);
        Assert.Equal(new ClearAtOutline(18, 18, 324, 225), req.Outline);
        Assert.Contains(rig.Log, l => l == "block size 18 px (read from the frame); outline minCount 324 to fit the 18x18 box");
    }
}
