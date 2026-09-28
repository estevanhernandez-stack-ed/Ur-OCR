using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

/// <summary>
/// The finder on painted frames. Pitch 40, so samples every 10 px at 5, 15, 25 ... and a 40 px block
/// painted on a grid point holds 4 x 4 samples whose mean is that grid point.
/// </summary>
public class TargetFinderTests
{
    private static readonly Rgb Grey = new(120, 120, 120);
    private static readonly Rgb Cyan = new(60, 220, 230);

    private static FinderSetup Setup(int w = 400, int h = 300, int cx = 200, int cy = 150, int radius = 2,
        IReadOnlyList<OreColour>? ore = null) =>
        new("grey", w, h, Pitch: 40, CenterX: cx, CenterY: cy, RadiusBlocks: radius, Outline: new OutlineBox(40, 40),
            Ore: ore ?? new[] { new OreColour("cyan crystal", Cyan) }, OreToleranceRgb: 40);

    private static FramePixels Frame(int w, int h, params (int X, int Y, int W, int H, Rgb C)[] paint)
    {
        var px = Frames.Solid(w, h, Grey);
        foreach (var (x, y, bw, bh, c) in paint) Frames.Fill(px, w, x, y, bw, bh, c);
        return new FramePixels(w, h, px);
    }

    private static (int, int) At(FinderTarget t) => (t.X, t.Y);

    [Fact]
    public void A_plain_frame_gives_the_grid_nearest_first()
    {
        var targets = TargetFinder.Find(Frame(400, 300), Setup());

        Assert.Equal(13, targets.Count);                            // radius 2: 13 lattice points
        Assert.All(targets, t => Assert.False(t.Ore));
        Assert.Equal(new[] { (200, 150), (200, 110), (160, 150), (240, 150), (200, 190) }, targets.Take(5).Select(At));
        Assert.Equal(Enumerable.Range(1, 13).Select(n => $"stone {n}"), targets.Select(t => t.Label));
    }

    [Fact]
    public void An_ore_block_is_one_target_at_its_centre_ahead_of_the_grid()
    {
        var targets = TargetFinder.Find(Frame(400, 300, (260, 130, 40, 40, Cyan)), Setup());

        Assert.Equal(new FinderTarget(280, 150, true, "ore 1"), targets[0]);
        Assert.Equal(13, targets.Count);                            // 12 grid points and the ore
        Assert.DoesNotContain(targets, t => !t.Ore && At(t) == (280, 150));   // its grid point is dropped
        Assert.Equal(new FinderTarget(200, 150, false, "stone 1"), targets[1]);
    }

    [Fact]
    public void Ore_patches_go_nearest_first()
    {
        var targets = TargetFinder.Find(Frame(400, 300, (340, 130, 40, 40, Cyan), (180, 210, 40, 40, Cyan)), Setup());

        Assert.Equal(
            new[] { new FinderTarget(200, 230, true, "ore 1"), new FinderTarget(360, 150, true, "ore 2") },
            targets.Where(t => t.Ore));
    }

    [Fact]
    public void A_single_ore_coloured_sample_is_noise()
    {
        var targets = TargetFinder.Find(Frame(400, 300, (265, 135, 1, 1, Cyan)), Setup());

        Assert.DoesNotContain(targets, t => t.Ore);
        Assert.Equal(13, targets.Count);
        Assert.Contains(targets, t => At(t) == (280, 150));
    }

    [Fact]
    public void A_colour_just_outside_the_tolerance_is_not_ore()
    {
        var outside = new Rgb(60, 220, 189);   // 41 from cyan
        var inside = new Rgb(60, 220, 190);    // 40 from cyan

        Assert.DoesNotContain(TargetFinder.Find(Frame(400, 300, (260, 130, 40, 40, outside)), Setup()), t => t.Ore);
        Assert.Contains(TargetFinder.Find(Frame(400, 300, (260, 130, 40, 40, inside)), Setup()), t => t.Ore);
    }

    [Fact]
    public void An_empty_palette_gives_only_the_grid()
    {
        var targets = TargetFinder.Find(Frame(400, 300, (260, 130, 40, 40, Cyan)), Setup(ore: Array.Empty<OreColour>()));

        Assert.Equal(13, targets.Count);
        Assert.DoesNotContain(targets, t => t.Ore);
        Assert.Contains(targets, t => At(t) == (280, 150));
    }

    [Fact]
    public void Points_whose_outline_box_leaves_the_window_are_dropped()
    {
        var setup = Setup(cx: 30);

        var targets = TargetFinder.Find(Frame(400, 300, (0, 130, 20, 40, Cyan)), setup);

        Assert.Equal(9, targets.Count);                             // the 4 grid points left of x = 20 are gone
        Assert.All(targets, t => Assert.True(setup.BoxFits(t.X, t.Y)));
        Assert.DoesNotContain(targets, t => t.Ore);                 // the patch's centre (10, 150) is too near the edge
    }

    [Fact]
    public void The_grid_is_capped_at_64_nearest_first()
    {
        var targets = TargetFinder.Find(Frame(800, 600), Setup(w: 800, h: 600, cx: 400, cy: 300, radius: 5));

        Assert.Equal(TargetFinder.MaxPoints, targets.Count);        // radius 5 has 81 lattice points
        var d2 = targets.Select(t => (t.X - 400) * (t.X - 400) + (t.Y - 300) * (t.Y - 300)).ToList();
        Assert.Equal(d2.OrderBy(d => d), d2);
        Assert.Equal(20 * 40 * 40, d2.Max());   // 49 within 4 blocks, 12 at 17 and 18, then 3 of the 8 at 20
    }

    [Fact]
    public void Never_more_than_64_points_and_ore_fills_them_first()
    {
        var paint = new List<(int, int, int, int, Rgb)>();
        for (var py = 0; py < 10; py++)
            for (var px = 0; px < 13; px++)
                paint.Add((px * 30, py * 30, 20, 10, Cyan));        // 130 two-sample patches, 108 of them placeable

        var targets = TargetFinder.Find(Frame(400, 300, paint.ToArray()), Setup());

        Assert.Equal(TargetFinder.MaxPoints, targets.Count);
        Assert.All(targets, t => Assert.True(t.Ore));
        var d2 = targets.Select(t => (t.X - 200) * (t.X - 200) + (t.Y - 150) * (t.Y - 150)).ToList();
        Assert.Equal(d2.OrderBy(d => d), d2);
    }

    [Fact]
    public void A_frame_at_another_size_gives_the_same_points()
    {
        var measured = TargetFinder.Find(Frame(400, 300, (260, 130, 40, 40, Cyan)), Setup());
        var doubled = TargetFinder.Find(Frame(800, 600, (520, 260, 80, 80, Cyan)), Setup());

        Assert.Equal(measured, doubled);
    }
}
