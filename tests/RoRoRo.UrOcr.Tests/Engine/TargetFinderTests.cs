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
        var targets = TargetFinder.Find(Frame(400, 300, (340, 130, 40, 40, Cyan), (180, 210, 40, 40, Cyan)),
            Setup(radius: 4));                                      // reach 200 px takes in the patch at 160

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
        // The right edge, not the left: the left column is HUD territory (below), and this test's
        // job is the outline-box-fits rule alone.
        var setup = Setup(cx: 370);

        var targets = TargetFinder.Find(Frame(400, 300, (380, 130, 20, 40, Cyan)), setup);

        Assert.Equal(9, targets.Count);                             // the 4 grid points right of x = 380 are gone
        Assert.All(targets, t => Assert.True(setup.BoxFits(t.X, t.Y)));
        Assert.DoesNotContain(targets, t => t.Ore);                 // the patch's centre (390, 150) is too near the edge
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

        var targets = TargetFinder.Find(Frame(400, 300, paint.ToArray()), Setup(radius: 5));   // reach 240 px

        Assert.Equal(TargetFinder.MaxPoints, targets.Count);
        Assert.All(targets, t => Assert.True(t.Ore));
        var d2 = targets.Select(t => (t.X - 200) * (t.X - 200) + (t.Y - 150) * (t.Y - 150)).ToList();
        Assert.Equal(d2.OrderBy(d => d), d2);
    }

    [Fact]
    public void Ore_past_reach_is_not_looked_for_so_a_white_hud_panel_is_not_quartz()
    {
        var white = new Rgb(240, 240, 240);
        var setup = Setup(ore: new[] { new OreColour("white quartz", white) });   // reach (2 + 1) x 40 = 120 px

        var targets = TargetFinder.Find(Frame(400, 300,
            (360, 10, 40, 40, white),                               // a HUD panel in the corner, 216 px out
            (260, 130, 40, 40, white)), setup);                     // quartz a block and a half from the character

        Assert.Equal(new[] { new FinderTarget(280, 150, true, "ore 1") }, targets.Where(t => t.Ore));
        Assert.True(setup.BoxFits(380, 30));                        // it would fit: reach alone keeps it out
    }

    [Fact]
    public void A_patch_three_blocks_wide_gets_a_point_on_each_block()
    {
        var targets = TargetFinder.Find(Frame(400, 300, (260, 130, 120, 40, Cyan)), Setup(radius: 4));

        Assert.Equal(new[] { (280, 150), (320, 150), (360, 150) }, targets.Where(t => t.Ore).Select(At));
        Assert.Equal(new[] { "ore 1", "ore 2", "ore 3" }, targets.Where(t => t.Ore).Select(t => t.Label));
        Assert.DoesNotContain(targets, t => !t.Ore && At(t) is (280, 150) or (320, 150) or (360, 150));
    }

    [Fact]
    public void A_patch_three_blocks_square_gets_nine_points()
    {
        var targets = TargetFinder.Find(Frame(400, 300, (260, 50, 120, 120, Cyan)), Setup(radius: 4));

        var ore = targets.Where(t => t.Ore).Select(At).ToHashSet();
        var expected = from y in new[] { 70, 110, 150 } from x in new[] { 280, 320, 360 } select (x, y);
        Assert.Equal(expected.ToHashSet(), ore);
    }

    [Fact]
    public void A_patch_smaller_than_a_block_off_the_grid_keeps_its_centre()
    {
        var targets = TargetFinder.Find(Frame(400, 300, (250, 170, 20, 20, Cyan)), Setup());

        Assert.Equal(new[] { new FinderTarget(260, 180, true, "ore 1") }, targets.Where(t => t.Ore));
    }

    [Fact]
    public void Points_closer_than_half_a_block_keep_the_one_nearest_the_character()
    {
        // Centre (275, 155) and the grid point (280, 150) are 7 px apart; the centre is nearer (200, 150).
        var targets = TargetFinder.Find(Frame(400, 300, (265, 140, 30, 30, Cyan)), Setup());

        Assert.Equal(new[] { new FinderTarget(275, 155, true, "ore 1") }, targets.Where(t => t.Ore));
    }

    [Fact]
    public void No_ore_point_on_the_character_and_its_stone_point_stays()
    {
        var targets = TargetFinder.Find(Frame(400, 300, (180, 130, 40, 40, Cyan)), Setup());   // ore colour on the centre

        Assert.DoesNotContain(targets, t => t.Ore);
        Assert.Equal(new FinderTarget(200, 150, false, "stone 1"), targets[0]);
        Assert.Equal(13, targets.Count);
    }

    [Fact]
    public void Ore_around_the_character_is_aimed_at_everywhere_but_the_centre()
    {
        var targets = TargetFinder.Find(Frame(400, 300, (140, 90, 120, 120, Cyan)), Setup());

        var ore = targets.Where(t => t.Ore).ToList();
        Assert.Equal(8, ore.Count);
        Assert.All(ore, t => Assert.True((t.X - 200) * (t.X - 200) + (t.Y - 150) * (t.Y - 150) > 20 * 20));
        Assert.Contains(targets, t => !t.Ore && At(t) == (200, 150));
        Assert.Equal(13, targets.Count);                            // 8 ore, the stone centre, 4 stone past the patch
    }

    [Fact]
    public void Ore_everywhere_fills_64_points_nearest_first_none_on_the_character()
    {
        var cyanFrame = new FramePixels(800, 600, Frames.Solid(800, 600, Cyan));

        var targets = TargetFinder.Find(cyanFrame, Setup(w: 800, h: 600, cx: 400, cy: 300, radius: 5));

        Assert.Equal(TargetFinder.MaxPoints, targets.Count);
        Assert.All(targets, t => Assert.True(t.Ore));
        var d2 = targets.Select(t => (t.X - 400) * (t.X - 400) + (t.Y - 300) * (t.Y - 300)).ToList();
        Assert.Equal(d2.OrderBy(d => d), d2);
        Assert.True(d2.Min() > 20 * 20);
    }

    [Fact]
    public void A_frame_at_another_size_gives_the_same_points()
    {
        var measured = TargetFinder.Find(Frame(400, 300, (260, 130, 40, 40, Cyan)), Setup());
        var doubled = TargetFinder.Find(Frame(800, 600, (520, 260, 80, 80, Cyan)), Setup());

        Assert.Equal(measured, doubled);
    }

    // Bug (2026-09-28): a ClearAt point landed on the inventory button in the bottom bar and opened
    // the menu. TargetFinder now drops every point (ore and stone) on one of the game's buttons
    // (HudMask.Boxes, 2026-09-30): the Roblox menu, Go to Top, the left icon column (x < 160), the
    // hotbar (y 470 and below, x 160 to 725) and the update timer, in the measured 800x599 client.

    private static FinderSetup HudSetup(int pitch, int radius, IReadOnlyList<OreColour>? ore = null) =>
        new("stone", 800, 599, Pitch: pitch, CenterX: 400, CenterY: 310, RadiusBlocks: radius,
            Outline: new OutlineBox(40, 40), Ore: ore ?? Array.Empty<OreColour>(), OreToleranceRgb: 40);

    [Fact]
    public void The_stone_grid_drops_points_in_the_hud_the_centre_stays()
    {
        // Orthogonal neighbours only (radius 1): right (650,310) is game, left (150,310) is in the icon
        // column, down (400,560) is on the hotbar, up (400,60) is on Go to Top.
        var targets = TargetFinder.Find(Frame(800, 599), HudSetup(pitch: 250, radius: 1));

        Assert.Equal(new[] { (400, 310), (650, 310) }, targets.Select(At).OrderBy(p => p.Item1));
        Assert.All(targets, t => Assert.False(t.Ore));
        Assert.DoesNotContain(targets, t => HudMask.Contains(t.X, t.Y, 800, 599));
    }

    [Fact]
    public void A_grid_that_reaches_past_the_hotbar_drops_only_the_points_below_it()
    {
        // Pitch 100, radius 2: 13 lattice points, well under the 64 cap, one of them (400, 510) past
        // the hotbar line at 470.
        var targets = TargetFinder.Find(Frame(800, 599), HudSetup(pitch: 100, radius: 2));

        Assert.Equal(12, targets.Count);
        Assert.DoesNotContain(targets, t => t.Y > 470);
        Assert.Contains(targets, t => At(t) == (400, 310));   // the centre, outside the mask, always stays
    }

    [Fact]
    public void Ore_inside_the_hud_gives_no_points()
    {
        // A cyan HUD icon in the bottom bar (y 480..520), within reach of the character at (400, 310).
        var ore = new[] { new OreColour("cyan crystal", Cyan) };
        var targets = TargetFinder.Find(
            Frame(800, 599, (380, 480, 40, 40, Cyan)),
            HudSetup(pitch: 40, radius: 5, ore: ore));

        Assert.DoesNotContain(targets, t => t.Ore);
    }

    [Fact]
    public void The_top_strip_beside_Go_to_Top_is_game()
    {
        // Character at (600, 310), radius 1: up (600, 40) sits in the top strip right of Go to Top,
        // which the old full-width band threw away; down (600, 580) is on the hotbar; right is off
        // the client.
        var targets = TargetFinder.Find(Frame(800, 599), HudSetup(pitch: 270, radius: 1) with { CenterX = 600 });

        Assert.Equal(new[] { (330, 310), (600, 40), (600, 310) }, targets.Select(At).OrderBy(p => p));
    }

    [Theory]
    [InlineData(387, true)]    // (787, 310): 12 px inside the right edge
    [InlineData(388, false)]   // (788, 310): 11 px, too close, though a 4 px outline box still fits
    public void Every_point_stays_12_px_inside_the_client(int pitch, bool kept)
    {
        var setup = new FinderSetup("stone", 800, 599, Pitch: pitch, CenterX: 400, CenterY: 310, RadiusBlocks: 1,
            Outline: new OutlineBox(4, 4), Ore: Array.Empty<OreColour>(), OreToleranceRgb: 40);

        var targets = TargetFinder.Find(Frame(800, 599), setup);

        Assert.True(setup.BoxFits(400 + pitch, 310));
        Assert.Equal(kept, targets.Any(t => At(t) == (400 + pitch, 310)));
        Assert.All(targets, t => Assert.InRange(t.X, TargetFinder.EdgeMarginPx, 799 - TargetFinder.EdgeMarginPx));
        Assert.All(targets, t => Assert.InRange(t.Y, TargetFinder.EdgeMarginPx, 598 - TargetFinder.EdgeMarginPx));
    }
}
