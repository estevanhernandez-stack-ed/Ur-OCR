using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

/// <summary>One ring spot's calm sample, with that spot's tolerance and ignore colours.</summary>
public sealed record SpotReading(int Order, Rgb Sampled, int ToleranceRgb, IReadOnlyList<Rgb> Ignore);

public interface ISpotReader
{
    /// <summary>Samples each ring spot in <paramref name="pid"/>'s window. Null when the window
    /// cannot be resolved (hidden, closed, mid-resize).</summary>
    IReadOnlyList<SpotReading>? Read(int pid, IReadOnlyList<Trigger> spots);
}

/// <summary>
/// Reads ring spots the way the trigger coordinator does: the same region resolution and the same
/// averaged box around the pick point, so the pulse and the 0.5.0 triggers see the same pixels.
/// </summary>
public sealed class SpotReader(ICaptureSource capture, IWindowMetrics metrics) : ISpotReader
{
    public IReadOnlyList<SpotReading>? Read(int pid, IReadOnlyList<Trigger> spots)
    {
        var result = new List<SpotReading>(spots.Count);
        foreach (var t in spots)
        {
            if (t.Ring is not { } spot || t.Color is not { } colour) continue;
            var region = TriggerRegionResolver.Resolve(t, t.IsClientSpace ? pid : 0, metrics);
            if (region is null || region.Width < 1 || region.Height < 1) return null;
            using var bmp = capture.Capture(region);
            result.Add(new SpotReading(spot.Order, ColorMatcher.Sample(bmp, colour, t.Region),
                colour.ToleranceRgb, colour.NoneOf ?? Array.Empty<Rgb>()));
        }
        return result;
    }
}
