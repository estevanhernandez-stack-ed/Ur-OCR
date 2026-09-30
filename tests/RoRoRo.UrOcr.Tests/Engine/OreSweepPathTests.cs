using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Ipc;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

/// <summary>The ore sweep's path (live 2026-09-30): the pass's ore points in one held drag, from the ore
/// point nearest the stone sweep's start block, nearest neighbour after that, and back to the first.
/// Never on the centre block, and never a straight move through it.</summary>
public class OreSweepPathTests
{
    private static readonly Func<int, int, bool> NoHud = (_, _) => false;

    /// <summary>Open ground: a 2000x2000 client, the character at 1000,1000, 50 px blocks, the stone
    /// sweep starting one block east.</summary>
    private static OreSweep Open(IReadOnlyList<(int X, int Y)> ore, Func<int, int, bool>? masked = null) =>
        SweepPath.Ore(ore, new SweepPoint(1050, 1000), 1000, 1000, 50, 2000, 2000, masked ?? NoHud);

    private static (double I, double J) Block(SweepPoint p) => ((p.X - 1000) / 50.0, (p.Y - 1000) / 50.0);

    /// <summary>Null when Ur Task's SweepPathMacro.Validate would take the path as a free path;
    /// otherwise the rule it breaks: 3 to 256 points, every point inside the client, no repeat, the
    /// end on the start. No lattice rule.</summary>
    internal static string? UrTaskFreePathRefusal(IReadOnlyList<SweepPoint> path, int clientW, int clientH)
    {
        if (path.Count is < 3 or > BridgeContract.MaxSweepPoints) return $"{path.Count} points";
        for (var i = 0; i < path.Count; i++)
        {
            var p = path[i];
            if (p.X < 0 || p.Y < 0 || p.X >= clientW || p.Y >= clientH) return $"point {i + 1} {p} is outside the client";
            if (i > 0 && path[i - 1] == p) return $"point {i + 1} repeats";
        }
        return path[^1] == path[0] ? null : $"ends at {path[^1]}, not the start {path[0]}";
    }

    private static void AssertNeverThroughTheCentre(IReadOnlyList<SweepPoint> path)
    {
        foreach (var p in path)
        {
            var (i, j) = Block(p);
            Assert.False(Math.Abs(i) <= 0.5 && Math.Abs(j) <= 0.5, $"{p} is on the centre block");
        }
        for (var k = 1; k < path.Count; k++)
            Assert.False(SweepPath.CrossesCentre(Block(path[k - 1]), Block(path[k])), $"{path[k - 1]} to {path[k]} crosses the centre");
    }

    [Fact]
    public void It_starts_at_the_ore_nearest_the_start_block_goes_nearest_neighbour_and_closes()
    {
        var ore = new[] { (1210, 1000), (1100, 1110), (1100, 1010) };

        var s = Open(ore);

        Assert.Equal(new[] { new SweepPoint(1100, 1010), new SweepPoint(1100, 1110), new SweepPoint(1210, 1000), new SweepPoint(1100, 1010) }, s.Path);
        Assert.Equal(new[] { 0, 1, 2 }, s.Swept.OrderBy(x => x));
        Assert.Null(UrTaskFreePathRefusal(s.Path, 2000, 2000));
    }

    [Fact]
    public void Fewer_than_two_ore_points_make_no_path()
    {
        Assert.Empty(Open(new[] { (1100, 1000) }).Path);
        Assert.Empty(Open(new[] { (1100, 1000) }).Swept);
        Assert.Empty(Open(Array.Empty<(int, int)>()).Path);
    }

    [Fact]
    public void An_ore_point_on_the_centre_block_is_never_swept()
    {
        var s = Open(new[] { (1100, 1000), (1020, 980), (1100, 1100) });

        Assert.DoesNotContain(new SweepPoint(1020, 980), s.Path);
        Assert.Equal(new[] { 0, 2 }, s.Swept.OrderBy(x => x));
        AssertNeverThroughTheCentre(s.Path);
    }

    [Fact]
    public void A_move_through_the_centre_goes_round_it_by_a_ring_1_block()
    {
        var s = Open(new[] { (1100, 1000), (900, 1000) });

        Assert.Equal(5, s.Path.Count);                  // east, round, west, round, east
        Assert.Equal(new SweepPoint(1100, 1000), s.Path[0]);
        Assert.Equal(s.Path[0], s.Path[^1]);
        Assert.Equal(new SweepPoint(900, 1000), s.Path[2]);
        foreach (var detour in new[] { s.Path[1], s.Path[3] })
        {
            var (i, j) = Block(detour);
            Assert.Equal(1, Math.Max(Math.Abs(i), Math.Abs(j)));
        }
        AssertNeverThroughTheCentre(s.Path);
        Assert.Null(UrTaskFreePathRefusal(s.Path, 2000, 2000));
    }

    [Fact]
    public void Without_a_usable_detour_the_point_across_the_centre_is_dropped()
    {
        // Every ring-1 block is masked (a button, or the edge), so the west ore can't be reached round it.
        bool Ring1(int x, int y) => Math.Max(Math.Abs(x - 1000), Math.Abs(y - 1000)) == 50;

        var s = Open(new[] { (1100, 1000), (1100, 1100), (900, 1000) }, Ring1);

        Assert.Equal(new[] { new SweepPoint(1100, 1000), new SweepPoint(1100, 1100), new SweepPoint(1100, 1000) }, s.Path);
        Assert.Equal(new[] { 0, 1 }, s.Swept.OrderBy(x => x));
    }

    [Fact]
    public void Dropped_down_to_one_ore_point_there_is_no_path()
    {
        bool Ring1(int x, int y) => Math.Max(Math.Abs(x - 1000), Math.Abs(y - 1000)) == 50;

        var s = Open(new[] { (1100, 1000), (900, 1000) }, Ring1);

        Assert.Empty(s.Path);
        Assert.Empty(s.Swept);
    }

    [Fact]
    public void Ore_all_round_the_character_makes_a_path_Ur_Task_takes()
    {
        // 24 ore points scattered on an 800x599 client round the default centre, the game's buttons masked.
        var rng = new Random(7);
        var ore = new List<(int X, int Y)>();
        while (ore.Count < 24)
        {
            var (x, y) = (rng.Next(60, 740), rng.Next(90, 460));
            if (Math.Abs(x - 400) <= 40 && Math.Abs(y - 310) <= 40) continue;
            if (HudMask.Contains(x, y, 800, 599)) continue;
            ore.Add((x, y));
        }

        var s = SweepPath.Ore(ore, new SweepPoint(480, 310), 400, 310, 80, 800, 599, (x, y) => HudMask.Contains(x, y, 800, 599));

        Assert.Null(UrTaskFreePathRefusal(s.Path, 800, 599));
        Assert.True(s.Swept.Count >= 20, $"swept {s.Swept.Count} of 24");
        Assert.All(s.Swept, i => Assert.Contains(new SweepPoint(ore[i].X, ore[i].Y), s.Path));
        foreach (var p in s.Path)
            Assert.False(Math.Abs(p.X - 400) <= 40 && Math.Abs(p.Y - 310) <= 40, $"{p} is on the centre block");
        for (var k = 1; k < s.Path.Count; k++)
            Assert.False(SweepPath.CrossesCentre(((s.Path[k - 1].X - 400) / 80.0, (s.Path[k - 1].Y - 310) / 80.0),
                ((s.Path[k].X - 400) / 80.0, (s.Path[k].Y - 310) / 80.0)));
    }
}
