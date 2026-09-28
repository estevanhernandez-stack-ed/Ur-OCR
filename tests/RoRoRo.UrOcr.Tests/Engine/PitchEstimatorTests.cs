using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

/// <summary>
/// The block size read off a calm frame: dark blocks with bright 2 px lines at a known spacing, the
/// character at the centre of an 800x600 frame. Estimates within 2 px of the painted spacing.
/// </summary>
public class PitchEstimatorTests
{
    private const int W = 800, H = 600, Cx = 400, Cy = 300;
    private static readonly Rgb Dark = new(40, 45, 60);
    private static readonly Rgb Line = new(225, 230, 240);

    private static FramePixels Grid(int spacingX, int spacingY, Action<int[]>? paint = null)
    {
        var px = Frames.Grid(W, H, spacingX, spacingY, Dark, Line);
        paint?.Invoke(px);
        return new FramePixels(W, H, px);
    }

    /// <summary>Every channel moved by up to 30 either way, from a fixed seed.</summary>
    private static void Noise(int[] px)
    {
        var rnd = new Random(1234);
        for (var i = 0; i < px.Length; i++)
        {
            int Ch(int v) => Math.Clamp(v + rnd.Next(-30, 31), 0, 255);
            var p = px[i];
            px[i] = (Ch((p >> 16) & 0xFF) << 16) | (Ch((p >> 8) & 0xFF) << 8) | Ch(p & 0xFF);
        }
    }

    [Theory]
    [InlineData(22)]
    [InlineData(32)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(170)]
    public void A_block_grid_gives_its_spacing(int spacing)
    {
        var e = PitchEstimator.Estimate(Grid(spacing, spacing), Cx, Cy);

        Assert.NotNull(e);
        Assert.InRange(e!.Pitch, spacing - 2, spacing + 2);
        Assert.InRange(e.Confidence, 0.3, 1.0);
    }

    [Theory]
    [InlineData(22)]
    [InlineData(32)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(170)]
    public void Noise_does_not_move_the_spacing(int spacing)
    {
        var e = PitchEstimator.Estimate(Grid(spacing, spacing, Noise), Cx, Cy);

        Assert.NotNull(e);
        Assert.InRange(e!.Pitch, spacing - 2, spacing + 2);
    }

    [Fact]
    public void The_character_at_the_centre_is_not_read_as_blocks()
    {
        var e = PitchEstimator.Estimate(Grid(32, 32, px => Frames.Fill(px, W, Cx - 10, Cy - 12, 20, 24, new Rgb(250, 180, 90))), Cx, Cy);

        Assert.NotNull(e);
        Assert.InRange(e!.Pitch, 30, 34);
    }

    [Fact]
    public void A_flat_frame_has_no_block_size()
    {
        Assert.Null(PitchEstimator.Estimate(new FramePixels(W, H, Frames.Solid(W, H, Dark)), Cx, Cy));
    }

    [Fact]
    public void Axes_that_disagree_give_no_block_size()
    {
        // Across 22, down 32: each axis is clear on its own, 45% apart.
        var frame = Grid(22, 32);

        Assert.Null(PitchEstimator.Estimate(frame, Cx, Cy));
    }

    [Fact]
    public void Axes_that_disagree_each_read_their_own_spacing()
    {
        // The combine rule, not a weak axis, is what refuses the frame above.
        var e = PitchEstimator.EstimateAxes(Grid(22, 32), Cx, Cy);

        Assert.InRange(e.AlongX!.Value, 20, 24);
        Assert.InRange(e.AlongY!.Value, 30, 34);
    }

    [Fact]
    public void Every_other_line_brighter_is_still_the_fundamental()
    {
        // Lines every 32 px, every second one brighter: 64 repeats perfectly, 32 is the block.
        var px = Frames.Grid(W, H, 32, 32, Dark, new Rgb(185, 190, 200));
        for (var x = 7; x < W; x += 64) Frames.Fill(px, W, x, 0, 2, H, Line);
        for (var y = 7; y < H; y += 64) Frames.Fill(px, W, 0, y, W, 2, Line);

        var e = PitchEstimator.Estimate(new FramePixels(W, H, px), Cx, Cy);

        Assert.NotNull(e);
        Assert.InRange(e!.Pitch, 30, 34);
    }

    [Fact]
    public void The_same_frame_gives_the_same_answer()
    {
        var frame = Grid(50, 50, Noise);

        Assert.Equal(PitchEstimator.Estimate(frame, Cx, Cy), PitchEstimator.Estimate(frame, Cx, Cy));
    }
}
