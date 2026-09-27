using System.Drawing;
using System.Text.Json;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

/// <summary>
/// The pick-point box model, shared in shape with Ur Task's ColorCheck: the
/// picker and the check average the same box around the clicked point.
/// </summary>
public class ColorMatcherBoxTests
{
    private static readonly Rgb Green = new(139, 224, 58);
    private static readonly Rgb Grey = new(128, 128, 128);

    // 20x20 grey with a 5x5 green patch whose centre is (4, 4) — well off the
    // region centre (10, 10), which is where the old SinglePixel check looked.
    private static Bitmap OffCentrePatch()
    {
        var bmp = new Bitmap(20, 20);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(Grey.R, Grey.G, Grey.B));
        using var brush = new SolidBrush(Color.FromArgb(Green.R, Green.G, Green.B));
        g.FillRectangle(brush, 2, 2, 5, 5);
        return bmp;
    }

    [Fact]
    public void Box_check_reads_the_picked_point_not_the_region_centre()
    {
        using var bmp = OffCentrePatch();
        var crit = new ColorCriteria(Green, 15, ColorSamplingMode.SinglePixel,
            Point: new PickPoint(4, 4), Box: new SampleBox());

        var r = new ColorMatcher().Evaluate(bmp, crit);

        Assert.True(r.Matched);
        Assert.Equal(Green, r.Sampled);
    }

    [Fact]
    public void Legacy_single_pixel_still_reads_the_centre()
    {
        using var bmp = OffCentrePatch();
        var crit = new ColorCriteria(Green, 15, ColorSamplingMode.SinglePixel);

        Assert.False(new ColorMatcher().Evaluate(bmp, crit).Matched);
    }

    [Fact]
    public void Picker_average_equals_check_average()
    {
        using var bmp = OffCentrePatch();
        var box = new SampleBox(-3, -3, 7, 7);   // straddles the patch edge on purpose
        var picked = ColorMatcher.AverageBox(bmp, new PickPoint(4, 4), box);
        var crit = new ColorCriteria(picked, 0, ColorSamplingMode.SinglePixel,
            Point: new PickPoint(4, 4), Box: box);

        var r = new ColorMatcher().Evaluate(bmp, crit);

        Assert.Equal(0, r.Distance, 3);
        Assert.True(r.Matched);
    }

    [Fact]
    public void Box_is_clamped_at_the_bitmap_edge()
    {
        using var bmp = OffCentrePatch();
        var crit = new ColorCriteria(Grey, 0, ColorSamplingMode.SinglePixel,
            Point: new PickPoint(19, 19), Box: new SampleBox());

        Assert.True(new ColorMatcher().Evaluate(bmp, crit).Matched);
    }

    [Fact]
    public void Point_scales_with_a_resized_capture()
    {
        // Recorded region 20x20, live capture 40x40 (client window doubled).
        using var small = OffCentrePatch();
        using var big = new Bitmap(small, 40, 40);
        var crit = new ColorCriteria(Green, 15, ColorSamplingMode.SinglePixel,
            Point: new PickPoint(4, 4), Box: new SampleBox(-1, -1, 3, 3));

        var r = new ColorMatcher().Evaluate(big, crit, new RegionRect(0, 0, 20, 20));

        Assert.True(r.Matched);
    }

    [Fact]
    public void Other_state_wins_when_closer_even_inside_tolerance()
    {
        using var bmp = new Bitmap(5, 5);
        using (var g = Graphics.FromImage(bmp)) g.Clear(Color.FromArgb(126, 130, 126));
        var crit = new ColorCriteria(new Rgb(120, 140, 120), 40, ColorSamplingMode.SinglePixel,
            Point: new PickPoint(2, 2), Box: new SampleBox(), Other: Grey);

        var r = new ColorMatcher().Evaluate(bmp, crit);

        Assert.False(r.Matched);
        Assert.NotNull(r.DistanceToOther);
    }

    [Theory]
    [InlineData(5, 5, true)]
    [InlineData(9, 9, true)]
    [InlineData(10, 5, false)]
    [InlineData(0, 5, false)]
    public void Box_size_limits(int w, int h, bool valid)
        => Assert.Equal(valid, new SampleBox(0, 0, w, h).IsValid);

    [Fact]
    public void Json_shape_matches_ur_task_and_legacy_omits_new_keys()
    {
        var withBox = new ColorCriteria(Green, 15, ColorSamplingMode.SinglePixel,
            Point: new PickPoint(4, 4), Box: new SampleBox(), Other: Grey);
        var json = JsonSerializer.Serialize(withBox, TriggerJsonOptions.Default);
        Assert.Contains("\"box\"", json);
        Assert.Contains("\"offsetX\": -2", json);
        Assert.Contains("\"w\": 5", json);
        Assert.Contains("\"other\"", json);
        Assert.Equal(withBox, JsonSerializer.Deserialize<ColorCriteria>(json, TriggerJsonOptions.Default));

        var legacy = JsonSerializer.Serialize(
            new ColorCriteria(Green, 15, ColorSamplingMode.SinglePixel), TriggerJsonOptions.Default);
        Assert.DoesNotContain("box", legacy);
        Assert.DoesNotContain("point", legacy);
        Assert.DoesNotContain("other", legacy);
    }

    [Fact]
    public void Legacy_json_without_box_still_loads()
    {
        const string json = """{ "targetRgb": { "r": 1, "g": 2, "b": 3 }, "toleranceRgb": 10, "samplingMode": "regionAverage" }""";
        var c = JsonSerializer.Deserialize<ColorCriteria>(json, TriggerJsonOptions.Default)!;
        Assert.Null(c.Box);
        Assert.Equal(ColorSamplingMode.RegionAverage, c.SamplingMode);
    }

    [Theory]
    [InlineData(139, 224, 58, "green #8BE03A")]
    [InlineData(128, 128, 128, "grey #808080")]
    [InlineData(230, 40, 70, "red #E62846")]
    [InlineData(20, 30, 90, "dark blue #141E5A")]
    [InlineData(250, 250, 250, "white #FAFAFA")]
    [InlineData(5, 5, 5, "black #050505")]
    public void Names_colours_like_ur_task(int r, int g, int b, string expected)
        => Assert.Equal(expected, ColorNamer.Describe(new Rgb(r, g, b)));
}
