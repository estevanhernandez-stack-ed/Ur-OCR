using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Storage;

public class TriggerValidationTests
{
    private static readonly Rgb Sky = new(135, 206, 235);

    private static RingDefinition Ring(params LayerDefinition[] layers) => new("mine8", "Mine #8",
        layers.Length > 0 ? layers : new[] { new LayerDefinition("navy", new[] { new Rgb(30, 30, 90) }) });

    private static IReadOnlyList<RingDefinition> Rings(params RingDefinition[] rings) => rings;

    private static Trigger Colour(ColorCriteria? c = null, RingSpot? ring = null) => new()
    {
        Id = Guid.NewGuid(), Name = "t",
        Region = new RegionRect(0, 0, 9, 9), Mode = TriggerMode.Color,
        Color = c ?? new ColorCriteria(new Rgb(0, 0, 0), 30, ColorSamplingMode.SinglePixel),
        Keybind = new KeyCombo("F13", Array.Empty<string>()),
        Ring = ring,
    };

    private static ColorCriteria NoneOf(params Rgb[] colours) =>
        new(new Rgb(0, 0, 0), 30, ColorSamplingMode.SinglePixel, NoneOf: colours);

    private static Trigger LayerTrigger(LayerCriteria? criteria, int hold = 0) => new()
    {
        Id = Guid.NewGuid(), Name = "layer",
        Region = new RegionRect(0, 0, 1, 1), Mode = TriggerMode.Layer,
        Layer = criteria, HoldForMs = hold,
        Keybind = new KeyCombo("F13", Array.Empty<string>()),
    };

    [Fact]
    public void A_plain_colour_trigger_is_valid()
    {
        Assert.Null(TriggerValidation.Validate(Colour(), Rings()));
    }

    [Fact]
    public void A_colour_trigger_without_criteria_is_invalid()
    {
        var t = Colour();
        t.Color = null;

        Assert.NotNull(TriggerValidation.Validate(t, Rings()));
    }

    [Fact]
    public void A_ring_spot_on_an_unknown_ring_is_invalid()
    {
        var t = Colour(NoneOf(Sky), new RingSpot("nope", 0));

        Assert.Contains("nope", TriggerValidation.Validate(t, Rings(Ring())));
    }

    [Fact]
    public void A_ring_spot_must_be_a_none_of_check()
    {
        var t = Colour(ring: new RingSpot("mine8", 0));

        Assert.NotNull(TriggerValidation.Validate(t, Rings(Ring())));
    }

    [Fact]
    public void A_ring_spot_may_have_an_empty_none_of_list()
    {
        var t = Colour(NoneOf(), new RingSpot("mine8", 0));

        Assert.Null(TriggerValidation.Validate(t, Rings(Ring())));
    }

    [Fact]
    public void A_negative_ring_order_is_invalid()
    {
        var t = Colour(NoneOf(Sky), new RingSpot("mine8", -1));

        Assert.NotNull(TriggerValidation.Validate(t, Rings(Ring())));
    }

    [Fact]
    public void A_spot_on_an_invalid_ring_reports_the_ring_problem()
    {
        var broken = Ring(new LayerDefinition("navy", Array.Empty<Rgb>()));
        var t = Colour(NoneOf(Sky), new RingSpot("mine8", 0));

        Assert.Contains("rock", TriggerValidation.Validate(t, Rings(broken)));
    }

    [Fact]
    public void A_layer_trigger_needs_its_ring()
    {
        Assert.Null(TriggerValidation.Validate(LayerTrigger(new LayerCriteria("mine8", LayerCondition.NoLayer)), Rings(Ring())));
        Assert.NotNull(TriggerValidation.Validate(LayerTrigger(new LayerCriteria("mine8", LayerCondition.NoLayer)), Rings()));
    }

    [Fact]
    public void A_layer_trigger_without_criteria_is_invalid()
    {
        Assert.NotNull(TriggerValidation.Validate(LayerTrigger(null), Rings(Ring())));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3_600_001)]
    public void A_hold_outside_0_to_an_hour_is_invalid(int hold)
    {
        var t = LayerTrigger(new LayerCriteria("mine8", LayerCondition.SameLayer), hold);

        Assert.NotNull(TriggerValidation.Validate(t, Rings(Ring())));
    }

    [Fact]
    public void A_text_trigger_cannot_be_a_ring_spot()
    {
        var t = new Trigger
        {
            Id = Guid.NewGuid(), Name = "text", Region = new RegionRect(0, 0, 9, 9),
            Mode = TriggerMode.Text, Text = new TextCriteria("ore", false, TextMatchType.Contains),
            Keybind = new KeyCombo("F13", Array.Empty<string>()), Ring = new RingSpot("mine8", 0),
        };

        Assert.NotNull(TriggerValidation.Validate(t, Rings(Ring())));
    }

    [Fact]
    public void A_ring_without_layers_is_invalid()
    {
        Assert.NotNull(TriggerValidation.Validate(new RingDefinition("mine8", "Mine #8", Array.Empty<LayerDefinition>())));
    }

    [Fact]
    public void Duplicate_layer_names_are_invalid()
    {
        var ring = Ring(new LayerDefinition("navy", new[] { new Rgb(1, 1, 1) }), new LayerDefinition("NAVY", new[] { new Rgb(2, 2, 2) }));

        Assert.NotNull(TriggerValidation.Validate(ring));
    }

    [Fact]
    public void A_min_layer_spots_below_1_is_invalid()
    {
        Assert.NotNull(TriggerValidation.Validate(Ring() with { MinLayerSpots = 0 }));
    }
}
