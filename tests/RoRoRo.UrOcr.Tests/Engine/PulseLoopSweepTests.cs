using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

/// <summary>
/// The pulse with a guarded ore finder on a frame whose block size reads (32 px seams): the ore through
/// ClearAt, the stone in one SweepPath, then a read that judges the sweep. Every unscripted playback
/// finishes on its first poll.
/// </summary>
public class PulseLoopSweepTests
{
    private sealed record Rig(PulseLoop Loop, ScriptedMacros Macros, ScriptedReader Reader, PulseClock Clock, List<string> Log);

    private static readonly Rgb Seam = new(225, 230, 240);
    private static readonly ClearAtGuard WireDot = new(55, 289, 3, 3, new Rgb(255, 19, 90), 30);

    /// <summary>A cyan crystal inside one 32 px block, about 47 px right of the character.</summary>
    private static readonly (int X, int Y, int W, int H, Rgb Colour) Crystal = (425, 329, 24, 24, PulseFixtures.Cyan);

    /// <summary>800x599 of 32 px <paramref name="block"/> blocks with bright seams, rectangles painted over.</summary>
    private static FramePixels Blocks(Rgb block, params (int X, int Y, int W, int H, Rgb Colour)[] paint)
    {
        var px = Frames.Grid(800, 599, 32, 32, block, Seam);
        foreach (var (x, y, w, h, c) in paint) Frames.Fill(px, 800, x, y, w, h, c);
        return new FramePixels(800, 599, px);
    }

    private static FramePixels Grey(params (int X, int Y, int W, int H, Rgb Colour)[] paint) => Blocks(PulseFixtures.Grey, paint);

    /// <summary>The pass the loop plans on a 32 px frame: the fixture finder at the read block size.</summary>
    private static IReadOnlyList<SweepPoint> PathFor(bool nearSide = true) =>
        SweepPath.Build(PulseFixtures.Finder() with { Pitch = 32, Guard = PulseFixtures.Dot }, nearSide);

    private static Rig Build(FramePixels frame, PulseConfig? config = null, RingDefinition? ring = null)
    {
        var macros = new ScriptedMacros();
        var reader = new ScriptedReader { Next = ScriptedReader.All(PulseFixtures.Grey), Frame = frame };
        var clock = new PulseClock();
        var log = new List<string>();
        var loop = new PulseLoop(config ?? PulseFixtures.Config(), new[] { ring ?? PulseFixtures.RingWithSweep() },
            PulseFixtures.Spots(), reader, macros, clock, log.Add);
        return new Rig(loop, macros, reader, clock, log);
    }

    private static Task Tick(Rig r) => r.Loop.TickAsync(true, 7, CancellationToken.None);

    /// <summary>Auto Mine on, ride 2 s, Auto Mine off, settle 1 s, read: the pass's first call started.</summary>
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
    public async Task Starts_saying_it_sweeps_stone()
    {
        var rig = Build(Grey());

        await Tick(rig);

        Assert.Contains(rig.Log, l => l.StartsWith("started")
            && l.EndsWith("with the ore finder (50 px blocks, radius 2), sweeping stone (400 ms a point, near side on)"));
    }

    [Fact]
    public async Task With_ore_in_view_the_pass_clears_the_ore_then_sweeps_the_stone()
    {
        var rig = Build(Grey(Crystal));

        await FirstRead(rig);                // read: ClearAt with the ore only
        var clear = Assert.Single(rig.Macros.ClearAts);
        Assert.NotEmpty(clear.Points);
        Assert.All(clear.Points, p => Assert.StartsWith("ore ", p.Label));
        Assert.Empty(rig.Macros.Sweeps);

        await Tick(rig);                     // ClearAt finished: the sweep starts
        Assert.Equal(new[] { "id-on", "id-off", ScriptedMacros.ClearAtId, ScriptedMacros.SweepId }, rig.Macros.RunIds);
        Assert.Equal(PathFor(), Assert.Single(rig.Macros.Sweeps).Path);
        var ore = clear.Points.Count;
        Assert.Contains(rig.Log, l => l.EndsWith($"is the target: {ore} ore {(ore == 1 ? "point" : "points")}, then a sweep of {PathFor().Count} points"));
    }

