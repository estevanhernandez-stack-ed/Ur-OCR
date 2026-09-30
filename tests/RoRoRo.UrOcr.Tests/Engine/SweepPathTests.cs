using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Ipc;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

public class SweepPathTests
{
    private static readonly Func<int, int, bool> NoHud = (_, _) => false;
    private static bool Hud(int x, int y) => HudMask.Contains(x, y, 800, 599);

    /// <summary>Open ground: a 2000x2000 client with the character in the middle and no HUD.</summary>
    private static IReadOnlyList<SweepPoint> Open(bool nearSide = false) => SweepPath.Build(1000, 1000, 50, 2000, 2000, nearSide, NoHud);

    private static List<(int I, int J)> Cells(IReadOnlyList<SweepPoint> path, int cx, int cy, int pitch) =>
        path.Select(p => ((p.X - cx) / pitch, (p.Y - cy) / pitch)).ToList();

    /// <summary>Null when Ur Task's SweepPathMacro.Validate would take the path and step; otherwise the
    /// rule it breaks: 3 to 256 points, a step of 8 to 240, every point inside the client and a whole
    /// number of steps from the start, no repeat, and the end on the start block.</summary>
    internal static string? UrTaskRefusal(IReadOnlyList<SweepPoint> path, int step, int clientW, int clientH)
    {
        if (path.Count is < 3 or > BridgeContract.MaxSweepPoints) return $"{path.Count} points";
        if (step is < BridgeContract.MinSweepStep or > BridgeContract.MaxSweepStep) return $"step {step}";
        var start = path[0];
        for (var i = 0; i < path.Count; i++)
        {
            var p = path[i];
            if (p.X < 0 || p.Y < 0 || p.X >= clientW || p.Y >= clientH) return $"point {i + 1} {p} is outside the client";
            if ((p.X - start.X) % step != 0 || (p.Y - start.Y) % step != 0) return $"point {i + 1} {p} is off the {step} px lattice";
            if (i > 0 && path[i - 1] == p) return $"point {i + 1} repeats";
        }
        return path[^1] == start ? null : $"ends at {path[^1]}, not the start {start}";
    }

    private static int Ring((int I, int J) c) => Math.Max(Math.Abs(c.I), Math.Abs(c.J));
    private static int Steps((int I, int J) a, (int I, int J) b) => Math.Max(Math.Abs(a.I - b.I), Math.Abs(a.J - b.J));

