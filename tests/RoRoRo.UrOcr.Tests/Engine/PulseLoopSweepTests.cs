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

    /// <summary>A size-by-size mark on each of the first <paramref name="n"/> swept blocks (the start
    /// left out), so the next read counts them changed.</summary>
    private static (int X, int Y, int W, int H, Rgb Colour)[] Marks(IReadOnlyList<SweepPoint> path, int n, int size = 8) =>
        path.Skip(1).Distinct().Take(n).Select(p => (p.X - size / 2, p.Y - size / 2, size, size, PulseFixtures.Black)).ToArray();

    /// <summary>More swept blocks than noise (SweepChange.NoiseShare): a quarter of them.</summary>
    private static int Real(IReadOnlyList<SweepPoint> path) => SweepChange.Points(path) / 4;

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

        rig.Reader.Frame = Grey(Marks(path, Real(path)));
        rig.Clock.Advance(1000);
        await Tick(rig);                     // read: judged, then the next pass sweeps

        Assert.Contains(rig.Log, l => l == $"the sweep changed {Real(path)} of {SweepChange.Points(path)} points");
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
    public async Task A_sweep_that_changed_only_noise_broke_nothing()
    {
        // Live 2026-09-30: on the mine floor sparkles and popups moved 3 of 43 boxes every pass, which
        // counted as progress and held the pulse there for twelve minutes.
        var rig = Build(Grey());
        var path = PathFor();
        await FirstRead(rig);
        await Tick(rig);                     // the sweep finished: settle

        var noise = (int)(SweepChange.Points(path) * SweepChange.NoiseShare);
        rig.Reader.Frame = Grey(Marks(path, noise));
        rig.Clock.Advance(1000);
        await Tick(rig);

        Assert.Equal(PulseState.Bursting, rig.Loop.State);
        Assert.Contains(rig.Log, l => l == $"the sweep broke nothing ({noise} of {SweepChange.Points(path)} points changed): riding a burst");
        Assert.Single(rig.Macros.Sweeps);
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

    /// <summary>The sweep the loop plans on the account's own block size: the fixture finder at that size.</summary>
    private static IReadOnlyList<SweepPoint> PathAt(int px, bool nearSide = true) =>
        SweepPath.Build(PulseFixtures.Finder() with { Pitch = px, Guard = PulseFixtures.Dot }, nearSide);

    [Fact]
    public async Task Starts_saying_the_account_s_block_size()
    {
        var rig = Build(Grey(), PulseFixtures.Config() with { SweepBlockPx = 160 });

        await Tick(rig);

        Assert.Contains(rig.Log, l => l.StartsWith("started")
            && l.EndsWith("sweeping stone (400 ms a point, near side on, 160 px blocks without a read)"));
    }

    [Fact]
    public async Task An_unread_block_size_sweeps_at_the_account_s_block_size()
    {
        var rig = Build(PulseFixtures.Calm(), PulseFixtures.Config() with { SweepBlockPx = 160 });

        await FirstRead(rig);

        var req = Assert.Single(rig.Macros.Sweeps);
        Assert.Equal(160, req.Step);
        Assert.Equal(PathAt(160), req.Path);
        Assert.Null(SweepPathTests.UrTaskRefusal(req.Path, req.Step, 800, 599));
        Assert.Empty(rig.Macros.ClearAts);                       // no ore: straight to the sweep
        Assert.Equal(new[] { "block size 50 px (layer default; no clear pattern)", "block size 160 px (account setting)" },
            rig.Log.Where(l => l.StartsWith("block size")));
        Assert.Contains(rig.Log, l => l.EndsWith($"is the target: 0 ore points, then a sweep of {PathAt(160).Count} points"));
    }

    [Fact]
    public async Task A_block_size_read_near_the_account_s_wins_over_it()
    {
        var rig = Build(Grey(), PulseFixtures.Config() with { SweepBlockPx = 36 });

        await FirstRead(rig);

        var req = Assert.Single(rig.Macros.Sweeps);
        Assert.Equal(32, req.Step);
        Assert.Equal(PathFor(), req.Path);
        Assert.DoesNotContain(rig.Log, l => l.Contains("account setting"));
    }

    [Fact]
    public async Task A_block_size_read_far_from_the_account_s_sweeps_at_the_account_s()
    {
        var rig = Build(Grey(), PulseFixtures.Config() with { SweepBlockPx = 110 });

        await FirstRead(rig);

        var req = Assert.Single(rig.Macros.Sweeps);
        Assert.Equal(110, req.Step);
        Assert.Equal(PathAt(110), req.Path);
        Assert.Null(SweepPathTests.UrTaskRefusal(req.Path, req.Step, 800, 599));
        Assert.Equal(new[] { "block size 110 px (account setting; read 32 px is far from it)" },
            rig.Log.Where(l => l.StartsWith("block size")));
    }

    [Fact]
    public async Task A_read_Ur_Task_would_refuse_sweeps_at_the_account_s_block_size()
    {
        // The 7 px read of A_block_size_Ur_Task_would_refuse_never_sweeps, with the account set to 20:
        // far from it, so the pass (finder and sweep) takes 20.
        var px = Frames.Grid(2400, 1797, 21, 21, PulseFixtures.Grey, Seam);
        var ring = PulseFixtures.Ring() with
        {
            Finders = new[] { PulseFixtures.Finder() with { Pitch = 12, Guard = PulseFixtures.Dot } },
        };
        var rig = Build(new FramePixels(2400, 1797, px), PulseFixtures.Config() with { SweepBlockPx = 20 }, ring);

        await FirstRead(rig);

        Assert.Equal(new[] { "block size 20 px (account setting; read 7 px is far from it)" },
            rig.Log.Where(l => l.StartsWith("block size")));
        var req = Assert.Single(rig.Macros.Sweeps);
        Assert.Equal(20, req.Step);
        Assert.Null(SweepPathTests.UrTaskRefusal(req.Path, req.Step, 800, 599));
    }

    [Fact]
    public async Task The_account_s_block_size_is_logged_once_while_it_is_in_use()
    {
        var rig = Build(PulseFixtures.Calm(), PulseFixtures.Config() with { SweepBlockPx = 160 });
        var path = PathAt(160);
        await FirstRead(rig);
        await Tick(rig);                     // the sweep finished: settle

        rig.Reader.Frame = PulseFixtures.Calm(Marks(path, Real(path), 60));
        rig.Clock.Advance(1000);
        await Tick(rig);                     // judged, then the next pass sweeps at 160 again

        Assert.Equal(2, rig.Macros.Sweeps.Count);
        Assert.Single(rig.Log, l => l == "block size 160 px (account setting)");
    }

    [Fact]
    public async Task A_sweep_at_the_account_s_block_size_is_judged_at_that_size()
    {
        // SweepChange's box is an eighth of the step each way: 20 px at 160, so an 8 px mark at a
        // swept point is too small to count, and a 60 px one counts.
        var rig = Build(PulseFixtures.Calm(), PulseFixtures.Config() with { SweepBlockPx = 160 });
        var path = PathAt(160);
        await FirstRead(rig);
        await Tick(rig);                     // the sweep finished: settle

        var big = Real(path);
        var small = Marks(path, big + 1)[big];   // one more swept block, marked too small to count
        rig.Reader.Frame = PulseFixtures.Calm(Marks(path, big, 60).Append(small).ToArray());
        rig.Clock.Advance(1000);
        await Tick(rig);

        Assert.Contains(rig.Log, l => l == $"the sweep changed {big} of {SweepChange.Points(path)} points");
    }

    [Fact]
    public async Task When_no_path_fits_at_the_account_s_block_size_stone_is_cleared_point_by_point()
    {
        // The character near the right edge: at 160 px the start block east of it is off the client.
        var ring = PulseFixtures.Ring() with
        {
            Finders = new[] { PulseFixtures.Finder() with { CenterX = 700, Guard = PulseFixtures.Dot } },
        };
        var rig = Build(PulseFixtures.Calm(), PulseFixtures.Config() with { SweepBlockPx = 160 }, ring);

        await FirstRead(rig);

        Assert.Empty(rig.Macros.Sweeps);
        Assert.NotEmpty(Assert.Single(rig.Macros.ClearAts).Points);
        Assert.Contains(rig.Log, l => l == "no sweep path fits at 160 px blocks (account setting): clearing stone point by point");
    }

    [Fact]
    public async Task When_no_path_fits_at_the_account_s_block_size_over_a_far_read_it_names_the_setting()
    {
        var ring = PulseFixtures.Ring() with
        {
            Finders = new[] { PulseFixtures.Finder() with { CenterX = 700, Guard = PulseFixtures.Dot } },
        };
        var rig = Build(Grey(), PulseFixtures.Config() with { SweepBlockPx = 160 }, ring);

        await FirstRead(rig);

        Assert.Empty(rig.Macros.Sweeps);
        Assert.Contains(rig.Log, l => l == "no sweep path fits at 160 px blocks (account setting): clearing stone point by point");
    }
}
