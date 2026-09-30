using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

/// <summary>
/// The ore sweep (live 2026-09-30: 24 ore points through ClearAt took 35 s though most broke in one
/// 0.3 s hold). On a sweep pass with 2 or more ore points, the ore goes in one held free-path drag,
/// then the stone sweep, then a read. Ore swept on one pass and found again on the next goes to
/// ClearAt, which holds until it breaks. Every unscripted playback finishes on its first poll.
/// </summary>
public class PulseLoopOreSweepTests
{
    private sealed record Rig(PulseLoop Loop, ScriptedMacros Macros, ScriptedReader Reader, PulseClock Clock, List<string> Log);

    private static readonly Rgb Seam = new(225, 230, 240);

    /// <summary>Cyan crystals in the 32 px blocks east and west of the character at 390,340: the move
    /// between them runs straight through the centre block.</summary>
    private static readonly (int X, int Y, int W, int H, Rgb Colour) East = (425, 329, 24, 24, PulseFixtures.Cyan);
    private static readonly (int X, int Y, int W, int H, Rgb Colour) West = (331, 329, 24, 24, PulseFixtures.Cyan);

    private static FramePixels Grey(params (int X, int Y, int W, int H, Rgb Colour)[] paint)
    {
        var px = Frames.Grid(800, 599, 32, 32, PulseFixtures.Grey, Seam);
        foreach (var (x, y, w, h, c) in paint) Frames.Fill(px, 800, x, y, w, h, c);
        return new FramePixels(800, 599, px);
    }

    private static IReadOnlyList<SweepPoint> StonePath() =>
        SweepPath.Build(PulseFixtures.Finder() with { Pitch = 32, Guard = PulseFixtures.Dot }, true);

    private static Rig Build(FramePixels frame, PulseConfig? config = null)
    {
        var macros = new ScriptedMacros();
        var reader = new ScriptedReader { Next = ScriptedReader.All(PulseFixtures.Grey), Frame = frame };
        var clock = new PulseClock();
        var log = new List<string>();
        var loop = new PulseLoop(config ?? PulseFixtures.Config() with { OreSweepDwellMs = 250 },
            new[] { PulseFixtures.RingWithSweep() }, PulseFixtures.Spots(), reader, macros, clock, log.Add);
        return new Rig(loop, macros, reader, clock, log);
    }

    private static Task Tick(Rig r) => r.Loop.TickAsync(true, 7, CancellationToken.None);

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

    /// <summary>The pass's calls finish one a tick; then the settle and the next read.</summary>
    private static async Task FinishPassAndRead(Rig r, FramePixels next)
    {
        for (var i = 0; i < 4 && r.Loop.State == PulseState.Clearing; i++) await Tick(r);
        Assert.Equal(PulseState.Pausing, r.Loop.State);
        r.Reader.Frame = next;
        r.Clock.Advance(1000);
        await Tick(r);
    }

    private static bool Near(SweepPoint p, (int X, int Y, int W, int H, Rgb Colour) crystal) =>
        p.X >= crystal.X - 8 && p.X < crystal.X + crystal.W + 8 && p.Y >= crystal.Y - 8 && p.Y < crystal.Y + crystal.H + 8;

    private static bool Near(ClearAtPoint p, (int X, int Y, int W, int H, Rgb Colour) crystal) => Near(new SweepPoint(p.X, p.Y), crystal);

    /// <summary>Marks on a quarter of the stone blocks the stone sweep went over, clear of the crystals,
    /// so the next read counts more change than noise (SweepChange.NoiseShare).</summary>
    private static (int X, int Y, int W, int H, Rgb Colour)[] BrokenStone(params (int X, int Y, int W, int H, Rgb Colour)[] keep)
    {
        var path = StonePath();
        return path.Skip(1).Where(p => !keep.Any(c => Near(p, c))).Take(path.Count / 4)
            .Select(p => (p.X - 4, p.Y - 4, 8, 8, PulseFixtures.Black)).ToArray();
    }

