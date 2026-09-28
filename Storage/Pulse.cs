using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoRoRo.UrOcr.Storage;

/// <summary>Top: clear on the target layer. OneAbove: clear on the layer above it, for an
/// under-powered account. JSON "top" / "oneAbove".</summary>
public enum PulseMode { Top, OneAbove }

/// <summary>Ur Task macro ids the loop runs, resolved from names at import. Clear holds one id per
/// ring spot, index = ring order (N, NE, E, SE, S, SW, W, NW).</summary>
public sealed record PulseMacros(string AutoMineOff, string AutoMineOn, string GoToTop, IReadOnlyList<string> Clear);

/// <summary>
/// One account's ore stop pulse (spec 2026-09-28, "Settings per account"). TargetLayer counts the
/// ring's layers from the top, 1-based. Stored in triggers.json under "pulses"; missing keys load
/// as the spec defaults.
/// </summary>
public sealed record PulseConfig(
    long AccountUserId,
    string RingId,
    int TargetLayer,
    PulseMode Mode = PulseMode.Top,
    int BurstMs = PulseConfig.DefaultBurstMs,
    int SettleMs = PulseConfig.DefaultSettleMs,
    int RockCapMinutes = PulseConfig.DefaultRockCapMinutes,
    bool Enabled = true,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PulseMacros? Macros = null)
{
    public const int DefaultBurstMs = 2000;
    public const int DefaultSettleMs = 1000;
    public const int DefaultRockCapMinutes = 5;

    /// <summary>The 1-based layer the loop clears on: the target, or the one above it.</summary>
    [JsonIgnore]
    public int AimLayer => Mode == PulseMode.OneAbove ? TargetLayer - 1 : TargetLayer;
}

/// <summary>The Ur Task macro names the importer looks up. They must match the Ur Task half exactly.</summary>
public static class PulseMacroNames
{
    public const string AutoMineOff = "Auto Mine off (checked)";
    public const string AutoMineOn = "Auto Mine on (checked)";
    public const string GoToTop = "Go to Top";
    public static string Clear(string spot) => $"Clear spot {spot}";
}

/// <summary>The file --import-pulse reads: schema 1 and one entry per account. Any "macros" in it
/// are ignored; the importer looks them up in Ur Task.</summary>
public sealed record PulseFile(int Schema, IReadOnlyList<PulseConfig> Pulses)
{
    public const int CurrentSchema = 1;

    public static PulseFile Load(string path) =>
        JsonSerializer.Deserialize<PulseFile>(File.ReadAllText(path), TriggerJsonOptions.Default)
        ?? throw new InvalidDataException($"{path} is empty.");
}
