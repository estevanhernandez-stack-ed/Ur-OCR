// Ipc/BridgeContract.cs
using System.Text.Json;
using System.Text.Json.Serialization;
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
}
