using RoRoRo.UrOcr.Ipc;

namespace RoRoRo.UrOcr.Engine;

public enum CallStatus
{
    /// <summary>Not started yet (account behind, busy retry pending) or still running.</summary>
    Waiting,
    /// <summary>Finished normally. For a Clear spot: it held and let go, so a block was cleared.</summary>
    Done,
    /// <summary>Finished having pressed nothing (reason "skipped": no outline).</summary>
    Skipped,
    /// <summary>A colour check failed and the playback stopped (reason "check-failed").</summary>
    CheckFailed,
    /// <summary>Ur Task no longer knows the playback (restarted, or kept past 10 minutes).</summary>
    Lost,
    /// <summary>The loop must stop; Detail says why.</summary>
    Stop,
}

/// <param name="NoOutline">A finished ClearAt's 1-based points that showed no outline, as Ur Task
/// sent them; null for anything else, or from an Ur Task too old to send them.</param>
/// <param name="Reason">On a Stop from a start Ur Task refused, its reason code (refused, unknown-macro,
/// ...); null otherwise. The pulse falls back from a refused ore sweep on it.</param>
public sealed record CallResult(CallStatus Status, string Label, string? Detail = null, IReadOnlyList<int>? NoOutline = null,
    string? Reason = null);

/// <summary>
/// Runs one Ur Task macro (or one ClearAt call) for one account and follows it to its end, one tick at a time.
/// Starting needs the account in front, read again right before RunMacro when the caller gives
/// <c>inFront</c> (Ur Task focuses the target on every run); polling does not. busy, ack-timeout and
/// no-targets-resolved retry every <see cref="RetryMs"/>. A playback that ends aborted or refused
/// after the account was seen behind was a focus change and runs again (at most
/// <see cref="MaxInterruptions"/> times in a row); aborted while the account stayed in front is
/// taken as Esc and stops. Every other refusal stops, with the reason in Detail. A call begun
/// <c>once</c> (a pulse usable) never retries or reruns: a busy, ack-timeout or no-targets refusal
/// or an interruption ends it as Skipped, with the reason in Detail.
/// </summary>
public sealed class MacroCall(IMacroRunClient client, string target, IClock clock, Action<string> log)
{
    public const int RetryMs = 1000;
    public const int MaxInterruptions = 5;
    /// <summary>Ur Task's focus-to-play wait: none, the account is already in front.</summary>
    public const int InterAltDelayMs = 0;

    private Func<CancellationToken, Task<RunMacroResponse>>? _start;
    private string _label = "";
    private string? _playbackId;
    private DateTimeOffset _retryAt = DateTimeOffset.MinValue;
    private string? _loggedRefusal;
    private int _interruptions;
    private bool _sawBehind;
    private bool _once;

    public bool Active => _start is not null;
    public string Label => _label;

    public void Begin(string macroId, string label, bool once = false) =>
        Arm(label, ct => client.RunAsync(macroId, new[] { target }, InterAltDelayMs, ct), once);

    /// <summary>One ClearAt for this account: every point in order as one playback, followed through
    /// GetPlayback like a macro. The request is built once, so a busy retry or an interrupted rerun
    /// sends the same points. Guard is the finder's optional pixel guard (live safety bug,
    /// 2026-09-28), left off the wire when the finder has none.</summary>
    public void BeginClearAt(ClearAtClient size, IReadOnlyList<ClearAtPoint> points, ClearAtOutline outline,
        ClearAtGuard? guard = null)
    {
        var request = BridgeContract.ForClearAt(target, size, points, outline, guard: guard);
        Arm(ClearAtLabel(points.Count), ct => client.ClearAtAsync(request, ct));
    }

    /// <summary>The name Ur Task's log gives a ClearAt playback.</summary>
    public static string ClearAtLabel(int points) => $"ClearAt ({points} {(points == 1 ? "point" : "points")})";

    /// <summary>One SweepPath for this account, followed through GetPlayback like a macro. The request
    /// is built once, so a busy retry or an interrupted rerun sends the same path. freePath: the ore
    /// sweep, off the block lattice, sent with step 0.</summary>
    public void BeginSweep(ClearAtClient size, IReadOnlyList<SweepPoint> path, int step, int dwellMs, ClearAtGuard guard,
        bool freePath = false)
    {
        var request = BridgeContract.ForSweepPath(target, size, path, step, dwellMs, guard, freePath);
        Arm(SweepLabel(path.Count), ct => client.SweepPathAsync(request, ct));
    }

    /// <summary>The name Ur Task's log gives a SweepPath playback.</summary>
    public static string SweepLabel(int points) => $"SweepPath ({points} {(points == 1 ? "point" : "points")})";

    private void Arm(string label, Func<CancellationToken, Task<RunMacroResponse>> start, bool once = false)
    {
        _once = once;
        _start = start;
        _label = label;
        _playbackId = null;
        _retryAt = DateTimeOffset.MinValue;
        _loggedRefusal = null;
        _interruptions = 0;
        _sawBehind = false;
    }

    /// <param name="inFront">A fresh read of "is this account in front", called right before
    /// RunMacro. The foreground flag is read once per tick; this closes most of the gap.</param>
    public async Task<CallResult> StepAsync(bool foreground, CancellationToken ct, Func<bool>? inFront = null)
    {
        if (_start is null) throw new InvalidOperationException("No macro to step: call Begin first.");
        return _playbackId is null
            ? await StartAsync(foreground, inFront, ct).ConfigureAwait(false)
            : await PollAsync(foreground, ct).ConfigureAwait(false);
    }

