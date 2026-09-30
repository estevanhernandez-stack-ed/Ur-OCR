// Ipc/BridgeContract.cs
using System.Text.Json;
using System.Text.Json.Serialization;
using RoRoRo.UrOcr.Storage;
namespace RoRoRo.UrOcr.Ipc;

public sealed record RunMacroRequest(string ContractVersion, string Method, string MacroId,
    IReadOnlyList<string>? Targets, int? InterAltDelayMs, string? CallerPluginId);

public sealed record RunMacroResponse(bool Ok, string? PlaybackId, bool Queued, string? Reason, string? Detail);

/// <summary>Ur Task's GetPlayback request (bridge 1.x, Ur Task 0.9.0 and later).</summary>
public sealed record GetPlaybackRequest(string ContractVersion, string Method, string PlaybackId, string CallerPluginId);

/// <summary>
/// Ur Task's answer: State is running | finished | stopped | failed. On failed, Reason is
/// check-failed, refused, aborted or error, and StepIndex is 1-based. Ok=false with Reason
/// "unknown-playback" means the id is unknown or expired (kept 10 minutes after it ends).
/// </summary>
public sealed record GetPlaybackResponse(bool Ok, string? State, string? Reason, string? Detail, int? StepIndex);

/// <summary>ClearAt's client size: the client the points were measured in. Ur Task sizes the window
/// to it first, as it does for a recorded macro, then plays the points and the box unscaled.</summary>
public sealed record ClearAtClient(int W, int H);

/// <summary>One ClearAt point in pixels of <see cref="ClearAtClient"/>. Label names it in Ur Task's log.</summary>
public sealed record ClearAtPoint(int X, int Y, string Label);

/// <summary>The outline check at every point: a W x H box centred on the point, passing at MinCount
/// or more pixels whose every channel is at least WhiteMin.</summary>
public sealed record ClearAtOutline(int W, int H, int MinCount, int WhiteMin);

/// <summary>The pixel guard checked before every press (bridge 1.x, additive, live safety bug
/// 2026-09-28): a W x H box at (X, Y), its top-left corner, that must show Expect within Tolerance,
/// else Ur Task stops the whole ClearAt (reason check-failed). Left off the wire (WhenWritingNull)
/// when the finder has none, so an Ur Task predating the guard is unaffected.</summary>
public sealed record ClearAtGuard(int X, int Y, int W, int H, Rgb Expect, int Tolerance);

/// <summary>
/// Ur Task's ClearAt (bridge 1.x, additive): every point in order as ONE playback, each a reach hold
/// (hover, outline check, hold-release-look). Answered with a RunMacroResponse and followed with
/// GetPlayback like a macro. MaxMsPerPoint null means no time limit and is always written, as in
/// the spec's example. Guard is optional and left off the wire when null.
/// </summary>
public sealed record ClearAtRequest(string ContractVersion, string Method, string CallerPluginId, string Target,
    ClearAtClient Client, IReadOnlyList<ClearAtPoint> Points, ClearAtOutline Outline,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? MaxMsPerPoint,
    ClearAtGuard? Guard = null);

/// <summary>One SweepPath point in pixels of <see cref="ClearAtClient"/>.</summary>
public sealed record SweepPoint(int X, int Y);

/// <summary>
/// Ur Task's SweepPath (bridge 1.x, additive, Ur Task 0.12.0): one continuous left-button hold along
/// Path, which starts and ends on the start block beside the character and moves a whole number of
/// Step px at a time. Ur Task dwells DwellMs at every point but the last, samples Guard every few
/// points while held, and lets go only on Path[0]. Answered with a RunMacroResponse and followed
/// with GetPlayback like a macro.
/// </summary>
public sealed record SweepPathRequest(string ContractVersion, string Method, string CallerPluginId, string Target,
    ClearAtClient Client, IReadOnlyList<SweepPoint> Path, int Step, int DwellMs, ClearAtGuard Guard);

/// <summary>Reason codes on the bridge, Ur Task's and Ur OCR's own synthetic ones.</summary>
public static class BridgeReasons
{
    public const string Busy = "busy";
    public const string NotRunning = "ur-task-not-running";
    public const string AckTimeout = "ack-timeout";
    public const string NoTargets = "no-targets-resolved";
    public const string UnknownPlayback = "unknown-playback";
    public const string CheckFailed = "check-failed";
    public const string Aborted = "aborted";
    public const string Refused = "refused";
    /// <summary>On a finished playback: it pressed nothing (a Clear spot with no outline). Set by
    /// the Ur Task half of the ore stop pulse.</summary>
    public const string Skipped = "skipped";
}

public static class PlaybackStates
{
    public const string Running = "running";
    public const string Finished = "finished";
    public const string Stopped = "stopped";
    public const string Failed = "failed";
}

public static class BridgeContract
{
    public const string PipeName = "626labs-ur-task";
    public const string Method = "RunMacro";
    public const string MethodGetPlayback = "GetPlayback";
    public const string MethodClearAt = "ClearAt";
    /// <summary>Ur Task refuses a ClearAt with more points than this (or none).</summary>
    public const int MaxClearAtPoints = 64;
    public const string MethodSweepPath = "SweepPath";
    /// <summary>Ur Task refuses a SweepPath with more points than this (or fewer than 3).</summary>
    public const int MaxSweepPoints = 256;
    /// <summary>Ur Task refuses a SweepPath step outside this range.</summary>
    public const int MinSweepStep = 8;
    /// <summary>Ur Task refuses a SweepPath step outside this range.</summary>
    public const int MaxSweepStep = 240;
    public const string CallerId = "626labs.ur-ocr";
    public const string ContractVersion = "1.0";

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>interAltDelayMs: Ur Task's wait between focusing a target and playing; null
    /// leaves it to the macro (500 ms by default) and is left off the wire.</summary>
    public static RunMacroRequest ForMacro(string macroId, IReadOnlyList<string>? targets, int? interAltDelayMs = null)
        => new(ContractVersion, Method, macroId,
               targets is { Count: > 0 } ? targets : new[] { "foreground" }, interAltDelayMs, CallerId);

    public static GetPlaybackRequest ForPlayback(string playbackId)
        => new(ContractVersion, MethodGetPlayback, playbackId, CallerId);

    public static ClearAtRequest ForClearAt(string target, ClearAtClient client, IReadOnlyList<ClearAtPoint> points,
        ClearAtOutline outline, int? maxMsPerPoint = null, ClearAtGuard? guard = null)
        => new(ContractVersion, MethodClearAt, CallerId, target, client, points, outline, maxMsPerPoint, guard);

    public static SweepPathRequest ForSweepPath(string target, ClearAtClient client, IReadOnlyList<SweepPoint> path,
        int step, int dwellMs, ClearAtGuard guard)
        => new(ContractVersion, MethodSweepPath, CallerId, target, client, path, step, dwellMs, guard);
}