    [Fact]
    public async Task With_no_ore_the_pass_sweeps_at_once()
    {
        var rig = Build(Grey());

        await FirstRead(rig);

        Assert.Empty(rig.Macros.ClearAts);
        Assert.Equal(new[] { "id-on", "id-off", ScriptedMacros.SweepId }, rig.Macros.RunIds);
        Assert.Contains(rig.Log, l => l.EndsWith($"is the target: 0 ore points, then a sweep of {PathFor().Count} points"));
    }

    [Fact]
    public async Task The_sweep_carries_the_account_s_dwell_the_block_size_the_measured_client_and_the_guard()
    {
        var rig = Build(Grey(), PulseFixtures.Config() with { SweepDwellMs = 650 });

        await FirstRead(rig);

        var req = Assert.Single(rig.Macros.Sweeps);
        Assert.Equal(("SweepPath", "42", new ClearAtClient(800, 599), 32, 650, WireDot),
            (req.Method, req.Target, req.Client, req.Step, req.DwellMs, req.Guard));
        Assert.Equal(req.Path[0], req.Path[^1]);
        Assert.Equal(new SweepPoint(422, 340), req.Path[0]);   // one block east of the character at 390,340
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_path_follows_the_account_s_near_side_setting(bool nearSide)
    {
        var rig = Build(Grey(), PulseFixtures.Config() with { SweepNearSide = nearSide });

        await FirstRead(rig);

        Assert.Equal(PathFor(nearSide), Assert.Single(rig.Macros.Sweeps).Path);
    }

    [Fact]
    public async Task A_sweep_that_changed_blocks_counts_as_progress_and_plans_the_next_pass()
    {
        var rig = Build(Grey());
        var path = PathFor();
        await FirstRead(rig);

        await Tick(rig);                     // the sweep finished: settle, Auto Mine stays off
        Assert.Equal(PulseState.Pausing, rig.Loop.State);
        Assert.Contains(rig.Log, l => l == $"swept {path.Count} points: reading again");

        rig.Reader.Frame = Grey((path[1].X - 4, path[1].Y - 4, 8, 8, PulseFixtures.Black),
                                (path[2].X - 4, path[2].Y - 4, 8, 8, PulseFixtures.Black));
        rig.Clock.Advance(1000);
        await Tick(rig);                     // read: judged, then the next pass sweeps

        Assert.Contains(rig.Log, l => l == $"the sweep changed 2 of {SweepChange.Points(path)} points");
        Assert.Equal(PulseState.Clearing, rig.Loop.State);
        Assert.Equal(2, rig.Macros.Sweeps.Count);
    }

    [Fact]
    public async Task A_sweep_that_broke_nothing_rides_a_burst()
    {
        var rig = Build(Grey());
        await FirstRead(rig);
        await Tick(rig);                     // the sweep finished: settle

        rig.Clock.Advance(1000);
        await Tick(rig);                     // read the same frame: nothing changed

        Assert.Equal(PulseState.Bursting, rig.Loop.State);
        Assert.Contains(rig.Log, l => l == $"the sweep broke nothing (0 of {SweepChange.Points(PathFor())} points changed): riding a burst");
        Assert.Single(rig.Macros.Sweeps);
        Assert.Equal("id-on", rig.Macros.RunIds.Last());
    }

    [Fact]
    public async Task Ore_cleared_before_the_sweep_counts_as_progress_whatever_the_sweep_did()
    {
        var rig = Build(Grey(Crystal));
        await FirstRead(rig);                // ClearAt started
        await Tick(rig);                     // ClearAt finished (cleared), the sweep started
        await Tick(rig);                     // the sweep finished

        Assert.Contains(rig.Log, l => l == $"cleared ore and swept {PathFor().Count} points: reading again");
        rig.Clock.Advance(1000);
        await Tick(rig);                     // the same frame: no judgement, straight to the next pass

        Assert.DoesNotContain(rig.Log, l => l.StartsWith("the sweep broke nothing"));
        Assert.Equal(2, rig.Macros.ClearAts.Count);
    }

    [Fact]
    public async Task Without_a_guard_stone_is_cleared_point_by_point_as_before()
    {
        var rig = Build(Grey(), ring: PulseFixtures.RingWithFinder());

        await FirstRead(rig);

        var req = Assert.Single(rig.Macros.ClearAts);
        Assert.Equal(13, req.Points.Count);
        Assert.Equal(new ClearAtPoint(390, 340, "stone 1"), req.Points[0]);
        Assert.Empty(rig.Macros.Sweeps);
    }

    [Fact]
    public async Task An_unread_block_size_never_sweeps()
    {
        var rig = Build(PulseFixtures.Calm());       // plain rock: no block size to read

        await FirstRead(rig);

        Assert.Equal(new ClearAtOutline(240, 240, 60, 225), Assert.Single(rig.Macros.ClearAts).Outline);
        Assert.Empty(rig.Macros.Sweeps);
    }

    [Fact]
    public async Task Never_sweeps_on_the_ride_down()
    {
        var rig = Build(Blocks(PulseFixtures.Navy));  // the top layer: above the target

        await FirstRead(rig);

        Assert.Equal(PulseState.Riding, rig.Loop.State);
        Assert.Empty(rig.Macros.Sweeps);
        Assert.Empty(rig.Macros.ClearAts);
    }

    [Fact]
    public async Task Never_sweeps_past_the_target()
    {
        var rig = Build(Grey(), PulseFixtures.Config(target: 2), PulseFixtures.RingWithSweep("black"));

        await FirstRead(rig);

        Assert.Equal(PulseState.GoingToTop, rig.Loop.State);
        Assert.Empty(rig.Macros.Sweeps);
    }

    [Fact]
    public async Task A_sweep_stopped_at_its_guard_stops_the_loop_with_Auto_Mine_off()
    {
        var rig = Build(Grey());
        rig.Macros.Script(ScriptedMacros.SweepId, ScriptedMacros.CheckFailed);
        await FirstRead(rig);

        await Tick(rig);

        Assert.Equal(PulseState.Stopped, rig.Loop.State);
        Assert.StartsWith($"'SweepPath ({PathFor().Count} points)' stopped at its check", rig.Loop.StopReason);
        Assert.EndsWith("Auto Mine is off.", rig.Loop.StopReason);
    }

    [Fact]
    public async Task An_Ur_Task_without_SweepPath_stops_the_loop_and_says_so()
    {
        var rig = Build(Grey());
        rig.Macros.SweepReplies.Enqueue(new RunMacroResponse(false, null, false, BridgeReasons.Refused, "Unknown method 'SweepPath'."));

        await FirstRead(rig);

        Assert.Equal(PulseState.Stopped, rig.Loop.State);
        Assert.Contains("Unknown method 'SweepPath'.", rig.Loop.StopReason);
        Assert.EndsWith("Auto Mine is off.", rig.Loop.StopReason);
    }

    [Fact]
    public async Task A_block_size_Ur_Task_would_refuse_never_sweeps()
    {
        // Ur Task refuses a step outside 8..240. A read over 240 cannot happen on a frame (the read
        // square holds at most ~87 measured px three times over), but a capture three times the
        // measured client reads its smallest block, 21 frame px, as 7 measured px: read, not a guess
        // (a finder of 12 px blocks puts the under-half cut at 6), and still too small to sweep.
        var px = Frames.Grid(2400, 1797, 21, 21, PulseFixtures.Grey, Seam);
        var ring = PulseFixtures.Ring() with
        {
            Finders = new[] { PulseFixtures.Finder() with { Pitch = 12, Guard = PulseFixtures.Dot } },
        };
        var rig = Build(new FramePixels(2400, 1797, px), ring: ring);

        await FirstRead(rig);

        Assert.Contains(rig.Log, l => l == "block size 7 px (read from the frame)");
        Assert.NotEmpty(Assert.Single(rig.Macros.ClearAts).Points);
        Assert.Empty(rig.Macros.Sweeps);
    }
}
