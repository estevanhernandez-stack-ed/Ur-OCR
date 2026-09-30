using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Ipc;

namespace RoRoRo.UrOcr.Tests.Engine;

internal sealed class PulseClock : IClock
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
    public void Advance(int ms) => Now = Now.AddMilliseconds(ms);
}

/// <summary>
/// A fake Ur Task. RunAsync hands out the queued RunReplies first, then accepts with ids pb1, pb2,
/// ... GetPlayback answers from the script of the playback's macro: replies in order, the last one
/// repeating for every later poll (and every later playback of that macro). Unscripted: finished.
/// </summary>
internal sealed class ScriptedMacros : IMacroRunClient
{
    public static readonly GetPlaybackResponse Finished = new(true, "finished", null, null, null);
    public static readonly GetPlaybackResponse Running = new(true, "running", null, null, null);
    public static readonly GetPlaybackResponse Skipped = new(true, "finished", "skipped", "No outline under the pointer.", null);
    public static readonly GetPlaybackResponse CheckFailed = new(true, "failed", "check-failed", "step 1 'Auto Mine off' found no candidate that matched.", 1);
    public static readonly GetPlaybackResponse Aborted = new(true, "failed", "aborted", "Foreground shifted away at step 1/1.", null);
    public static readonly GetPlaybackResponse RefusedFocus = new(true, "failed", "refused", "Couldn't focus the account.", null);
    public static readonly GetPlaybackResponse Stopped = new(true, "stopped", null, null, null);
    public static readonly GetPlaybackResponse Unknown = new(false, null, "unknown-playback", "No playback with id 'pb1'. Finished playbacks are kept for 10 minutes.", null);
    public static readonly GetPlaybackResponse Gone = new(false, null, "ur-task-not-running", "Ur Task is not running or refused the connection.", null);

    public static RunMacroResponse Refusal(string reason) => new(false, null, false, reason, $"{reason} detail");

    public List<(string MacroId, IReadOnlyList<string>? Targets, int? Delay)> Runs { get; } = new();
    public List<string> RunIds => Runs.Select(r => r.MacroId).ToList();
    public List<string> Polls { get; } = new();
    public Queue<RunMacroResponse> RunReplies { get; } = new();

    /// <summary>The id a ClearAt playback is listed under in Runs and scripted with in Script.</summary>
    public const string ClearAtId = "clear-at";
    public List<ClearAtRequest> ClearAts { get; } = new();
    /// <summary>Answers to ClearAt, used before RunReplies and the default accept.</summary>
    public Queue<RunMacroResponse> ClearAtReplies { get; } = new();

    public Task<RunMacroResponse> ClearAtAsync(ClearAtRequest request, CancellationToken ct)
    {
        ClearAts.Add(request);
        if (ClearAtReplies.Count == 0) return RunAsync(ClearAtId, new[] { request.Target }, null, ct);
        Runs.Add((ClearAtId, new[] { request.Target }, null));
        return Task.FromResult(ClearAtReplies.Dequeue());
    }

    /// <summary>The id a SweepPath playback is listed under in Runs and scripted with in Script.</summary>
    public const string SweepId = "sweep-path";
    public List<SweepPathRequest> Sweeps { get; } = new();
    /// <summary>Answers to SweepPath, used before RunReplies and the default accept.</summary>
    public Queue<RunMacroResponse> SweepReplies { get; } = new();

    /// <summary>Answer a free-path SweepPath (the ore sweep) as an Ur Task older than free paths does:
    /// refused, listed in RefusedFreePaths rather than Sweeps.</summary>
    public bool RefuseFreePath { get; set; }
    public List<SweepPathRequest> RefusedFreePaths { get; } = new();

    public Task<RunMacroResponse> SweepPathAsync(SweepPathRequest request, CancellationToken ct)
    {
        if (RefuseFreePath && request.FreePath == true)
        {
            RefusedFreePaths.Add(request);
            return Task.FromResult(new RunMacroResponse(false, null, false, BridgeReasons.Refused,
                "SweepPath needs a step of 8 to 240 px; got 0."));
        }
        Sweeps.Add(request);
        if (SweepReplies.Count == 0) return RunAsync(SweepId, new[] { request.Target }, null, ct);
        Runs.Add((SweepId, new[] { request.Target }, null));
        return Task.FromResult(SweepReplies.Dequeue());
    }

    private readonly Dictionary<string, Queue<GetPlaybackResponse>> _scripts = new();
    private readonly Dictionary<string, string> _macroOf = new();
    private int _next;

    public void Script(string macroId, params GetPlaybackResponse[] replies) =>
        _scripts[macroId] = new Queue<GetPlaybackResponse>(replies);

    public Task<RunMacroResponse> RunAsync(string macroId, IReadOnlyList<string>? targets, CancellationToken ct) =>
        RunAsync(macroId, targets, null, ct);

    public Task<RunMacroResponse> RunAsync(string macroId, IReadOnlyList<string>? targets, int? interAltDelayMs, CancellationToken ct)
    {
        Runs.Add((macroId, targets, interAltDelayMs));
        if (RunReplies.Count > 0) return Task.FromResult(RunReplies.Dequeue());
        var id = $"pb{++_next}";
        _macroOf[id] = macroId;
        return Task.FromResult(new RunMacroResponse(true, id, false, null, null));
    }

    public Task<GetPlaybackResponse> GetPlaybackAsync(string playbackId, CancellationToken ct)
    {
        Polls.Add(playbackId);
        var macro = _macroOf.GetValueOrDefault(playbackId, "");
        if (_scripts.TryGetValue(macro, out var q) && q.Count > 0)
            return Task.FromResult(q.Count > 1 ? q.Dequeue() : q.Peek());
        return Task.FromResult(Finished);
    }
}
