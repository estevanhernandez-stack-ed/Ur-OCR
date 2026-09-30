using RoRoRo.UrOcr.Ipc;

namespace RoRoRo.UrOcr.Storage;

/// <summary>Whether a pulse can run. Null means valid; otherwise one sentence naming the first problem.</summary>
public static class PulseValidation
{
    public const int SpotCount = 8;
    public const int MinBurstMs = 100;
    public const int MaxBurstMs = 60_000;
    public const int MaxSettleMs = 10_000;
    public const int MaxRockCapMinutes = 60;
    /// <summary>The dwell range Ur Task's SweepPath takes.</summary>
    public const int MinSweepDwellMs = 50;
    public const int MaxSweepDwellMs = 5000;
    /// <summary>The step range Ur Task's SweepPath takes, for the account's fallback block size.</summary>
    public const int MinSweepBlockPx = BridgeContract.MinSweepStep;
    public const int MaxSweepBlockPx = BridgeContract.MaxSweepStep;

    /// <summary>The ring's spot triggers in ring order, enabled or not: the pulse reads their
    /// positions and sample boxes even when the 0.5.0 triggers themselves are switched off.</summary>
    public static IReadOnlyList<Trigger> SpotsOf(string ringId, IReadOnlyList<Trigger> triggers) =>
        triggers
            .Where(t => t.Mode == TriggerMode.Color && t.Color is not null && t.Ring is { } r
                        && string.Equals(r.RingId, ringId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Ring!.Order)
            .ToList();

    /// <summary>The ring's ore finder for the 1-based aim layer, or null: no finders, none for that
    /// layer, or a layer out of range. Null means the pulse clears with the eight Clear spot macros.</summary>
    public static FinderSetup? FinderFor(RingDefinition ring, int aimLayer)
    {
        if (ring.Finders is null || aimLayer < 1 || aimLayer > ring.Layers.Count) return null;
        var name = ring.Layers[aimLayer - 1].Name;
        return ring.Finders.FirstOrDefault(f => f is not null && string.Equals(f.Layer, name, StringComparison.OrdinalIgnoreCase));
    }

    public static string? Validate(PulseConfig p, IReadOnlyList<RingDefinition> rings, IReadOnlyList<Trigger> triggers)
    {
        if (p.AccountUserId <= 0) return "accountUserId must be the account's Roblox user id.";
        if (string.IsNullOrWhiteSpace(p.RingId)) return "ringId is missing.";
        var ring = TriggerValidation.Find(rings, p.RingId);
        if (ring is null) return $"Ring {p.RingId} is not defined: import the ring first (--import-ring).";
        if (TriggerValidation.Validate(ring) is { } ringProblem) return $"Ring {p.RingId}: {ringProblem}";

        var layers = ring.Layers.Count;
        if (p.TargetLayer < 1 || p.TargetLayer > layers)
            return $"targetLayer must be 1 to {layers} (ring {p.RingId}'s layers, top first), not {p.TargetLayer}.";
        if (p.Mode == PulseMode.OneAbove && p.TargetLayer < 2)
            return "Mode oneAbove needs targetLayer 2 or more: nothing is above layer 1.";
        if (p.BurstMs < MinBurstMs || p.BurstMs > MaxBurstMs)
            return $"burstMs must be {MinBurstMs} to {MaxBurstMs}, not {p.BurstMs}.";
        if (p.SettleMs < 0 || p.SettleMs > MaxSettleMs)
            return $"settleMs must be 0 to {MaxSettleMs}, not {p.SettleMs}.";
        if (p.RockCapMinutes < 1 || p.RockCapMinutes > MaxRockCapMinutes)
            return $"rockCapMinutes must be 1 to {MaxRockCapMinutes}, not {p.RockCapMinutes}.";
        if (p.SweepDwellMs < MinSweepDwellMs || p.SweepDwellMs > MaxSweepDwellMs)
            return $"sweepDwellMs must be {MinSweepDwellMs} to {MaxSweepDwellMs}, not {p.SweepDwellMs}.";
        if (p.SweepBlockPx is < MinSweepBlockPx or > MaxSweepBlockPx)
            return $"sweepBlockPx must be {MinSweepBlockPx} to {MaxSweepBlockPx} (the step Ur Task takes), not {p.SweepBlockPx}.";
        if (FinderFor(ring, p.AimLayer) is { } finder && finder.Validate() is { } finderProblem)
            return $"Ring {p.RingId}'s ore finder for {finder.Layer}: {finderProblem}";

        var orders = SpotsOf(p.RingId, triggers).Select(t => t.Ring!.Order).ToList();
        if (!orders.SequenceEqual(Enumerable.Range(0, SpotCount)))
            return $"Ring {p.RingId} needs {SpotCount} spot triggers, orders 0 to {SpotCount - 1}; it has {orders.Count}. Import the ring first (--import-ring).";

        var m = p.Macros;
        if (m is null) return "macros are missing: import the pulse with --import-pulse, which looks them up in Ur Task.";
        if (string.IsNullOrWhiteSpace(m.AutoMineOff) || string.IsNullOrWhiteSpace(m.AutoMineOn) || string.IsNullOrWhiteSpace(m.GoToTop))
            return "macros must name Auto Mine off, Auto Mine on and Go to Top.";
        if (m.Clear is null || m.Clear.Count != SpotCount || m.Clear.Any(string.IsNullOrWhiteSpace))
            return $"macros.clear must list {SpotCount} macro ids, one per spot in ring order.";
        return null;
    }
}
