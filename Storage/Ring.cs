using System.Text.Json.Serialization;

namespace RoRoRo.UrOcr.Storage;

/// <summary>One layer of a mine: the colours its plain rock shows at a ring spot.</summary>
public sealed record LayerDefinition(string Name, IReadOnlyList<Rgb> Rock);

/// <summary>
/// A ring of spot triggers around the character that share one layer reading.
/// The current layer is the one whose rock the most spots are within tolerance
/// of, provided at least <see cref="MinLayerSpots"/> spots agree. Stored in
/// triggers.json next to the triggers; spots point at it by <see cref="Id"/>.
/// </summary>
/// <remarks>Finders: the ore finder per layer (spec "Reach, measured, and the ore finder"), null on a
/// ring imported without one; left out of triggers.json when null, so older files round-trip unchanged.
/// LayerMinShare and LayerLead: the pulse's layer read by colour share (spec "The layer is read by colour
/// share, not 8 spots"), null for the defaults; left out of triggers.json when null, like Finders.</remarks>
public sealed record RingDefinition(string Id, string Name, IReadOnlyList<LayerDefinition> Layers, int MinLayerSpots = 3,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<FinderSetup>? Finders = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? LayerMinShare = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? LayerLead = null);

/// <summary>Marks a colour trigger as a spot of a ring. Order is the ring order: lower goes first.</summary>
public sealed record RingSpot(string RingId, int Order);

public enum LayerCondition { SameLayer, NoLayer }

/// <summary>
/// Criteria of a <see cref="TriggerMode.Layer"/> trigger. SameLayer matches
/// while the ring is on a layer; with <see cref="Trigger.HoldForMs"/> that reads
/// "on the same layer for N ms" (a layer change restarts the hold). NoLayer
/// matches while the ring is visible but no layer is current.
/// </summary>
public sealed record LayerCriteria(string RingId, LayerCondition Condition);
