using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

public class SweepChangeTests
{
    /// <summary>From the start block (450,300) to two blocks and back, at 50 px.</summary>
    private static readonly SweepPoint[] Path =
        { new(450, 300), new(450, 250), new(400, 250), new(450, 300) };

    private static FramePixels Grey(int w = 800, int h = 599, params (int X, int Y, int W, int H, Rgb Colour)[] paint)
    {
        var px = Frames.Solid(w, h, PulseFixtures.Grey);
        foreach (var (x, y, bw, bh, c) in paint) Frames.Fill(px, w, x, y, bw, bh, c);
        return new FramePixels(w, h, px);
    }

    [Fact]
    public void An_unchanged_frame_changed_nothing()
        => Assert.Equal(0, SweepChange.Count(Grey(), Grey(), Path, 800, 599, 50));

    [Fact]
    public void Blocks_that_changed_under_the_path_are_counted_and_the_start_is_not()
    {
        var after = Grey(paint: new[]
        {
            (445, 245, 10, 10, PulseFixtures.Black),               // (450,250) broke
            (395, 245, 10, 10, PulseFixtures.Black),               // (400,250) broke
            (445, 295, 10, 10, new Rgb(250, 250, 250)),            // the outline on the start block, where the pointer rests
        });

        Assert.Equal(2, SweepChange.Count(Grey(), after, Path, 800, 599, 50));
    }

    [Fact]
    public void A_change_off_the_path_is_not_counted()
    {
        var after = Grey(paint: new[] { (295, 295, 10, 10, PulseFixtures.Black) });

        Assert.Equal(0, SweepChange.Count(Grey(), after, Path, 800, 599, 50));
    }

    [Fact]
    public void Frames_at_another_size_are_sampled_scaled()
    {
        // 125%: (450,250) is about (562,312) in a 1000x749 frame, (400,250) about (500,312).
        var after = Grey(1000, 749, new[] { (555, 305, 15, 15, PulseFixtures.Black), (493, 305, 15, 15, PulseFixtures.Black) });

        Assert.Equal(2, SweepChange.Count(Grey(1000, 749), after, Path, 800, 599, 50));
    }

    [Fact]
    public void Points_counts_each_swept_block_once_without_the_start()
    {
        var detoured = new SweepPoint[] { new(450, 300), new(450, 250), new(400, 250), new(450, 250), new(500, 250), new(450, 300) };

        Assert.Equal((2, 3), (SweepChange.Points(Path), SweepChange.Points(detoured)));
    }
}
