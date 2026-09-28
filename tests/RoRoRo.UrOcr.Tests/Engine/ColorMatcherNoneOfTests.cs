using System.Drawing;
using System.Text.Json;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

/// <summary>
/// "None of" checks: match when the sample is more than tolerance from every
/// listed colour. The ore-stop ring lists a layer's rock plus sky and the
/// character, so anything else (ore) matches.
/// </summary>
public class ColorMatcherNoneOfTests
{
    private static readonly Rgb Navy = new(30, 30, 90);
    private static readonly Rgb Grey = new(120, 120, 120);
    private static readonly Rgb Sky = new(135, 206, 235);
    private static readonly Rgb Orange = new(240, 160, 40);

    private static ColorCriteria NoneOf(int tolerance, params Rgb[] colours) =>
        new(new Rgb(0, 0, 0), tolerance, ColorSamplingMode.SinglePixel, NoneOf: colours);

    private static Bitmap Solid(Rgb c)
    {
        var bmp = new Bitmap(9, 9);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(c.R, c.G, c.B));
        return bmp;
    }

    [Fact]
    public void Matches_when_far_from_every_listed_colour()
    {
        var r = ColorMatcher.Judge(Orange, NoneOf(30, Navy, Grey, Sky));

        Assert.True(r.Matched);
        Assert.Equal(Grey, r.Nearest);           // orange to grey is about 149.7, the nearest of the three
        Assert.Equal(149.7, r.Distance, 1);
    }

    [Fact]
    public void No_match_when_within_tolerance_of_any_listed_colour()
    {
        var nearGrey = new Rgb(125, 118, 122);

        var r = ColorMatcher.Judge(nearGrey, NoneOf(30, Navy, Grey, Sky));

        Assert.False(r.Matched);
        Assert.Equal(Grey, r.Nearest);
        Assert.True(r.Distance < 30);
    }

    [Fact]
    public void Exactly_at_tolerance_counts_as_near()
    {
        // (120,120,150) is exactly 30 from grey.
        var r = ColorMatcher.Judge(new Rgb(120, 120, 150), NoneOf(30, Grey));

        Assert.False(r.Matched);
        Assert.Equal(30, r.Distance, 3);
    }

    [Fact]
    public void Target_is_ignored_in_none_of_mode()
    {
        var targetInList = new ColorCriteria(Orange, 30, ColorSamplingMode.SinglePixel, NoneOf: new[] { Orange });
        var targetElsewhere = new ColorCriteria(Navy, 30, ColorSamplingMode.SinglePixel, NoneOf: new[] { Grey });

        Assert.False(ColorMatcher.Judge(Orange, targetInList).Matched);
        Assert.True(ColorMatcher.Judge(Orange, targetElsewhere).Matched);
    }

    [Fact]
    public void Extra_colours_join_the_list()
    {
        var crit = NoneOf(30, Sky);

        Assert.True(ColorMatcher.Judge(Grey, crit).Matched);
        Assert.False(ColorMatcher.Judge(Grey, crit, extraNoneOf: new[] { Grey }).Matched);
    }

    [Fact]
    public void Extra_colours_are_ignored_by_a_target_check()
    {
        var crit = new ColorCriteria(Grey, 30, ColorSamplingMode.SinglePixel);

        Assert.True(ColorMatcher.Judge(Grey, crit, extraNoneOf: new[] { Grey }).Matched);
    }

    [Fact]
    public void An_empty_list_never_matches()
    {
        var crit = new ColorCriteria(new Rgb(0, 0, 0), 30, ColorSamplingMode.SinglePixel, NoneOf: Array.Empty<Rgb>());

        var r = ColorMatcher.Judge(Orange, crit);

        Assert.False(r.Matched);
        Assert.Null(r.Nearest);
    }

    [Fact]
    public void Evaluate_samples_the_box_then_applies_none_of()
    {
        using var bmp = Solid(Orange);
        var crit = new ColorCriteria(new Rgb(0, 0, 0), 30, ColorSamplingMode.SinglePixel,
            Point: new PickPoint(4, 4), Box: new SampleBox(), NoneOf: new[] { Navy, Grey });

        var r = new ColorMatcher().Evaluate(bmp, crit);

        Assert.True(r.Matched);
        Assert.Equal(Orange, r.Sampled);
    }

    [Fact]
    public void Sample_matches_what_Evaluate_saw()
    {
        using var bmp = Solid(Navy);
        var crit = NoneOf(30, Grey) with { Point = new PickPoint(4, 4), Box = new SampleBox() };

        Assert.Equal(Navy, ColorMatcher.Sample(bmp, crit, null));
    }

    [Fact]
    public void Legacy_target_criteria_are_valid()
    {
        Assert.Null(new ColorCriteria(Grey, 30, ColorSamplingMode.SinglePixel).Validate());
    }

    [Fact]
    public void None_of_with_an_other_colour_is_invalid()
    {
        var crit = new ColorCriteria(Grey, 30, ColorSamplingMode.SinglePixel, Other: Navy, NoneOf: new[] { Sky });

        Assert.Contains("other", crit.Validate(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_empty_none_of_list_is_invalid_unless_a_layer_supplies_colours()
    {
        var crit = NoneOf(30);

        Assert.NotNull(crit.Validate());
        Assert.Null(crit.Validate(layerSupplied: true));
    }

    [Fact]
    public void A_listed_colour_outside_0_to_255_is_invalid()
    {
        Assert.NotNull(NoneOf(30, new Rgb(256, 0, 0)).Validate());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(443)]
    public void Tolerance_outside_0_to_442_is_invalid(int tolerance)
    {
        Assert.NotNull(NoneOf(tolerance, Grey).Validate());
    }

    [Fact]
    public void An_oversized_box_is_invalid()
    {
        var crit = NoneOf(30, Grey) with { Point = new PickPoint(4, 4), Box = new SampleBox(W: 10) };

        Assert.NotNull(crit.Validate());
    }

    [Fact]
    public void None_of_round_trips_through_json()
    {
        var json = JsonSerializer.Serialize(NoneOf(30, Navy, Sky), TriggerJsonOptions.Default);

        Assert.Contains("\"noneOf\"", json);
        var back = JsonSerializer.Deserialize<ColorCriteria>(json, TriggerJsonOptions.Default)!;
        Assert.Equal(new[] { Navy, Sky }, back.NoneOf);
    }

    [Fact]
    public void Target_criteria_json_has_no_none_of_key()
    {
        var json = JsonSerializer.Serialize(new ColorCriteria(Grey, 30, ColorSamplingMode.SinglePixel), TriggerJsonOptions.Default);

        Assert.DoesNotContain("noneOf", json, StringComparison.OrdinalIgnoreCase);
    }
}
