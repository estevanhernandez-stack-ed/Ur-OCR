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

    private static Rig Build(RingDefinition? ring = null, FramePixels? frame = null, PulseConfig? config = null)
    {
        var macros = new ScriptedMacros();
        var reader = new ScriptedReader { Next = ScriptedReader.All(PulseFixtures.Grey), Frame = frame ?? PulseFixtures.Calm() };
        var clock = new PulseClock();
        var log = new List<string>();
        var loop = new PulseLoop(config ?? PulseFixtures.Config(), new[] { ring ?? PulseFixtures.RingWithFinder() },
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
        Assert.Equal(new ClearAtOutline(240, 240, 60, 225), req.Outline);   // plain rock: no block size read
        Assert.Equal(13, req.Points.Count);
        Assert.Equal(new ClearAtPoint(390, 340, "stone 1"), req.Points[0]);
        Assert.Equal(new[] { 7 }, rig.Reader.FramePids);
    }

    [Fact]
    public async Task Without_a_guard_ClearAt_carries_none()
    {
        var rig = Build();

        await FirstRead(rig);

        Assert.Null(Assert.Single(rig.Macros.ClearAts).Guard);
    }

    [Fact]
    public async Task The_finder_s_guard_goes_on_every_ClearAt()
    {
        var guard = new GuardBox(55, 289, 3, 3, new Rgb(255, 19, 90), 30);
        var ring = PulseFixtures.Ring() with { Finders = new[] { PulseFixtures.Finder() with { Guard = guard } } };
        var rig = Build(ring);

        await FirstRead(rig);

        var req = Assert.Single(rig.Macros.ClearAts);
        Assert.Equal(new ClearAtGuard(55, 289, 3, 3, new Rgb(255, 19, 90), 30), req.Guard);
    }

    [Fact]
    public async Task Ore_goes_first_in_the_points()
    {
        var rig = Build(frame: PulseFixtures.Calm((465, 315, 50, 50, PulseFixtures.Cyan)));

        await FirstRead(rig);

        var req = Assert.Single(rig.Macros.ClearAts);
        Assert.Equal(new ClearAtPoint(490, 340, "ore 1"), req.Points[0]);   // the grid point on the ore beats its centre (492, 336)
        Assert.Equal(13, req.Points.Count);
        Assert.Single(req.Points, p => (p.X, p.Y) == (490, 340));            // as ore, not again as stone
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
        Assert.Equal(2, rig.Reader.FramePids.Count);
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

    /// <summary>From a ClearAt just ended in Bursting: rides out the burst in 500 ms steps and returns
    /// its length, then settles and reads again, and ends the next ClearAt with the given reply.</summary>
    private static async Task<int> RideBurstThenClear(Rig r, GetPlaybackResponse next)
    {
        Assert.Equal(PulseState.Bursting, r.Loop.State);
        await Tick(r);                    // Auto Mine on finished: the ride timer starts
        var ms = 0;
        while (r.Loop.State == PulseState.Bursting)
        {
            r.Clock.Advance(500);
            ms += 500;
            await Tick(r);
        }
        await Tick(r);                    // Auto Mine off finished: settle
        r.Clock.Advance(1000);
        r.Macros.Script(ScriptedMacros.ClearAtId, next);
        await Tick(r);                    // read, ClearAt started
        await Tick(r);                    // ClearAt ended
        return ms;
    }

    [Fact]
    public async Task Each_pass_in_a_row_with_nothing_in_reach_rides_twice_as_long()
    {
        var rig = Build();
        rig.Macros.Script(ScriptedMacros.ClearAtId, ScriptedMacros.Skipped);
        await FirstRead(rig);
        await Tick(rig);                  // pass 1 skipped

        var bursts = new List<int>();
        for (var i = 0; i < 3; i++) bursts.Add(await RideBurstThenClear(rig, ScriptedMacros.Skipped));
        bursts.Add(await RideBurstThenClear(rig, ScriptedMacros.Finished));   // pass 5 clears: settle, read
        rig.Clock.Advance(1000);
        await Tick(rig);                  // read, ClearAt started
        rig.Macros.Script(ScriptedMacros.ClearAtId, ScriptedMacros.Skipped);
        await Tick(rig);                  // pass 6 skipped
        bursts.Add(await RideBurstThenClear(rig, ScriptedMacros.Skipped));

        Assert.Equal(new[] { 2000, 4000, 8000, 16000, 2000 }, bursts);
        Assert.Equal(new[]
        {
            "nothing in reach to clear (all 13 points skipped): riding a burst",
            "nothing in reach to clear (all 13 points skipped): riding a burst of 4 s",
            "nothing in reach to clear (all 13 points skipped): riding a burst of 8 s",
            "nothing in reach to clear (all 13 points skipped): riding a burst of 16 s",
            "nothing in reach to clear (all 13 points skipped): riding a burst",
            "nothing in reach to clear (all 13 points skipped): riding a burst of 4 s",
        }, rig.Log.Where(l => l.StartsWith("nothing in reach")));
    }

    [Fact]
    public async Task The_growing_burst_stops_at_30_seconds()
    {
        var rig = Build();
        rig.Macros.Script(ScriptedMacros.ClearAtId, ScriptedMacros.Skipped);
        await FirstRead(rig);
        await Tick(rig);

        var bursts = new List<int>();
        for (var i = 0; i < 7; i++) bursts.Add(await RideBurstThenClear(rig, ScriptedMacros.Skipped));

        Assert.Equal(new[] { 2000, 4000, 8000, 16000, 30000, 30000, 30000 }, bursts);
        Assert.Equal(PulseLoop.MaxBurstMs, bursts.Max());
        Assert.Contains(rig.Log, l => l.EndsWith("riding a burst of 30 s"));
    }

    [Fact]
    public async Task Going_to_top_resets_the_burst()
    {
        // Clears on black (layer 2): a grey frame is past it.
        var ring = PulseFixtures.RingWithFinder("black");
        var rig = Build(ring, Solid(PulseFixtures.Black), PulseFixtures.Config(target: 2));
        rig.Macros.Script(ScriptedMacros.ClearAtId, ScriptedMacros.Skipped);
        await FirstRead(rig);
        await Tick(rig);                  // pass 1 skipped: 2 s
        Assert.Equal(2000, await RideBurstThenClear(rig, ScriptedMacros.Skipped));
        Assert.Equal(4000, await RideBurstThenClear(rig, ScriptedMacros.Skipped));   // the next burst would be 8 s

        rig.Reader.Frame = Solid(PulseFixtures.Grey);
        await Tick(rig);                  // Auto Mine on finished
        while (rig.Loop.State == PulseState.Bursting)
        {
            rig.Clock.Advance(500);
            await Tick(rig);
        }
        await Tick(rig);                  // off finished: settle
        rig.Clock.Advance(1000);
        await Tick(rig);                  // read grey: past the target, Go to Top started
        Assert.Equal(PulseState.GoingToTop, rig.Loop.State);
        await Tick(rig);                  // Go to Top finished: riding; Auto Mine on started
        await Tick(rig);                  // on finished: ride timer
        rig.Reader.Frame = Solid(PulseFixtures.Black);
        rig.Clock.Advance(2000);
        await Tick(rig);                  // Auto Mine off started
        await Tick(rig);                  // settle
        rig.Clock.Advance(1000);
        await Tick(rig);                  // read black: ClearAt started
        await Tick(rig);                  // skipped

        Assert.Equal(2000, await RideBurstThenClear(rig, ScriptedMacros.Skipped));
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

    /// <summary>A calm frame of grey blocks with bright 2 px seams every <paramref name="spacing"/> px.</summary>
    private static FramePixels Blocks(int spacing, int w = 800, int h = 599) =>
        new(w, h, Frames.Grid(w, h, spacing, spacing, PulseFixtures.Grey, new Rgb(225, 230, 240)));

    /// <summary>An 800x599 frame of one colour.</summary>
    private static FramePixels Solid(Rgb c) => new(800, 599, Frames.Solid(800, 599, c));

    [Fact]
    public async Task The_layer_is_read_by_colour_share_on_the_calm_frame_not_the_spots()
    {
        var rig = Build();
        rig.Reader.Next = ScriptedReader.All(PulseFixtures.Navy);     // the spots would say navy

        await FirstRead(rig);

        Assert.Equal(PulseState.Clearing, rig.Loop.State);
        Assert.Equal("grey", rig.Loop.Layer);
        Assert.Equal(0, rig.Reader.Reads);
        Assert.Equal(new[] { 7 }, rig.Reader.FramePids);               // one calm frame: block size, layer, targets
        Assert.Contains(rig.Log, l => l == "layer grey (100% of the area, next navy 0%) is the target: clearing at 13 points (0 ore, 13 stone)");
    }

    [Fact]
    public async Task A_frame_with_no_layer_colours_rides_a_burst()
    {
        var rig = Build(frame: Solid(PulseFixtures.Sky));

        await FirstRead(rig);

        Assert.Equal(PulseState.Bursting, rig.Loop.State);
        Assert.Empty(rig.Macros.ClearAts);
        Assert.Contains(rig.Log, l => l == "no layer on a calm frame (best navy 0%): riding a burst");
    }

    [Fact]
    public async Task A_frame_of_a_layer_above_the_target_rides_on()
    {
        var rig = Build(frame: Solid(PulseFixtures.Navy));

        await FirstRead(rig);

        Assert.Equal(PulseState.Riding, rig.Loop.State);
        Assert.Empty(rig.Macros.ClearAts);
        Assert.Contains(rig.Log, l => l == "layer navy (100% of the area, next black 0%) is above the target (grey): riding on");
    }

    [Fact]
    public async Task The_ring_can_raise_the_minimum_share()
    {
        // Sky over the top half of the view: grey is well under 90% of the disc.
        var frame = PulseFixtures.Calm((0, 0, 800, 330, PulseFixtures.Sky));
        var strict = Build(PulseFixtures.RingWithFinder() with { LayerMinShare = 0.9 }, frame);
        var plain = Build(frame: frame);

        await FirstRead(strict);
        await FirstRead(plain);

        Assert.Equal(PulseState.Bursting, strict.Loop.State);
        Assert.Equal(PulseState.Clearing, plain.Loop.State);
    }

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
    public async Task A_frame_with_no_clear_pattern_uses_the_layer_block_size_with_a_big_box()
    {
        var rig = Build();                                   // plain grey rock

        await FirstRead(rig);

        var req = Assert.Single(rig.Macros.ClearAts);
        Assert.Equal(new ClearAtOutline(FinderSetup.MaxOutlineSide, FinderSetup.MaxOutlineSide, 60, 225), req.Outline);
        Assert.Contains(req.Points, p => (p.X, p.Y) == (440, 340));   // the grid still one layer block apart
        Assert.Contains(rig.Log, l => l == "block size 50 px (layer default; no clear pattern)");
    }

    /// <summary>Radius 5 around 390,340: far more than 16 grid points fit a 240 box on the 800x599 client.</summary>
    private static RingDefinition WideRing() =>
        PulseFixtures.Ring() with { Finders = new[] { PulseFixtures.Finder() with { RadiusBlocks = 5 } } };

    [Fact]
    public async Task An_unread_block_size_checks_the_16_nearest_stone_points_and_every_ore_point()
    {
        var rig = Build(WideRing(), PulseFixtures.Calm((565, 315, 50, 50, PulseFixtures.Cyan)));

        await FirstRead(rig);

        var req = Assert.Single(rig.Macros.ClearAts);
        Assert.Equal(new ClearAtOutline(240, 240, 60, 225), req.Outline);
        Assert.Equal(new ClearAtPoint(588, 336, "ore 1"), req.Points[0]);    // the patch centre, nearer than its grid point
        Assert.Equal(16, req.Points.Count(p => p.Label.StartsWith("stone")));
        Assert.Equal(17, req.Points.Count);
        Assert.Equal(new ClearAtPoint(390, 340, "stone 1"), req.Points[1]);   // nearest stone first
        Assert.Equal("stone 16", req.Points[^1].Label);
        Assert.Contains(rig.Log, l => l.EndsWith("is the target: clearing at 17 points (1 ore, 16 stone)"));
    }

    [Fact]
    public async Task A_read_block_size_keeps_the_box_at_the_block_and_the_full_grid()
    {
        var rig = Build(WideRing(), Blocks(32));

        await FirstRead(rig);

        var req = Assert.Single(rig.Macros.ClearAts);
        Assert.Equal(new ClearAtOutline(32, 32, 60, 225), req.Outline);
        Assert.Equal(TargetFinder.MaxPoints, req.Points.Count);   // radius 5 is 81 grid points: the full grid, capped at 64
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
        // Pitch lowered to 20 so a read of 18 stays trusted under the half-the-default rule (Task 12);
        // this test is only about minCount clamping to the box, not that rule.
        var finder = PulseFixtures.Finder() with { Pitch = 20, Outline = new OutlineBox(50, 50, MinCount: 400) };
        var rig = Build(PulseFixtures.Ring() with { Finders = new[] { finder } }, Blocks(18));

        await FirstRead(rig);

        var req = Assert.Single(rig.Macros.ClearAts);
        Assert.Equal(new ClearAtOutline(18, 18, 324, 225), req.Outline);
        Assert.Contains(rig.Log, l => l == "block size 18 px (read from the frame); outline minCount 324 to fit the 18x18 box");
    }

    [Fact]
    public async Task A_read_under_half_the_layer_default_counts_as_unread()
    {
        // The layer default is 50 (PulseFixtures.Finder); 18 is under half of it (25), the exact
        // shape of the live finding: fine crack texture inside the black-layer blocks read as 18-24 px
        // while the layer's real blocks are 30+ px.
        var rig = Build(WideRing(), Blocks(18));

        await FirstRead(rig);

        var req = Assert.Single(rig.Macros.ClearAts);
        Assert.Equal(new ClearAtOutline(240, 240, 60, 225), req.Outline);
        Assert.Equal(16, req.Points.Count(p => p.Label.StartsWith("stone")));
        Assert.Equal(16, req.Points.Count);
        Assert.Contains(rig.Log, l => l == "block size 50 px (layer default; read 18 px is under half the default)");
    }

    [Fact]
    public async Task A_read_just_over_half_the_default_is_trusted()
    {
        var rig = Build(frame: Blocks(26));                  // half of 50 is 25; 26 stays trusted

        await FirstRead(rig);

        var req = Assert.Single(rig.Macros.ClearAts);
        Assert.Equal(new ClearAtOutline(26, 26, 60, 225), req.Outline);
        Assert.Contains(rig.Log, l => l == "block size 26 px (read from the frame)");
    }

    [Fact]
    public async Task A_much_larger_read_is_trusted()
    {
        var rig = Build(frame: Blocks(60));                  // only the lower bound is new; larger reads are unaffected

        await FirstRead(rig);

        var req = Assert.Single(rig.Macros.ClearAts);
        Assert.Equal(new ClearAtOutline(60, 60, 60, 225), req.Outline);
        Assert.Contains(rig.Log, l => l == "block size 60 px (read from the frame)");
    }

    // Live 2026-09-30: with blocks near 110 px the read swung to 30, 33, 29 and 47 px on later passes,
    // and each of those passes gridded and boxed at the swung size. An account that set its block
    // size ignores a read far from it.

    [Fact]
    public async Task A_read_far_from_the_account_s_block_size_uses_the_setting()
    {
        var rig = Build(frame: Blocks(32), config: PulseFixtures.Config() with { SweepBlockPx = 110 });

        await FirstRead(rig);

        var req = Assert.Single(rig.Macros.ClearAts);
        Assert.Equal(new ClearAtOutline(110, 110, 60, 225), req.Outline);
        Assert.Contains(req.Points, p => (p.X, p.Y) == (500, 340));    // the grid one setting block apart
        Assert.DoesNotContain(req.Points, p => (p.X, p.Y) == (422, 340));
        Assert.Equal(new[] { "block size 110 px (account setting; read 32 px is far from it)" },
            rig.Log.Where(l => l.StartsWith("block size")));
    }

    [Theory]
    [InlineData(42, true)]      // 0.75 x 42 = 31.5: 32 is inside
    [InlineData(43, false)]     // 0.75 x 43 = 32.25: 32 is under it
    [InlineData(25, true)]      // 1.33 x 25 = 33.25: 32 is inside
    [InlineData(24, false)]     // 1.33 x 24 = 31.92: 32 is over it
    public async Task A_read_counts_as_near_the_setting_between_three_quarters_and_four_thirds_of_it(int setting, bool near)
    {
        var rig = Build(frame: Blocks(32), config: PulseFixtures.Config() with { SweepBlockPx = setting });

        await FirstRead(rig);

        var side = Assert.Single(rig.Macros.ClearAts).Outline.W;
        Assert.Equal(near ? 32 : setting, side);
        Assert.Contains(rig.Log, l => l == (near
            ? "block size 32 px (read from the frame)"
            : $"block size {setting} px (account setting; read 32 px is far from it)"));
        Assert.Equal((0.75, 1.33), (PulseLoop.SettingReadMin, PulseLoop.SettingReadMax));
    }

    [Fact]
    public async Task A_read_under_half_the_default_uses_the_account_s_block_size_when_it_has_one()
    {
        var rig = Build(WideRing(), Blocks(18), PulseFixtures.Config() with { SweepBlockPx = 110 });

        await FirstRead(rig);

        var req = Assert.Single(rig.Macros.ClearAts);
        Assert.Equal(new ClearAtOutline(110, 110, 60, 225), req.Outline);
        Assert.Contains(rig.Log, l => l == "block size 110 px (account setting; read 18 px is far from it)");
    }

    [Fact]
    public async Task No_read_with_an_account_setting_still_uses_the_layer_default_for_the_finder()
    {
        var rig = Build(frame: PulseFixtures.Calm(), config: PulseFixtures.Config() with { SweepBlockPx = 110 });

        await FirstRead(rig);

        Assert.Equal(new ClearAtOutline(FinderSetup.MaxOutlineSide, FinderSetup.MaxOutlineSide, 60, 225),
            Assert.Single(rig.Macros.ClearAts).Outline);
        Assert.Contains(rig.Log, l => l == "block size 50 px (layer default; no clear pattern)");
    }
}