    [Fact]
    public void On_open_ground_the_path_walks_rings_1_to_4_in_spiral_order_from_the_east_block()
    {
        var cells = Cells(Open(), 1000, 1000, 50);

        Assert.Equal(81, cells.Count);   // 8 + 16 + 24 + 32 blocks, then back to the start
        Assert.Equal(new (int, int)[] { (1, 0), (1, -1), (0, -1), (-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (2, 1), (2, 0) },
            cells.Take(10));
        Assert.Equal((1, 0), cells[^1]);
        Assert.Equal(80, cells.Take(80).Distinct().Count());
    }

    [Fact]
    public void Every_hop_on_open_ground_is_one_block_except_the_closing_return()
    {
        var cells = Cells(Open(), 1000, 1000, 50);

        for (var k = 1; k < cells.Count - 1; k++) Assert.Equal(1, Steps(cells[k - 1], cells[k]));
        Assert.Equal(((4, 4), (1, 0)), (cells[^2], cells[^1]));
    }

    [Fact]
    public void Each_ring_starts_one_block_outward_from_where_the_last_ended()
    {
        var cells = Cells(Open(), 1000, 1000, 50);

        for (var k = 1; k <= 3; k++)
        {
            var end = cells.IndexOf((k, k));
            Assert.Equal((k + 1, k), cells[end + 1]);
        }
        for (var k = 1; k < cells.Count - 1; k++) Assert.True(Ring(cells[k]) >= Ring(cells[k - 1]));
    }

    [Fact]
    public void The_path_never_stands_on_or_passes_through_the_centre_block()
    {
        foreach (var pitch in new[] { 16, 22, 32, 50, 80, 120, 150, 160, 170, 180, 240 })
            foreach (var nearSide in new[] { true, false })
            {
                var path = SweepPath.Build(400, 310, pitch, 800, 599, nearSide, Hud);
                var cells = Cells(path, 400, 310, pitch);

                Assert.True(path.Count >= SweepPath.MinPoints, $"pitch {pitch}: only {path.Count} points");
                Assert.True(path.Count <= SweepPath.MaxPoints);
                Assert.Equal((1, 0), cells[0]);
                Assert.Equal((1, 0), cells[^1]);
                Assert.DoesNotContain((0, 0), cells);
                for (var k = 1; k < cells.Count; k++)
                {
                    Assert.NotEqual(cells[k - 1], cells[k]);
                    Assert.False(SweepPath.CrossesCentre(cells[k - 1], cells[k]),
                        $"pitch {pitch}, near side {nearSide}: {cells[k - 1]} to {cells[k]} crosses the centre");
                }
            }
    }

    [Fact]
    public void HUD_points_are_skipped_and_the_path_jumps_over_them()
    {
        var path = SweepPath.Build(400, 310, 50, 800, 599, nearSide: false, Hud);
        var cells = Cells(path, 400, 310, 50);

        Assert.DoesNotContain(path, p => Hud(p.X, p.Y));
        Assert.True(path.Count < 81);                                    // ring 4's bottom row is under the hotbar line
        Assert.Contains(Enumerable.Range(1, cells.Count - 1), k => Steps(cells[k - 1], cells[k]) > 1);
    }

    [Fact]
    public void Every_point_stays_12_px_inside_the_client()
    {
        var path = SweepPath.Build(400, 310, 100, 800, 599, nearSide: true, NoHud);

        Assert.All(path, p => Assert.True(p.X >= 12 && p.Y >= 12 && p.X <= 787 && p.Y <= 586, $"{p} is too near the edge"));
        Assert.True(path.Count < 81);                                    // ring 4 at 100 px leaves the client
    }

    [Fact]
    public void The_near_side_extension_reaches_within_a_block_of_the_bottom_margin()
    {
        var path = SweepPath.Build(400, 310, 50, 800, 599, nearSide: true, NoHud);
        var bottom = 599 - SweepPath.EdgeMarginPx;

        Assert.Contains(path, p => p.Y >= bottom - 50 && p.Y <= bottom);
        Assert.DoesNotContain(path, p => p.Y > bottom);
    }

    [Fact]
    public void The_other_three_sides_stay_at_ring_4()
    {
        var cells = Cells(SweepPath.Build(400, 310, 50, 800, 599, nearSide: true, NoHud), 400, 310, 50);

        Assert.All(cells, c => Assert.True(Math.Abs(c.I) <= 4 && c.J >= -4, $"{c} is past ring 4 on a far side"));
        Assert.Contains(cells, c => c.J > 4);
    }

    [Fact]
    public void Without_the_near_side_extension_nothing_passes_ring_4()
    {
        var cells = Cells(SweepPath.Build(400, 310, 50, 800, 599, nearSide: false, NoHud), 400, 310, 50);

        Assert.All(cells, c => Assert.True(Ring(c) <= 4));
    }

    [Fact]
    public void The_extension_continues_one_block_down_from_the_end_of_ring_4()
    {
        var cells = Cells(SweepPath.Build(400, 310, 50, 800, 599, nearSide: true, NoHud), 400, 310, 50);

        var end = cells.IndexOf((4, 4));
        Assert.Equal(new (int, int)[] { (4, 5), (3, 5) }, cells.Skip(end + 1).Take(2));
    }

    [Fact]
    public void With_the_real_HUD_mask_the_hotbar_line_stops_the_extension()
    {
        // HudMask masks everything below y 470 of 599, the full width: the near-side rows stop there.
        var path = SweepPath.Build(390, 340, 32, 800, 599, nearSide: true, Hud);

        Assert.True(path.Max(p => p.Y) <= 470);
    }

    [Fact]
    public void A_start_block_outside_the_game_area_gives_no_path()
        => Assert.Empty(SweepPath.Build(780, 300, 50, 800, 599, nearSide: true, NoHud));

    [Fact]
    public void A_long_path_is_capped_at_256_points_and_still_closes()
    {
        var path = SweepPath.Build(400, 310, 16, 800, 3000, nearSide: true, NoHud);

        Assert.InRange(path.Count, 250, SweepPath.MaxPoints);
        Assert.Equal(new SweepPoint(416, 310), path[0]);
        Assert.Equal(path[0], path[^1]);
    }

    [Fact]
    public void When_clipping_leaves_a_hop_through_the_centre_it_goes_round()
    {
        // 180 px blocks: ring 1's bottom row is under the hotbar line, ring 2 keeps only (2,0) and (2,-1).
        // The move from (-1,0) to (2,0) would cross the character's block, so it goes by (0,-1).
        var cells = Cells(SweepPath.Build(400, 310, 180, 800, 599, nearSide: true, Hud), 400, 310, 180);

        Assert.Equal(new (int, int)[] { (1, 0), (1, -1), (0, -1), (-1, -1), (-1, 0), (0, -1), (2, 0), (2, -1), (1, 0) }, cells);
    }

    [Fact]
    public void At_160_px_round_the_default_centre_the_path_is_ring_1_and_what_fits_of_ring_2()
    {
        // The owner's close zoom: 150 to 180 px blocks. With the real HUD mask only columns -1 to 2 and
        // rows -1 to 1 fit (x 240 to 720, y 150 to 470); ring 2 keeps its right column, the rest of it
        // is off the client or in the HUD. Row 1 sits on y 470, the hotbar line, which HudMask leaves in.
        var path = SweepPath.Build(400, 310, 160, 800, 599, nearSide: true, Hud);
        var cells = Cells(path, 400, 310, 160);

        Assert.Equal(new[]
        {
            new SweepPoint(560, 310), new SweepPoint(560, 150), new SweepPoint(400, 150), new SweepPoint(240, 150),
            new SweepPoint(240, 310), new SweepPoint(240, 470), new SweepPoint(400, 470), new SweepPoint(560, 470),
            new SweepPoint(720, 470), new SweepPoint(720, 310), new SweepPoint(720, 150), new SweepPoint(560, 310),
        }, path);
        Assert.Null(UrTaskRefusal(path, 160, 800, 599));
        Assert.DoesNotContain(path, p => Hud(p.X, p.Y));
        Assert.DoesNotContain((0, 0), cells);
        for (var k = 1; k < cells.Count; k++) Assert.False(SweepPath.CrossesCentre(cells[k - 1], cells[k]));
    }

    [Fact]
    public void From_150_to_180_px_every_path_round_the_default_centre_is_one_Ur_Task_takes()
    {
        for (var pitch = 150; pitch <= 180; pitch++)
            foreach (var nearSide in new[] { true, false })
            {
                var path = SweepPath.Build(400, 310, pitch, 800, 599, nearSide, Hud);

                Assert.NotEmpty(path);
                Assert.True(UrTaskRefusal(path, pitch, 800, 599) is null, $"pitch {pitch}: {UrTaskRefusal(path, pitch, 800, 599)}");
                Assert.DoesNotContain(path, p => Hud(p.X, p.Y));
            }
    }

    [Theory]
    [InlineData(-1, 0, 1, 0, true)]      // straight through the middle
    [InlineData(-1, 0, 2, -1, true)]     // a shallow slant through it
    [InlineData(-1, 0, 0, -1, false)]    // touches the corner only
    [InlineData(1, 0, 1, 1, false)]      // beside it
    public void A_move_crosses_the_centre_only_through_its_inside(int ai, int aj, int bi, int bj, bool crosses)
        => Assert.Equal(crosses, SweepPath.CrossesCentre((ai, aj), (bi, bj)));
}
