using System.IO;
using System.Text.Json;

namespace RoRoRo.UrOcr.Storage;

public sealed record MeasuredSpot(int Order, string Name, int X, int Y, string Macro);
public sealed record MeasuredLayer(string Name, IReadOnlyList<Rgb> Rock);
public sealed record MeasuredAction(int HoldForMs, string Macro, int CooldownMs = 5000);

/// <summary>
/// The measured-values file the live capture sweep writes (docs/reference/ore-stop/).
/// Ur OCR imports it as a ring (RingImporter); Ur Task's macro generator reads the
/// same file for the spot points. Coordinates are game-area pixels at the recorded
/// client size, recorded at 100% display scale. Macro fields are Ur Task macro names.
/// </summary>
public sealed record MeasuredRing(
    int Schema, string RingId, string Name,
    int RecordedClientW, int RecordedClientH, int ScalePercent,
    int ToleranceRgb, int MinLayerSpots, SampleBox Box,
    IReadOnlyList<MeasuredSpot> Spots, IReadOnlyList<Rgb> Ignore, IReadOnlyList<MeasuredLayer> Layers,
    MeasuredAction RockCap, MeasuredAction Camera, int SpotCooldownMs = 1000)
{
    public const int CurrentSchema = 1;

    /// <summary>Ring order: order 0 is N, then clockwise.</summary>
    public static readonly string[] RingOrder = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    public static MeasuredRing Load(string path) =>
        JsonSerializer.Deserialize<MeasuredRing>(File.ReadAllText(path), TriggerJsonOptions.Default)
        ?? throw new InvalidDataException($"{path} is empty.");

    /// <summary>Null when the file can be imported, else one sentence naming the first problem.</summary>
    public string? Validate()
    {
        if (Schema != CurrentSchema) return $"schema must be {CurrentSchema}, not {Schema}.";
        if (string.IsNullOrWhiteSpace(RingId)) return "ringId is missing.";
        if (string.IsNullOrWhiteSpace(Name)) return "name is missing.";
        if (RecordedClientW < 1 || RecordedClientH < 1) return "recordedClientW and recordedClientH must be positive.";
        if (ScalePercent != 100) return $"The sweep must be recorded at 100% display scale, not {ScalePercent}%.";
        if (ToleranceRgb < 1 || ToleranceRgb > ColorCriteria.MaxTolerance)
            return $"toleranceRgb must be 1 to {ColorCriteria.MaxTolerance}.";
        if (MinLayerSpots < 1 || MinLayerSpots > RingOrder.Length) return $"minLayerSpots must be 1 to {RingOrder.Length}.";
        if (Box is null || !Box.IsValid) return $"box must be 1 to {SampleBox.MaxSide} pixels a side.";
        if (Spots is null || Spots.Count != RingOrder.Length) return $"spots must list exactly {RingOrder.Length} spots.";
        for (var i = 0; i < RingOrder.Length; i++)
        {
            var s = Spots.FirstOrDefault(x => x is not null && x.Order == i);
            if (s is null) return $"No spot has order {i}.";
            if (!string.Equals(s.Name, RingOrder[i], StringComparison.Ordinal))
                return $"Spot {i} must be named {RingOrder[i]}, not {s.Name}.";
            if (s.X < 0 || s.Y < 0 || s.X >= RecordedClientW || s.Y >= RecordedClientH)
                return $"Spot {s.Name} ({s.X}, {s.Y}) is outside the {RecordedClientW}x{RecordedClientH} game area.";
            if (string.IsNullOrWhiteSpace(s.Macro)) return $"Spot {s.Name} names no macro.";
        }
        if (Ignore is null) return "ignore is missing (use [] for none).";
        if (Ignore.Any(c => c is null || !ColorCriteria.InRange(c))) return "An ignore colour has a channel outside 0 to 255.";
        if (Layers is null || Layers.Count == 0) return "layers is empty: run ring-fit.ps1 -Write first.";
        var ring = new RingDefinition(RingId, Name,
            Layers.Select(l => new LayerDefinition(l?.Name ?? "", l?.Rock ?? Array.Empty<Rgb>())).ToList(), MinLayerSpots);
        if (TriggerValidation.Validate(ring) is { } ringProblem) return ringProblem;
        foreach (var (label, a) in new[] { ("rockCap", RockCap), ("camera", Camera) })
        {
            if (a is null) return $"{label} is missing.";
            if (a.HoldForMs < 1 || a.HoldForMs > TriggerValidation.MaxHoldForMs)
                return $"{label}.holdForMs must be 1 to {TriggerValidation.MaxHoldForMs}.";
            if (a.CooldownMs < 0) return $"{label}.cooldownMs cannot be negative.";
            if (string.IsNullOrWhiteSpace(a.Macro)) return $"{label} names no macro.";
        }
        if (SpotCooldownMs < 0) return "spotCooldownMs cannot be negative.";
        return null;
    }
}