    [Fact]
    public async Task Two_or_more_ore_points_are_swept_in_one_free_path_then_the_stone()
    {
        var rig = Build(Grey(East, West));

        await FirstRead(rig);                // the ore sweep started
        var ore = Assert.Single(rig.Macros.Sweeps);
        Assert.True(ore.FreePath);
        Assert.Equal((0, 250), (ore.Step, ore.DwellMs));
        Assert.Equal(ore.Path[0], ore.Path[^1]);
        Assert.True(Near(ore.Path[0], East), $"starts at {ore.Path[0]}, the ore nearest the stone sweep's start");
        Assert.Contains(ore.Path, p => Near(p, West));
        Assert.Null(OreSweepPathTests.UrTaskFreePathRefusal(ore.Path, 800, 599));
        for (var k = 1; k < ore.Path.Count; k++)
            Assert.False(SweepPath.CrossesCentre(((ore.Path[k - 1].X - 390) / 32.0, (ore.Path[k - 1].Y - 340) / 32.0),
                ((ore.Path[k].X - 390) / 32.0, (ore.Path[k].Y - 340) / 32.0)));

        await Tick(rig);                     // the stone sweep, as before
        Assert.Equal(2, rig.Macros.Sweeps.Count);
        var stone = rig.Macros.Sweeps[1];
        Assert.Null(stone.FreePath);
        Assert.Equal((32, 400), (stone.Step, stone.DwellMs));
        Assert.Equal(StonePath(), stone.Path);
        Assert.Empty(rig.Macros.ClearAts);
        Assert.Equal(new[] { "id-on", "id-off", ScriptedMacros.SweepId, ScriptedMacros.SweepId }, rig.Macros.RunIds);

        var swept = ore.Path.Count(p => Near(p, East) || Near(p, West)) - 1;   // the closing return is the first again
        var at = rig.Log.IndexOf($"ore sweep of {swept} points");
        Assert.True(at > 0, "the ore sweep line is logged");
        Assert.EndsWith($"is the target: {swept} ore points, then a sweep of {StonePath().Count} points", rig.Log[at - 1]);
        Assert.DoesNotContain(rig.Log, l => l.StartsWith("ore spots surviving"));
    }

    [Fact]
    public async Task One_ore_point_goes_to_ClearAt_as_before()
    {
        var rig = Build(Grey((425, 329, 24, 24, PulseFixtures.Cyan)));

        await FirstRead(rig);

        var clear = Assert.Single(rig.Macros.ClearAts);
        Assert.Single(clear.Points);
        Assert.Empty(rig.Macros.Sweeps);
        Assert.DoesNotContain(rig.Log, l => l.StartsWith("ore sweep"));
        await Tick(rig);
        Assert.Null(Assert.Single(rig.Macros.Sweeps).FreePath);
    }

    [Fact]
    public async Task Ore_still_there_after_its_sweep_goes_to_ClearAt_on_the_next_pass()
    {
        var rig = Build(Grey(East, West));
        await FirstRead(rig);
        var swept = rig.Macros.Sweeps[0].Path.Count(p => Near(p, East) || Near(p, West)) - 1;

        await FinishPassAndRead(rig, Grey(new[] { East, West }.Concat(BrokenStone(East, West)).ToArray()));   // the stone changed, the ore did not

        var clear = Assert.Single(rig.Macros.ClearAts);
        Assert.Equal(swept, clear.Points.Count);
        Assert.All(clear.Points, p => Assert.True(Near(p, East) || Near(p, West)));
        Assert.Contains($"ore spots surviving a sweep go to ClearAt: {swept}", rig.Log);
        Assert.Equal(2, rig.Macros.Sweeps.Count);                     // no ore sweep this pass
        Assert.Equal(ScriptedMacros.ClearAtId, rig.Macros.RunIds.Last());
    }

    [Fact]
    public async Task Ore_that_broke_is_not_found_again_and_new_ore_is_swept()
    {
        // East broke; West survived; two new crystals appeared north: West to ClearAt, the new two swept.
        var rig = Build(Grey(East, West));
        await FirstRead(rig);

        var north1 = (393, 265, 24, 24, PulseFixtures.Cyan);
        var north2 = (425, 265, 24, 24, PulseFixtures.Cyan);
        await FinishPassAndRead(rig, Grey(new[] { West, north1, north2 }.Concat(BrokenStone(East, West, north1, north2)).ToArray()));

        var oreSweep = rig.Macros.Sweeps[2];
        Assert.True(oreSweep.FreePath);
        Assert.Contains(oreSweep.Path, p => Near(p, north1));
        Assert.Contains(oreSweep.Path, p => Near(p, north2));
        Assert.DoesNotContain(oreSweep.Path, p => Near(p, West));
        await Tick(rig);                                              // then ClearAt for the survivor
        Assert.All(Assert.Single(rig.Macros.ClearAts).Points, p => Assert.True(Near(p, West)));
    }

