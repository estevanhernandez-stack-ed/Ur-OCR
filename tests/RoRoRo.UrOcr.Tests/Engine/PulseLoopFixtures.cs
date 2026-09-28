using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Tests.Engine;

internal static class PulseFixtures
{
    public static readonly Rgb Navy = new(30, 30, 90);
    public static readonly Rgb Black = new(10, 10, 12);
    public static readonly Rgb Grey = new(120, 120, 120);
    public static readonly Rgb Orange = new(240, 160, 40);
    public static readonly Rgb Sky = new(135, 206, 235);

    /// <summary>Three layers, top first: navy (1), black (2), grey (3).</summary>
    public static RingDefinition Ring() => new("mine8", "Mine #8", new[]
    {
        new LayerDefinition("navy", new[] { Navy }),
        new LayerDefinition("black", new[] { Black }),
        new LayerDefinition("grey", new[] { Grey }),
    }, MinLayerSpots: 3);

    public static Trigger Spot(int order) => new()
    {
        Id = Guid.NewGuid(),
        Name = $"Mine #8: {MeasuredRing.RingOrder[order]}",
        Region = new RegionRect(100 + order * 20, 100, 9, 9),
        Mode = TriggerMode.Color,
        Color = new ColorCriteria(new Rgb(0, 0, 0), 20, ColorSamplingMode.SinglePixel,
            Point: new PickPoint(4, 4), Box: new SampleBox(), NoneOf: new[] { Sky }),
        Keybind = new KeyCombo("F13", Array.Empty<string>()),
        AccountAware = true,
        Action = TriggerAction.RunMacro,
        MacroId = $"mine-{order}",
        Ring = new RingSpot("mine8", order),
    };

    public static IReadOnlyList<Trigger> Spots() => Enumerable.Range(0, 8).Select(Spot).ToList();

    public static PulseMacros Macros() => new("id-off", "id-on", "id-top",
        MeasuredRing.RingOrder.Select(n => $"id-clear-{n}").ToList());

    public static PulseConfig Config(int target = 3, PulseMode mode = PulseMode.Top, long account = 42) =>
        new(account, "mine8", target, mode, Macros: Macros());
}

internal sealed class ScriptedReader : ISpotReader
{
    public IReadOnlyList<SpotReading>? Next { get; set; }
    public int Reads { get; private set; }

    public IReadOnlyList<SpotReading>? Read(int pid, IReadOnlyList<Trigger> spots)
    {
        Reads++;
        return Next;
    }

    /// <summary>Eight spots of one colour, with per-spot overrides; tolerance 20, sky ignored.</summary>
    public static IReadOnlyList<SpotReading> All(Rgb colour, params (int Order, Rgb Colour)[] overrides)
    {
        var list = new List<SpotReading>();
        for (var o = 0; o < 8; o++)
        {
            var c = colour;
            foreach (var (order, col) in overrides)
                if (order == o) c = col;
            list.Add(new SpotReading(o, c, 20, new[] { PulseFixtures.Sky }));
        }
        return list;
    }
}