    private async Task<CallResult> StartAsync(bool foreground, Func<bool>? inFront, CancellationToken ct)
    {
        var now = clock.Now;
        if (!foreground || now < _retryAt) return Waiting;
        // Ur Task focuses the target on every run: never ask while someone else is in front.
        if (inFront is not null && !inFront()) return Waiting;

        var resp = await _start!(ct).ConfigureAwait(false);
        if (resp.Ok && !string.IsNullOrEmpty(resp.PlaybackId))
        {
            _playbackId = resp.PlaybackId;
            _loggedRefusal = null;
            _sawBehind = false;
            return Waiting;
        }
        switch (resp.Reason)
        {
            case BridgeReasons.Busy:
            case BridgeReasons.AckTimeout:
            case BridgeReasons.NoTargets:
                if (_once) return End(CallStatus.Skipped, $"Ur Task said {resp.Reason}");
                _retryAt = now.AddMilliseconds(RetryMs);
                if (_loggedRefusal != resp.Reason)
                {
                    log($"'{_label}': Ur Task said {resp.Reason}, trying again every {RetryMs / 1000} s");
                    _loggedRefusal = resp.Reason;
                }
                return Waiting;
            case BridgeReasons.NotRunning:
                return End(CallStatus.Stop, $"Ur Task is not running, so '{_label}' could not start.");
            default:
                return End(CallStatus.Stop, resp.Ok
                    ? $"Ur Task accepted '{_label}' but gave no playback id."
                    : $"Ur Task refused '{_label}': {resp.Reason}. {resp.Detail}".TrimEnd(), reason: resp.Ok ? null : resp.Reason);
        }
    }

    private async Task<CallResult> PollAsync(bool foreground, CancellationToken ct)
    {
        if (!foreground) _sawBehind = true;
        var r = await client.GetPlaybackAsync(_playbackId!, ct).ConfigureAwait(false);
        if (!r.Ok)
        {
            if (r.Reason == BridgeReasons.AckTimeout)
            {
                // A stalled poll is otherwise silent: nothing in the log says the loop is stuck
                // waiting on Ur Task. Log the first stall of a run, not every retry.
                if (_loggedRefusal != BridgeReasons.AckTimeout)
                {
                    log($"'{_label}': Ur Task hasn't answered how it's going (ack-timeout); still polling.");
                    _loggedRefusal = BridgeReasons.AckTimeout;
                }
                return Waiting;
            }
            return r.Reason switch
            {
                BridgeReasons.UnknownPlayback => End(CallStatus.Lost,
                    $"Ur Task no longer knows playback {_playbackId} of '{_label}' (restarted, or kept past 10 minutes)."),
                BridgeReasons.NotRunning => End(CallStatus.Stop, $"Ur Task stopped answering while '{_label}' ran."),
                _ => End(CallStatus.Stop, $"Ur Task would not say how '{_label}' went: {r.Reason}. {r.Detail}".TrimEnd()),
            };
        }
        _loggedRefusal = null;   // a good answer clears the stall so a later one logs again

        switch (r.State)
        {
            case PlaybackStates.Running:
                return Waiting;
            case PlaybackStates.Finished:
                return string.Equals(r.Reason, BridgeReasons.Skipped, StringComparison.OrdinalIgnoreCase)
                    ? End(CallStatus.Skipped, r.Detail, r.NoOutline)
                    : End(CallStatus.Done, r.Detail, r.NoOutline);
            case PlaybackStates.Stopped:
                return End(CallStatus.Stop, $"'{_label}' was stopped in Ur Task (Esc or StopMacro), so the pulse loop stops too.");
            case PlaybackStates.Failed when r.Reason == BridgeReasons.CheckFailed:
                return End(CallStatus.CheckFailed, r.Detail);
            case PlaybackStates.Failed when r.Reason == BridgeReasons.Aborted && !_sawBehind:
                return End(CallStatus.Stop,
                    $"'{_label}' ended while the account was in front ({r.Detail}); taken as Esc, so the pulse loop stops.");
            case PlaybackStates.Failed when r.Reason is BridgeReasons.Aborted or BridgeReasons.Refused:
                if (_once) return End(CallStatus.Skipped, $"'{_label}' was interrupted ({r.Detail})");
                if (++_interruptions > MaxInterruptions)
                    return End(CallStatus.Stop, $"'{_label}' was interrupted {MaxInterruptions + 1} times in a row: {r.Detail}");
                log($"'{_label}' was interrupted ({r.Detail}); it runs again when the account is in front");
                _playbackId = null;
                // Paced like a busy refusal: an in-front rerun that is refused again should not
                // burn every remaining attempt in the same second.
                _retryAt = clock.Now.AddMilliseconds(RetryMs);
                return Waiting;
            default:
                return End(CallStatus.Stop, $"'{_label}' failed in Ur Task: {r.Reason}. {r.Detail}".TrimEnd());
        }
    }

    private CallResult Waiting => new(CallStatus.Waiting, _label);

    private CallResult End(CallStatus status, string? detail, IReadOnlyList<int>? noOutline = null, string? reason = null)
    {
        var label = _label;
        _start = null;
        _playbackId = null;
        return new CallResult(status, label, detail, noOutline, reason);
    }
}
