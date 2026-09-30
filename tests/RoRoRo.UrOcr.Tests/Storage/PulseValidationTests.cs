using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Storage;

public class PulseValidationTests
{
    private static RingDefinition Ring() => new("mine8", "Mine #8", new[]
    {
        new LayerDefinition("navy", new[] { new Rgb(30, 30, 90) }),
        new LayerDefinition("black", new[] { new Rgb(10, 10, 12) }),
        new LayerDefinition("grey", new[] { new Rgb(120, 120, 120) }),
    }, MinLayerSpots: 3);

    private static Trigger Spot(int order, string ringId = "mine8", bool enabled = true) => new()
    {
        Id = Guid.NewGuid(),
        Name = $"Mine #8: {MeasuredRing.RingOrder[order]}",
        Enabled = enabled,
        Region = new RegionRect(100 + order * 20, 100, 9, 9),
        Mode = TriggerMode.Color,
        Color = new ColorCriteria(new Rgb(0, 0, 0), 20, ColorSamplingMode.SinglePixel,
            Point: new PickPoint(4, 4), Box: new SampleBox(), NoneOf: new[] { new Rgb(135, 206, 235) }),
        Keybind = new KeyCombo("F13", Array.Empty<string>()),
        Ring = new RingSpot(ringId, order),
    };

    private static IReadOnlyList<Trigger> Spots(int count = 8) => Enumerable.Range(0, count).Select(o => Spot(o)).ToList();

    private static PulseMacros Macros(int clears = 8) => new("id-off", "id-on", "id-top",
        MeasuredRing.RingOrder.Take(clears).Select(n => $"id-clear-{n}").ToList());

    private static PulseConfig Pulse() => new(42, "mine8", 3, Macros: Macros());

    private static string? Check(PulseConfig p, IReadOnlyList<Trigger>? spots = null, params RingDefinition[] rings) =>
        PulseValidation.Validate(p, rings.Length == 0 ? new[] { Ring() } : rings, spots ?? Spots());

    [Fact]
    public void A_complete_pulse_is_valid() => Assert.Null(Check(Pulse()));

    [Fact]
    public void The_account_must_be_set() =>
        Assert.Contains("accountUserId", Check(Pulse() with { AccountUserId = 0 }));

    [Fact]
    public void The_ring_must_be_imported_first() =>
        Assert.Contains("import the ring first", Check(Pulse() with { RingId = "mine9" }));

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void The_target_must_be_one_of_the_rings_layers(int target) =>
        Assert.Contains("targetLayer must be 1 to 3", Check(Pulse() with { TargetLayer = target }));

    [Fact]
    public void One_above_needs_a_layer_above_the_target() =>
        Assert.Contains("nothing is above layer 1", Check(Pulse() with { TargetLayer = 1, Mode = PulseMode.OneAbove }));

    [Fact]
    public void One_above_layer_two_is_fine() =>
        Assert.Null(Check(Pulse() with { TargetLayer = 2, Mode = PulseMode.OneAbove }));

    [Theory]
    [InlineData(50, 1000, 5, "burstMs")]
    [InlineData(60001, 1000, 5, "burstMs")]
    [InlineData(2000, -1, 5, "settleMs")]
    [InlineData(2000, 10001, 5, "settleMs")]
    [InlineData(2000, 1000, 0, "rockCapMinutes")]
    [InlineData(2000, 1000, 61, "rockCapMinutes")]
    public void Timings_must_be_in_range(int burst, int settle, int cap, string field) =>
        Assert.Contains(field, Check(Pulse() with { BurstMs = burst, SettleMs = settle, RockCapMinutes = cap }));

    [Theory]
    [InlineData(49)]
    [InlineData(5001)]
    public void The_sweep_dwell_must_be_in_range(int dwell) =>
        Assert.Contains($"sweepDwellMs must be 50 to 5000, not {dwell}", Check(Pulse() with { SweepDwellMs = dwell }));

