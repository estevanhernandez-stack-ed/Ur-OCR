using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Storage;

public class FinderSetupTests
{
    internal static FinderSetup Valid() => new("grey", 800, 599, Pitch: 50, CenterX: 390, CenterY: 340,
        RadiusBlocks: 5, Outline: new OutlineBox(50, 50),
        Ore: new[] { new OreColour("cyan crystal", new Rgb(60, 220, 230)) }, OreToleranceRgb: 40);

    [Fact]
    public void A_measured_finder_is_valid() => Assert.Null(Valid().Validate());

    /// <summary>A block in a one-block shaft is about 170 px; Ur Task takes a box up to 240.</summary>
    [Fact]
    public void A_shaft_sized_box_is_valid() =>
        Assert.Null((Valid() with { Outline = new OutlineBox(240, 240) }).Validate());

    [Theory]
    [InlineData("pitch", "pitch must be 4 to 599")]
    [InlineData("radius", "radiusBlocks must be 1 to 20")]
    [InlineData("bigBox", "outline must be 1 to 240 pixels a side")]
    [InlineData("minCount", "outline.minCount must be 1 to 2500")]
    [InlineData("whiteMin", "outline.whiteMin must be 1 to 255")]
    [InlineData("centre", "must sit inside the 800x599 game area")]
    [InlineData("oreColour", "Ore colour cyan crystal has a channel outside 0 to 255")]
    [InlineData("oreMissing", "ore is missing")]
    [InlineData("tolerance", "oreToleranceRgb must be 1 to 442")]
    public void A_bad_value_is_named(string what, string expected)
    {
        var v = Valid();
        var bad = what switch
        {
            "pitch" => v with { Pitch = 3 },
            "radius" => v with { RadiusBlocks = 0 },
            "bigBox" => v with { Outline = v.Outline with { W = 241 } },
            "minCount" => v with { Outline = v.Outline with { MinCount = 2501 } },
            "whiteMin" => v with { Outline = v.Outline with { WhiteMin = 0 } },
            "centre" => v with { CenterX = 10 },
            "oreColour" => v with { Ore = new[] { new OreColour("cyan crystal", new Rgb(60, 300, 230)) } },
            "oreMissing" => v with { Ore = null! },
            "tolerance" => v with { OreToleranceRgb = 0 },
            _ => throw new ArgumentOutOfRangeException(nameof(what)),
        };

        Assert.Contains(expected, bad.Validate());
    }
}
