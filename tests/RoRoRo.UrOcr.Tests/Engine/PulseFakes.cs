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
