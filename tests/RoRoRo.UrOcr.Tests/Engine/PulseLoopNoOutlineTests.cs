using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

/// <summary>
/// The no-outline memory (ore-stop pulse, fix 1): a finished ClearAt names the points that showed no
/// outline, and the next passes leave those spots out until the pulse rides or PulseLoop.NoOutlinePasses
/// passes go by. The rig is the sweep rig (32 px blocks, a guard), with two crystals in reach: one
/// right of the character (the "east" points) and one left of it.
/// </summary>
public class PulseLoopNoOutlineTests
{
    private sealed record Rig(PulseLoop Loop, ScriptedMacros Macros, ScriptedReader Reader, PulseClock Clock, List<string> Log);

    private static readonly Rgb Seam = new(225, 230, 240);

    private static readonly (int X, int Y, int W, int H, Rgb Colour) East = (425, 329, 24, 24, PulseFixtures.Cyan);
    private static readonly (int X, int Y, int W, int H, Rgb Colour) West = (329, 329, 24, 24, PulseFixtures.Cyan);

    private static FramePixels Grey(params (int X, int Y, int W, int H, Rgb Colour)[] paint) =>
        Blocks(PulseFixtures.Grey, paint);

    private static FramePixels Blocks(Rgb block, params (int X, int Y, int W, int H, Rgb Colour)[] paint)
    {
        var px = Frames.Grid(800, 599, 32, 32, block, Seam);
        foreach (var (x, y, w, h, c) in paint) Frames.Fill(px, 800, x, y, w, h, c);
        return new FramePixels(800, 599, px);
    }

    private static Rig Build(FramePixels frame)
    {
        var macros = new ScriptedMacros();
        var reader = new ScriptedReader { Next = ScriptedReader.All(PulseFixtures.Grey), Frame = frame };
        var clock = new PulseClock();
        var log = new List<string>();
        var loop = new PulseLoop(PulseFixtures.Config(), new[] { PulseFixtures.RingWithSweep() },
            PulseFixtures.Spots(), reader, macros, clock, log.Add);
        return new Rig(loop, macros, reader, clock, log);
    }

    private static Task Tick(Rig r) => r.Loop.TickAsync(true, 7, CancellationToken.None);

    /// <summary>Auto Mine on, ride, Auto Mine off, settle, read: the pass's first call started.</summary>
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

    /// <summary>From a started ClearAt: it finishes, the sweep runs, a settle, and the next read plans
    /// the next pass (its ClearAt started, or its sweep when every ore point was left out).</summary>
    private static async Task NextPass(Rig r)
    {
        await Tick(r);                  // ClearAt finished: the sweep starts
        await Tick(r);                  // the sweep finished: settle, Auto Mine stays off
        r.Clock.Advance(1000);
        await Tick(r);                  // read: the next pass
    }

    private static GetPlaybackResponse FinishedNoOutline(params int[] points) => new(true, "finished", null, null, null, points);

    /// <summary>1-based indices of the request's points that <paramref name="pick"/> chooses.</summary>
    private static int[] Indices(ClearAtRequest req, Func<ClearAtPoint, bool> pick) =>
        req.Points.Select((p, i) => (p, i)).Where(x => pick(x.p)).Select(x => x.i + 1).ToArray();

    private static bool IsEast(ClearAtPoint p) => p.X > 400;

    private static string SkipLine(int n) =>
        $"skipping {n} ore {(n == 1 ? "point" : "points")} that showed no outline on a recent pass";

    [Fact]
    public async Task Points_that_showed_no_outline_are_left_out_of_the_next_ClearAt()
    {
        var rig = Build(Grey(East, West));
        await FirstRead(rig);
        var first = Assert.Single(rig.Macros.ClearAts);
        var east = Indices(first, IsEast);
        Assert.NotEmpty(east);
        Assert.True(east.Length < first.Points.Count);   // the west crystal has points too
        rig.Macros.Script(ScriptedMacros.ClearAtId, FinishedNoOutline(east));

        await NextPass(rig);

        var second = rig.Macros.ClearAts[1];
        Assert.DoesNotContain(second.Points, IsEast);
        Assert.Equal(first.Points.Count - east.Length, second.Points.Count);
        Assert.Contains(SkipLine(east.Length), rig.Log);
        var ore = second.Points.Count;
        Assert.Contains(rig.Log, l => l.EndsWith($"is the target: {ore} ore {(ore == 1 ? "point" : "points")}, then a sweep of {rig.Macros.Sweeps[0].Path.Count} points"));
    }

