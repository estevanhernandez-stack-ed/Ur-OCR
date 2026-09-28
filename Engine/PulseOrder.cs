using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

/// <summary>
/// The order a clearing pass tries the spots in (spec: "ore first ... then stone"). Ore is a calm
/// sample more than its tolerance from every rock colour of the layer and every ignore colour of
/// the spot. Ore goes first, furthest from the rock set first; then the rest in ring order.
/// </summary>
public static class PulseOrder
{
    public static IReadOnlyList<int> Rank(IReadOnlyList<SpotReading> spots, IReadOnlyList<Rgb> rock)
    {
        var scored = spots
            .Select(s => (s.Order, FromRock: Nearest(s.Sampled, rock), Ore: IsOre(s, rock)))
            .ToList();
        var ore = scored.Where(x => x.Ore).OrderByDescending(x => x.FromRock).ThenBy(x => x.Order).Select(x => x.Order);
        var rest = scored.Where(x => !x.Ore).OrderBy(x => x.Order).Select(x => x.Order);
        return ore.Concat(rest).ToList();
    }

    public static bool IsOre(SpotReading s, IReadOnlyList<Rgb> rock) =>
        Nearest(s.Sampled, rock) > s.ToleranceRgb && Nearest(s.Sampled, s.Ignore) > s.ToleranceRgb;

    /// <summary>Distance to the nearest colour of the set; +Infinity for an empty set.</summary>
    public static double Nearest(Rgb c, IReadOnlyList<Rgb> set)
    {
        var best = double.PositiveInfinity;
        foreach (var s in set)
        {
            var d = ColorMatcher.Distance(c, s);
            if (d < best) best = d;
        }
        return best;
    }
}
