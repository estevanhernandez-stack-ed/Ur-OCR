using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

public enum RingStatus { Unknown, NoLayer, OnLayer }

/// <summary>
/// What a ring reads now. Unknown: no spot could be sampled (not the foreground
/// alt, window gone). NoLayer: spots were sampled but fewer than MinLayerSpots
/// agree on any layer. OnLayer: <see cref="Layer"/> is current. Since is when
/// this status and layer began. Votes is how many spots matched the best layer,
/// out of Spots sampled.
/// </summary>
public sealed record RingState(RingStatus Status, string? Layer, DateTimeOffset Since, int Votes, int Spots)
{
    public static readonly RingState Initial = new(RingStatus.Unknown, null, DateTimeOffset.MinValue, 0, 0);

    public string Describe() => Status switch
    {
        RingStatus.OnLayer => $"layer {Layer} ({Votes} of {Spots} spots)",
        RingStatus.NoLayer => $"no layer ({Votes} of {Spots} spots at best)",
        _ => "ring not visible",
    };
}

/// <summary>One ring spot's sampled colour this tick, with that spot's tolerance.</summary>
public sealed record SpotSample(int Order, Rgb Sampled, int ToleranceRgb);

/// <summary>
/// The current layer of every ring. The coordinator feeds it each tick; the
/// activity log, ur-ocr.log and layer triggers read it. Thread-safe: the UI may
/// read while the coordinator writes.
/// </summary>
public sealed class RingTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<string, RingState> _states = new(StringComparer.OrdinalIgnoreCase);

    public RingState Get(string ringId)
    {
        lock (_gate) return GetLocked(ringId);
    }

    public IReadOnlyDictionary<string, RingState> Snapshot()
    {
        lock (_gate) return new Dictionary<string, RingState>(_states, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Votes the layer from this tick's samples. Changed is true when status or layer changed.</summary>
    public (RingState State, bool Changed) Update(RingDefinition ring, IReadOnlyList<SpotSample> samples, DateTimeOffset now)
    {
        if (samples.Count == 0) return MarkUnknown(ring.Id, now);
        var votes = Vote(ring, samples);
        var top = votes.Count == 0 ? 0 : votes.Values.Max();
        lock (_gate)
        {
            var prev = GetLocked(ring.Id);
            var layer = Pick(ring, votes, prev.Layer);
            var status = layer is null ? RingStatus.NoLayer : RingStatus.OnLayer;
            return SetLocked(ring.Id, prev, status, layer, top, samples.Count, now);
        }
    }

    public (RingState State, bool Changed) MarkUnknown(string ringId, DateTimeOffset now)
    {
        lock (_gate)
        {
            var prev = GetLocked(ringId);
            return SetLocked(ringId, prev, RingStatus.Unknown, null, 0, 0, now);
        }
    }

    /// <summary>For each layer, how many samples are within their tolerance of any of its rock colours.</summary>
    public static IReadOnlyDictionary<string, int> Vote(RingDefinition ring, IReadOnlyList<SpotSample> samples)
    {
        var votes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in ring.Layers)
        {
            var n = 0;
            foreach (var s in samples)
                if (layer.Rock.Any(rock => ColorMatcher.Distance(s.Sampled, rock) <= s.ToleranceRgb)) n++;
            votes[layer.Name] = n;
        }
        return votes;
    }

    /// <summary>
    /// The layer with the most votes, if it has at least MinLayerSpots. A tie keeps
    /// <paramref name="previous"/> when it is among the tied, else the first in list order.
    /// </summary>
    public static string? Pick(RingDefinition ring, IReadOnlyDictionary<string, int> votes, string? previous)
    {
        var top = votes.Count == 0 ? 0 : votes.Values.Max();
        if (top < ring.MinLayerSpots) return null;
        if (previous is not null && votes.TryGetValue(previous, out var p) && p == top)
            return ring.Layers.First(l => string.Equals(l.Name, previous, StringComparison.OrdinalIgnoreCase)).Name;
        return ring.Layers.First(l => votes[l.Name] == top).Name;
    }

    private RingState GetLocked(string ringId) =>
        _states.TryGetValue(ringId, out var s) ? s : RingState.Initial;

    private (RingState, bool) SetLocked(string ringId, RingState prev, RingStatus status, string? layer,
        int votes, int spots, DateTimeOffset now)
    {
        var same = prev.Status == status && string.Equals(prev.Layer, layer, StringComparison.OrdinalIgnoreCase);
        var next = new RingState(status, layer, same ? prev.Since : now, votes, spots);
        _states[ringId] = next;
        return (next, !same);
    }
}