    [Fact]
    public async Task When_every_ore_point_is_left_out_the_pass_goes_straight_to_the_sweep()
    {
        var rig = Build(Grey(East));
        await FirstRead(rig);
        var first = Assert.Single(rig.Macros.ClearAts);
        rig.Macros.Script(ScriptedMacros.ClearAtId, FinishedNoOutline(Indices(first, _ => true)));

        await NextPass(rig);

        Assert.Single(rig.Macros.ClearAts);
        Assert.Equal(2, rig.Macros.Sweeps.Count);
        Assert.Equal(ScriptedMacros.SweepId, rig.Macros.RunIds.Last());
        Assert.Contains(SkipLine(first.Points.Count), rig.Log);
    }

    [Fact]
    public async Task A_remembered_spot_comes_back_after_its_passes_run_out()
    {
        var rig = Build(Grey(East, West));
        await FirstRead(rig);
        var first = Assert.Single(rig.Macros.ClearAts);
        rig.Macros.Script(ScriptedMacros.ClearAtId, FinishedNoOutline(Indices(first, IsEast)));
        await NextPass(rig);                                  // pass 2: the east points left out
        rig.Macros.Script(ScriptedMacros.ClearAtId, FinishedNoOutline());

        for (var pass = 3; pass <= PulseLoop.NoOutlinePasses + 2; pass++) await NextPass(rig);

        var passes = rig.Macros.ClearAts;
        Assert.Equal(PulseLoop.NoOutlinePasses + 2, passes.Count);
        for (var i = 1; i <= PulseLoop.NoOutlinePasses; i++) Assert.DoesNotContain(passes[i].Points, IsEast);
        Assert.Equal(first.Points, passes[^1].Points);        // one pass later, the east spot is tried again
        Assert.Equal(PulseLoop.NoOutlinePasses, rig.Log.Count(l => l.StartsWith("skipping ")));
    }

    [Fact]
    public async Task A_burst_forgets_every_remembered_spot()
    {
        var rig = Build(Grey(East));
        await FirstRead(rig);
        var first = Assert.Single(rig.Macros.ClearAts);
        rig.Macros.Script(ScriptedMacros.ClearAtId,
            new GetPlaybackResponse(true, "finished", "skipped", null, null, Indices(first, _ => true)));
        await Tick(rig);                                      // ClearAt skipped every point: the sweep starts
        await Tick(rig);                                      // the sweep finished
        rig.Clock.Advance(1000);
        await Tick(rig);                                      // the same frame: the sweep broke nothing, a burst
        Assert.Equal(PulseState.Bursting, rig.Loop.State);

        for (var i = 0; i < 20 && rig.Macros.ClearAts.Count < 2; i++)
        {
            rig.Clock.Advance(1000);
            await Tick(rig);
        }

        Assert.Equal(first.Points, rig.Macros.ClearAts[1].Points);
        Assert.DoesNotContain(rig.Log, l => l.StartsWith("skipping "));
    }

    [Fact]
    public async Task Riding_on_to_another_layer_forgets_every_remembered_spot()
    {
        var rig = Build(Grey(East, West));
        await FirstRead(rig);
        var first = Assert.Single(rig.Macros.ClearAts);
        rig.Macros.Script(ScriptedMacros.ClearAtId, FinishedNoOutline(Indices(first, IsEast)));
        rig.Reader.Frame = Blocks(PulseFixtures.Navy);        // the next read: above the target

        await NextPass(rig);
        Assert.Equal(PulseState.Riding, rig.Loop.State);
        rig.Reader.Frame = Grey(East, West);
        for (var i = 0; i < 20 && rig.Macros.ClearAts.Count < 2; i++)
        {
            rig.Clock.Advance(1000);
            await Tick(rig);
        }

        Assert.Equal(first.Points, rig.Macros.ClearAts[1].Points);
        Assert.DoesNotContain(rig.Log, l => l.StartsWith("skipping "));
    }

    [Fact]
    public async Task An_older_Ur_Task_that_names_no_points_leaves_nothing_out()
    {
        var rig = Build(Grey(East, West));
        await FirstRead(rig);
        var first = Assert.Single(rig.Macros.ClearAts);   // unscripted: finished, no noOutline

        await NextPass(rig);

        Assert.Equal(first.Points, rig.Macros.ClearAts[1].Points);
        Assert.DoesNotContain(rig.Log, l => l.StartsWith("skipping "));
    }
}
