using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoRoRo.UrOcr.Storage;

/// <summary>Top: clear on the target layer. OneAbove: clear on the layer above it, for an
/// under-powered account. JSON "top" / "oneAbove".</summary>
public enum PulseMode { Top, OneAbove }

/// <summary>Ur Task macro ids the loop runs, resolved from names at import. Clear holds one id per
/// ring spot, index = ring order (N, NE, E, SE, S, SW, W, NW). CameraTurnLeft is optional: null (not
/// in Ur Task, or a file from before it) and a pass with nothing in reach rides a burst without
/// looking around first. CameraTopDown is optional too: set, the pulse runs it at the start and
/// after every Go to Top (pitch and zoom); null leaves the camera as it is.</summary>
public sealed record PulseMacros(string AutoMineOff, string AutoMineOn, string GoToTop, IReadOnlyList<string> Clear,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CameraTurnLeft = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CameraTopDown = null);

/// <summary>
/// One account's ore stop pulse (spec 2026-09-28, "Settings per account"). TargetLayer counts the
/// ring's layers from the top, 1-based. SweepDwellMs and SweepNearSide are the sweep's per-account
/// settings (ore-stop sweep spec: dwell per account, default 400 ms; near-side rows, default on).
/// SweepBlockPx is the block size the sweep steps by when none is read off the frame (the read tops
/// out near 100 px, and a block down a shaft is 150 to 180 px), and the pass's block size whenever a
/// read lands far from it (PulseLoop.SettingReadMin/Max); null, the default, sweeps only on a read.
/// OreSweepDwellMs is how long the ore sweep holds on each ore point (default 400 ms, the same range).
/// Usables are optional charges fired by Ur Task hotbar-key macros (PulseUsables); null, the
/// default, fires none. RideFirstMs is the first ride after the start and after every Go to Top,
/// before any calm read (default 8000 ms); RideBurstFarMs is the ride after a calm read 2 or more
/// layers above the aim layer (default 5000 ms). A read 1 layer above rides BurstMs, and so does every
/// burst (live 2026-09-30: 8 stop-read cycles of 2 s rides and 2.5 s reads took 39 s to reach black).
/// Stored in triggers.json under "pulses"; missing keys load as the spec defaults.
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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PulseMacros? Macros = null,
    int SweepDwellMs = PulseConfig.DefaultSweepDwellMs,
    bool SweepNearSide = true,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? SweepBlockPx = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PulseUsables? Usables = null,
    int OreSweepDwellMs = PulseConfig.DefaultOreSweepDwellMs,
    int RideFirstMs = PulseConfig.DefaultRideFirstMs,
    int RideBurstFarMs = PulseConfig.DefaultRideBurstFarMs)
{
    /// <summary>The first ride from the top (start, Go to Top): no read yet, and the upper layers take
    /// longer than one burst to get through (owner, 2026-09-30).</summary>
    public const int DefaultRideFirstMs = 8000;
    /// <summary>The ride after a read 2 or more layers above the aim layer.</summary>
    public const int DefaultRideBurstFarMs = 5000;
    /// <summary>How long the ore sweep holds on each ore point: most ore broke in one 0.3 s hold with
    /// damage boosters (live 2026-09-30); what survives goes to ClearAt on the next pass.</summary>
    public const int DefaultOreSweepDwellMs = 400;
    /// <summary>How long the sweep holds on each block (owner decision 2): the main breaks a
    /// bottom-layer block in about 0.3 s; a weaker pickaxe needs longer.</summary>
    public const int DefaultSweepDwellMs = 400;
    public const int DefaultBurstMs = 2000;
    public const int DefaultSettleMs = 1000;
    public const int DefaultRockCapMinutes = 5;

    /// <summary>The 1-based layer the loop clears on: the target, or the one above it.</summary>
    [JsonIgnore]
    public int AimLayer => Mode == PulseMode.OneAbove ? TargetLayer - 1 : TargetLayer;
}

/// <summary>One usable: an Ur Task macro, by id, that fires a charge (a hotbar key), and the least
/// time between two fires. JSON "macro" / "everyMs".</summary>
public sealed record PulseUsable(string Macro, int EveryMs);

/// <summary>
/// An account's usables, each optional. Ride fires once Auto Mine is on in a ride, and again each
/// everyMs while the ride lasts, only while the pulse is
/// sure it is above the aim layer (a Rover bomb on the way down): the last calm read named a layer
/// above it, and no read has reached the aim layer or deeper since the last Go to Top; Target fires before a pass on the aim
/// layer, then the loop settles and reads again, since the character may drop (a Core Charge). Never
/// while held (pause, dry run) or going to top. A usable Ur Task refuses, or that fails, is skipped
/// that time and the loop goes on.
/// </summary>
public sealed record PulseUsables(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PulseUsable? Ride = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PulseUsable? Target = null);

/// <summary>The Ur Task macro names the importer looks up. They must match the Ur Task half exactly.</summary>
public static class PulseMacroNames
{
    public const string AutoMineOff = "Auto Mine off (checked)";
    public const string AutoMineOn = "Auto Mine on (checked)";
    public const string GoToTop = "Go to Top";
    /// <summary>Optional: a quarter turn of the camera, the character staying centred.</summary>
    public const string CameraTurnLeft = "Camera turn left";
    /// <summary>Optional: the digging view, pitch and zoom, set from any camera.</summary>
    public const string CameraTopDown = "Camera top-down";
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