    [Fact]
    public void The_sweep_dwell_limits_are_valid()
    {
        Assert.Null(Check(Pulse() with { SweepDwellMs = 50 }));
        Assert.Null(Check(Pulse() with { SweepDwellMs = 5000 }));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(241)]
    [InlineData(0)]
    public void The_sweep_block_size_must_be_in_range(int px) =>
        Assert.Contains($"sweepBlockPx must be 8 to 240 (the step Ur Task takes), not {px}", Check(Pulse() with { SweepBlockPx = px }));

    [Fact]
    public void The_sweep_block_size_limits_and_no_setting_are_valid()
    {
        Assert.Null(Check(Pulse() with { SweepBlockPx = 8 }));
        Assert.Null(Check(Pulse() with { SweepBlockPx = 240 }));
        Assert.Null(Check(Pulse() with { SweepBlockPx = null }));
    }

    [Fact]
    public void The_ring_needs_all_eight_spot_triggers() =>
        Assert.Contains("needs 8 spot triggers", Check(Pulse(), Spots(7)));

    [Fact]
    public void The_macros_must_be_resolved() =>
        Assert.Contains("macros are missing", Check(Pulse() with { Macros = null }));

    [Fact]
    public void One_clear_macro_per_spot() =>
        Assert.Contains("macros.clear", Check(Pulse() with { Macros = Macros(clears: 7) }));

    [Fact]
    public void Blank_toggle_macros_are_refused() =>
        Assert.Contains("Auto Mine off", Check(Pulse() with { Macros = Macros() with { AutoMineOn = " " } }));

    private static RingDefinition RingWith(FinderSetup finder) => Ring() with { Finders = new[] { finder } };

    [Fact]
    public void FinderFor_picks_the_finder_of_the_aim_layer()
    {
        var ring = RingWith(FinderSetupTests.Valid());      // a finder for grey, layer 3

        Assert.Equal("grey", PulseValidation.FinderFor(ring, 3)!.Layer);
        Assert.Null(PulseValidation.FinderFor(ring, 2));
        Assert.Null(PulseValidation.FinderFor(ring, 4));
        Assert.Null(PulseValidation.FinderFor(Ring(), 3));
    }

    [Fact]
    public void A_bad_finder_on_the_aim_layer_fails_the_pulse() =>
        Assert.Contains("Ring mine8's ore finder for grey: pitch must be 4 to 599",
            Check(Pulse(), null, RingWith(FinderSetupTests.Valid() with { Pitch = 3 })));

    [Fact]
    public void A_bad_finder_on_another_layer_leaves_the_pulse_valid() =>
        Assert.Null(Check(Pulse(), null, RingWith(FinderSetupTests.Valid() with { Layer = "navy", Pitch = 3 })));

    [Fact]
    public void SpotsOf_takes_this_rings_colour_spots_in_ring_order_enabled_or_not()
    {
        var triggers = new List<Trigger> { Spot(2), Spot(0, enabled: false), Spot(1, ringId: "other"), Spot(1) };

        var spots = PulseValidation.SpotsOf("MINE8", triggers);

        Assert.Equal(new[] { 0, 1, 2 }, spots.Select(t => t.Ring!.Order));
        Assert.All(spots, t => Assert.Equal("mine8", t.Ring!.RingId));
    }

    [Theory]
    [InlineData(999)]
    [InlineData(600_001)]
    public void A_usable_every_out_of_range_is_refused(int ms)
    {
        var p = Pulse() with { Usables = new PulseUsables(Ride: new PulseUsable("id-rover", ms)) };

        Assert.Equal($"usables.ride.everyMs must be 1000 to 600000, not {ms}.", Check(p));
    }

    [Fact]
    public void A_usable_with_no_macro_is_refused()
    {
        var p = Pulse() with { Usables = new PulseUsables(Target: new PulseUsable(" ", 20000)) };

        Assert.Equal("usables.target.macro must be an Ur Task macro id.", Check(p));
    }

    [Fact]
    public void Usables_are_optional_each()
    {
        Assert.Null(Check(Pulse() with { Usables = new PulseUsables() }));
        Assert.Null(Check(Pulse() with { Usables = new PulseUsables(Ride: new PulseUsable("id-rover", 1000)) }));
        Assert.Null(Check(Pulse() with { Usables = new PulseUsables(Target: new PulseUsable("id-core", 600_000)) }));
    }
}
