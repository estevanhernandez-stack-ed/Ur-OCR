// Ipc/IMacroRunClient.cs
namespace RoRoRo.UrOcr.Ipc;

public interface IMacroRunClient
{
    Task<RunMacroResponse> RunAsync(string macroId, IReadOnlyList<string>? targets, CancellationToken ct);

    /// <summary>RunAsync with Ur Task's focus-to-play wait set (0 when the account is already in
    /// front). The default drops the delay so a client that cannot send it (the trigger tests'
    /// fakes) still compiles; MacroRunClient sends it.</summary>
    Task<RunMacroResponse> RunAsync(string macroId, IReadOnlyList<string>? targets, int? interAltDelayMs, CancellationToken ct)
        => RunAsync(macroId, targets, ct);

    /// <summary>How a playback RunAsync started is going. The default answers "refused" so a
    /// client that cannot ask (the trigger tests' fakes) still compiles; MacroRunClient asks Ur Task.</summary>
    Task<GetPlaybackResponse> GetPlaybackAsync(string playbackId, CancellationToken ct)
        => Task.FromResult(new GetPlaybackResponse(false, null, BridgeReasons.Refused,
            "This client cannot ask Ur Task about playbacks.", null));
}
