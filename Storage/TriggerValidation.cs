namespace RoRoRo.UrOcr.Storage;

/// <summary>
/// Whether a trigger or ring can run. Null means valid; otherwise one sentence
/// naming the first problem, fit for the activity log.
/// </summary>
public static class TriggerValidation
{
    public const int MaxHoldForMs = 3_600_000;

    public static string? Validate(Trigger t, IReadOnlyList<RingDefinition> rings)
    {
        if (t.HoldForMs < 0 || t.HoldForMs > MaxHoldForMs)
            return $"Hold must be 0 to {MaxHoldForMs} ms, not {t.HoldForMs}.";
        if (t.CooldownMs < 0) return "Cooldown cannot be negative.";

        switch (t.Mode)
        {
            case TriggerMode.Color:
                if (t.Color is null) return "A colour trigger needs colour criteria.";
                if (t.Ring is null) return t.Color.Validate();
                if (t.Ring.Order < 0) return "Ring order cannot be negative.";
                if (RingProblem(t.Ring.RingId, rings) is { } spotRing) return spotRing;
                if (t.Color.NoneOf is null) return "A ring spot must be a none-of check.";
                return t.Color.Validate(layerSupplied: true);

            case TriggerMode.Layer:
                if (t.Layer is null) return "A layer trigger needs layer criteria.";
                if (t.Ring is not null) return "A layer trigger cannot also be a ring spot.";
                return RingProblem(t.Layer.RingId, rings);

            case TriggerMode.Text:
                if (t.Text is null) return "A text trigger needs text criteria.";
                return t.Ring is null ? null : "Only colour triggers can be ring spots.";

            default:
                return "Unknown trigger mode.";
        }
    }

    public static string? Validate(RingDefinition ring)
    {
        if (string.IsNullOrWhiteSpace(ring.Id)) return "A ring needs an id.";
        if (ring.MinLayerSpots < 1) return "A ring needs minLayerSpots of at least 1.";
        if (ring.Layers is null || ring.Layers.Count == 0) return "A ring needs at least one layer.";
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in ring.Layers)
        {
            if (layer is null || string.IsNullOrWhiteSpace(layer.Name)) return "Every layer needs a name.";
            if (!names.Add(layer.Name)) return $"Two layers are named {layer.Name}.";
            if (layer.Rock is null || layer.Rock.Count == 0) return $"Layer {layer.Name} lists no rock colours.";
            if (layer.Rock.Any(c => c is null || !ColorCriteria.InRange(c)))
                return $"Layer {layer.Name} has a rock colour outside 0 to 255.";
        }
        return null;
    }

    public static RingDefinition? Find(IReadOnlyList<RingDefinition> rings, string id) =>
        rings.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));

    private static string? RingProblem(string ringId, IReadOnlyList<RingDefinition> rings)
    {
        var ring = Find(rings, ringId);
        if (ring is null) return $"Ring {ringId} is not defined.";
        return Validate(ring) is { } problem ? $"Ring {ringId}: {problem}" : null;
    }
}