    [Fact]
    public async Task The_survivor_memory_is_wiped_by_a_ride()
    {
        var rig = Build(Grey(East, West));
        await FirstRead(rig);

        await FinishPassAndRead(rig, Grey(East, West));               // nothing changed anywhere: a burst
        Assert.Equal(PulseState.Bursting, rig.Loop.State);
        await Tick(rig);                                              // Auto Mine on ends
        rig.Clock.Advance(2000);
        await Tick(rig);
        await Tick(rig);
        rig.Clock.Advance(1000);
        await Tick(rig);                                              // read after the ride

        Assert.Empty(rig.Macros.ClearAts);
        Assert.Equal(3, rig.Macros.Sweeps.Count);
        Assert.True(rig.Macros.Sweeps[2].FreePath);                   // swept again, not sent to ClearAt
    }

    [Fact]
    public async Task The_next_read_judges_the_ore_sweep_too()
    {
        var rig = Build(Grey(East, West));
        await FirstRead(rig);
        var ore = rig.Macros.Sweeps[0].Path;
        var west = ore.First(p => Near(p, West));

        await FinishPassAndRead(rig, Grey(new[] { East, (west.X - 6, west.Y - 6, 12, 12, PulseFixtures.Black) }.Concat(BrokenStone(East, West)).ToArray()));

        var points = SweepChange.Points(StonePath().Concat(ore).ToList());
        Assert.DoesNotContain(rig.Log, l => l.StartsWith("the sweep broke nothing"));
        Assert.Contains(rig.Log, l => l.StartsWith("the sweep changed ") && l.EndsWith($" of {points} points"));
    }

    [Fact]
    public async Task An_Ur_Task_that_refuses_free_paths_clears_the_ore_through_ClearAt_and_says_so_once()
    {
        var rig = Build(Grey(East, West));
        rig.Macros.SweepReplies.Enqueue(ScriptedMacros.Refusal(BridgeReasons.Refused));

        await FirstRead(rig);                // the ore sweep refused, straight to ClearAt with every ore point

        Assert.NotEqual(PulseState.Stopped, rig.Loop.State);
        var clear = Assert.Single(rig.Macros.ClearAts);
        Assert.Contains(clear.Points, p => Near(p, East));
        Assert.Contains(clear.Points, p => Near(p, West));
        Assert.Single(rig.Log, l => l.StartsWith("Ur Task refused the ore sweep"));
        await Tick(rig);
        Assert.Null(rig.Macros.Sweeps.Last().FreePath);             // the stone sweep still runs

        await Tick(rig);                     // settle, then the next pass: refused again, logged once
        rig.Macros.SweepReplies.Enqueue(ScriptedMacros.Refusal(BridgeReasons.Refused));
        rig.Reader.Frame = Grey(new[] { East, West }.Concat(BrokenStone(East, West)).ToArray());
        rig.Clock.Advance(1000);
        await Tick(rig);

        Assert.Equal(2, rig.Macros.ClearAts.Count);
        Assert.Single(rig.Log, l => l.StartsWith("Ur Task refused the ore sweep"));
    }

    [Fact]
    public async Task An_ore_sweep_stopped_at_its_guard_stops_the_loop()
    {
        var rig = Build(Grey(East, West));
        rig.Macros.Script(ScriptedMacros.SweepId, ScriptedMacros.CheckFailed);
        await FirstRead(rig);

        await Tick(rig);

        Assert.Equal(PulseState.Stopped, rig.Loop.State);
        Assert.StartsWith("'SweepPath (", rig.Loop.StopReason);
        Assert.Contains("stopped at its check", rig.Loop.StopReason);
        Assert.EndsWith("Auto Mine is off.", rig.Loop.StopReason);
        Assert.Single(rig.Macros.Sweeps);
    }

    [Fact]
    public async Task Ur_Task_not_running_still_stops_the_loop()
    {
        var rig = Build(Grey(East, West));
        rig.Macros.SweepReplies.Enqueue(ScriptedMacros.Refusal(BridgeReasons.NotRunning));

        await FirstRead(rig);

        Assert.Equal(PulseState.Stopped, rig.Loop.State);
        Assert.Empty(rig.Macros.ClearAts);
    }
}
