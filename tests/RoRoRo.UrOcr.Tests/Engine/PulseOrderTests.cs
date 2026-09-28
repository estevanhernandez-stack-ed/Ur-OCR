using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

public class PulseOrderTests
{
    private static readonly Rgb Grey = new(120, 120, 120);
    private static readonly Rgb Grey2 = new(100, 100, 100);
    private static readonly Rgb Orange = new(240, 160, 40);
    private static readonly Rgb Reddish = new(160, 120, 120);   // 40 from grey: ore, but near it
    private static readonly Rgb Sky = new(135, 206, 235);
    private static readonly IReadOnlyList<Rgb> Rock = new[] { Grey, Grey2 };

    private static IReadOnlyList<SpotReading> Ring(params (int Order, Rgb Colour)[] overrides)
    {
        var list = new List<SpotReading>();
        for (var o = 0; o < 8; o++)
        {
            var c = Grey;
            foreach (var (order, colour) in overrides)
                if (order == o) c = colour;
            list.Add(new SpotReading(o, c, 20, new[] { Sky }));
        }
        return list;
    }

    [Fact]
    public void All_rock_goes_in_ring_order() =>
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, PulseOrder.Rank(Ring(), Rock));

    [Fact]
    public void Ore_goes_first_furthest_from_the_rock_first() =>
        Assert.Equal(new[] { 6, 1, 0, 2, 3, 4, 5, 7 }, PulseOrder.Rank(Ring((1, Reddish), (6, Orange)), Rock));

    [Fact]
    public void An_ignore_colour_is_not_ore() =>
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, PulseOrder.Rank(Ring((3, Sky)), Rock));

    [Fact]
    public void Equal_ores_keep_ring_order() =>
        Assert.Equal(new[] { 2, 5, 0, 1, 3, 4, 6, 7 }, PulseOrder.Rank(Ring((5, Orange), (2, Orange)), Rock));

    [Fact]
    public void Within_tolerance_of_any_rock_colour_is_not_ore()
    {
        Assert.False(PulseOrder.IsOre(new SpotReading(0, new Rgb(105, 105, 105), 20, Array.Empty<Rgb>()), Rock));
        Assert.True(PulseOrder.IsOre(new SpotReading(0, Orange, 20, Array.Empty<Rgb>()), Rock));
    }
}
