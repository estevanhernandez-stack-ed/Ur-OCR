using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

/// <summary>
/// "Can't see the mine" (live 2026-09-30): a mine reset with the character at the top dropped it at
/// the bottom of the full mine, in the dark. The pulse rode burst after burst on "no layer" reads and
/// then took "black (4% of the area)" as the target. Now a read under DarkShareFloorPct is a dark
/// read, never a target pass, and DarkStreakLimit dark reads in a row go to top.
/// </summary>
public class PulseLoopDarkTests
{
    private sealed record Rig(PulseLoop Loop, ScriptedMacros Macros, ScriptedReader Reader, PulseClock Clock, List<string> Log);

    private static Rig Build(FramePixels frame)
    {
        var macros = new ScriptedMacros();
        var reader = new ScriptedReader { Next = ScriptedReader.All(PulseFixtures.Grey), Frame = frame };
        var clock = new PulseClock();
        var log = new List<string>();
        var loop = new PulseLoop(PulseFixtures.Config(), new[] { PulseFixtures.RingWithFinder() },
            PulseFixtures.Spots(), reader, macros, clock, log.Add);
        return new Rig(loop, macros, reader, clock, log);
    }

    private static Task Tick(Rig r) => r.Loop.TickAsync(true, 7, CancellationToken.None);

    /// <summary>From Riding or Bursting (2 s): Auto Mine on, ride, Auto Mine off, settle 1 s, read.</summary>
    private static async Task NextRead(Rig r)
    {
        await Tick(r);
        await Tick(r);
        r.Clock.Advance(2000);
        await Tick(r);
        await Tick(r);
        r.Clock.Advance(1000);
        await Tick(r);
    }

    /// <summary>A dark frame (nothing near a listed colour) with a 250 px grey band of the given height
    /// below the character: 12 px reads as 4% of the area, 20 px as 7%, both with no block size read.</summary>
    private static FramePixels Dark(int greyBand = 0)
    {
        var px = Frames.Solid(800, 599, new Rgb(60, 0, 0));
        if (greyBand > 0) Frames.Fill(px, 800, 290, 380, 250, greyBand, PulseFixtures.Grey);
        return new FramePixels(800, 599, px);
    }

    private static int GoToTops(Rig r) => r.Macros.RunIds.Count(id => id == "id-top");

    [Fact]
    public async Task Four_dark_reads_in_a_row_go_to_top()
    {
        var rig = Build(Dark());

        for (var i = 0; i < 3; i++)
        {
            await NextRead(rig);
            Assert.Equal(PulseState.Bursting, rig.Loop.State);
        }
        Assert.Equal(0, GoToTops(rig));

        await NextRead(rig);

        Assert.Equal(1, GoToTops(rig));
        Assert.Contains(rig.Log, l => l == "can't see the mine (4 dark reads in a row): going to top");
        Assert.Equal(3, rig.Log.Count(l => l.StartsWith("no layer on a calm frame")));
    }

    [Fact]
    public async Task Three_dark_reads_then_a_good_read_do_not_go_to_top()
    {
        var rig = Build(Dark());
        for (var i = 0; i < 3; i++) await NextRead(rig);

        rig.Reader.Frame = PulseFixtures.Calm();
        await NextRead(rig);
        Assert.Equal(PulseState.Clearing, rig.Loop.State);

        // The streak starts over: three more dark reads still do not go to top.
        rig.Reader.Frame = Dark();
        await Tick(rig);                              // the ClearAt finishes: settle and read
        rig.Clock.Advance(1000);
        await Tick(rig);
        for (var i = 0; i < 2; i++) await NextRead(rig);

        Assert.Equal(0, GoToTops(rig));
        Assert.DoesNotContain(rig.Log, l => l.StartsWith("can't see the mine"));
    }

    [Fact]
    public async Task A_target_read_under_the_floor_does_not_start_a_pass_and_counts_as_dark()
    {
        var rig = Build(Dark(greyBand: 12));

        await NextRead(rig);

        Assert.Equal(PulseState.Bursting, rig.Loop.State);
        Assert.Empty(rig.Macros.ClearAts);
        Assert.Contains(rig.Log, l => l == "layer grey (4% of the area) is under the 5% floor: riding a burst");
        Assert.DoesNotContain(rig.Log, l => l.Contains("is the target"));

        for (var i = 0; i < 3; i++) await NextRead(rig);

        Assert.Equal(1, GoToTops(rig));
        Assert.Contains(rig.Log, l => l == "can't see the mine (4 dark reads in a row): going to top");
        Assert.Empty(rig.Macros.ClearAts);
    }

    [Fact]
    public async Task A_target_read_over_the_floor_starts_a_pass_as_before()
    {
        var rig = Build(Dark(greyBand: 20));

        await NextRead(rig);

        Assert.Equal(PulseState.Clearing, rig.Loop.State);
        Assert.Single(rig.Macros.ClearAts);
        Assert.Contains(rig.Log, l => l == "layer grey (7% of the area, next navy 0%) is the target: clearing at 13 points (0 ore, 13 stone)");
        Assert.DoesNotContain(rig.Log, l => l.Contains("floor"));
    }

    [Fact]
    public async Task The_streak_starts_over_after_Go_to_Top()
    {
        var rig = Build(Dark());
        for (var i = 0; i < 4; i++) await NextRead(rig);
        Assert.Equal(1, GoToTops(rig));

        await Tick(rig);                              // Go to Top finishes: riding again
        Assert.Equal(PulseState.Riding, rig.Loop.State);
        for (var i = 0; i < 3; i++) await NextRead(rig);
        Assert.Equal(1, GoToTops(rig));               // three dark reads since: not yet

        await NextRead(rig);
        Assert.Equal(2, GoToTops(rig));
        Assert.Equal(2, rig.Log.Count(l => l == "can't see the mine (4 dark reads in a row): going to top"));
    }
}
