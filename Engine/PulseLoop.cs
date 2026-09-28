using System.Globalization;
using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

public enum PulseState { Riding, Pausing, Reading, Clearing, Bursting, GoingToTop, Stopped }

/// <summary>
/// One account's ore stop pulse (spec 2026-09-28). Riding: Auto Mine on for BurstMs. Pausing: Auto
/// Mine off, then SettleMs for the effects to clear. Reading: vote the layer on that calm frame.
/// Above the aim layer: ride again. Past it: Go to Top. At it: Clearing, one Clear spot per ring
/// spot, ore first; Ur Task skips a spot with no outline. A pass that cleared something settles and
/// reads again; a pass that cleared nothing rides a burst (Bursting) first. RockCapMinutes on one
/// aim layer with nothing cleared (time behind or paused not counted): Go to Top. Each tick does
/// everything it can and returns at the first wait. Nothing starts unless the account is in front,
/// read again right before each macro; a playback already started is still followed.
/// A clear or macro whose check could not run (CheckFailed) or that Ur Task lost (Lost) stops the loop.
/// </summary>
public sealed class PulseLoop
{
    public const int MaxStepsPerTick = 12;

    private readonly PulseConfig _config;
    private readonly RingDefinition? _ring;
    private readonly IReadOnlyList<Trigger> _spots;
    private readonly ISpotReader _reader;
    private readonly IClock _clock;
    private readonly Action<string> _log;
    private readonly MacroCall _call;

    private bool _macroDone;               // this state's macro has ended
    private DateTimeOffset? _until;        // this state's timer: set when its macro ends
    private readonly Queue<int> _queue = new();
    private int _spot = -1;                // the ring order being cleared, -1 between clears
    private readonly List<string> _cleared = new();
    private readonly List<string> _skipped = new();
    private DateTimeOffset _progressAt;    // last new layer or cleared block, for the rock cap
    private bool _unreadableLogged;
    private DateTimeOffset? _behindSince;  // first tick behind (or held), for the rock cap

    public PulseLoop(PulseConfig config, IReadOnlyList<RingDefinition> rings, IReadOnlyList<Trigger> triggers,
        ISpotReader reader, IMacroRunClient macros, IClock clock, Action<string> log)
    {
        _config = config;
        _reader = reader;
        _clock = clock;
        _log = log;
        _ring = TriggerValidation.Find(rings, config.RingId);
        _spots = PulseValidation.SpotsOf(config.RingId, triggers);
        _call = new MacroCall(macros, config.AccountUserId.ToString(CultureInfo.InvariantCulture), clock, log);

        if (PulseValidation.Validate(config, rings, triggers) is { } problem)
        {
            Stop(problem);
            return;
        }
        var mode = config.Mode == PulseMode.OneAbove ? "one above" : "top";
        _log($"started: ring {config.RingId}, target layer {config.TargetLayer} ({mode}), clearing on {_ring!.Layers[config.AimLayer - 1].Name}");
    }

    public long AccountUserId => _config.AccountUserId;
    public PulseState State { get; private set; } = PulseState.Riding;
    public string? StopReason { get; private set; }
    /// <summary>The layer of the last calm read, null before the first and after a Go to Top.</summary>
    public string? Layer { get; private set; }

    private PulseMacros M => _config.Macros!;

    /// <param name="inFront">A fresh read of the foreground gate for this account, asked right
    /// before each RunMacro (Ur Task focuses the target on every run).</param>
    public async Task TickAsync(bool foreground, int pid, CancellationToken ct, Func<bool>? inFront = null)
    {
        if (foreground) NoteFront(); else NoteBehind();
        for (var step = 0; step < MaxStepsPerTick && State != PulseState.Stopped; step++)
        {
            if (_call.Active)
            {
                var r = await _call.StepAsync(foreground, ct, inFront).ConfigureAwait(false);
                if (r.Status == CallStatus.Waiting) return;
                OnCallEnded(r);
                continue;
            }
            if (!foreground) return;
            if (!Act(pid)) return;
        }
    }

    /// <summary>The account is behind, or the runner is held (F9, dry run): from now until it is
    /// back in front does not count toward the rock cap.</summary>
    public void NoteBehind() => _behindSince ??= _clock.Now;

    private void NoteFront()
    {
        if (_behindSince is not { } since) return;
        _behindSince = null;
        if (Layer is not null) _progressAt += _clock.Now - since;
    }

    /// <summary>One move in the current state. False when it has to wait (a timer, an unreadable ring).</summary>
    private bool Act(int pid)
    {
        var now = _clock.Now;
        switch (State)
        {
            case PulseState.Riding:
            case PulseState.Bursting:
                if (!_macroDone) return Begin(M.AutoMineOn, PulseMacroNames.AutoMineOn);
                return now >= _until && Enter(PulseState.Pausing);

            case PulseState.Pausing:
                if (!_macroDone) return Begin(M.AutoMineOff, PulseMacroNames.AutoMineOff);
                return now >= _until && Enter(PulseState.Reading);

            case PulseState.Reading:
                return Read(pid, now);

            case PulseState.Clearing:
                return ClearNext();

            case PulseState.GoingToTop:
                if (!_macroDone) return Begin(M.GoToTop, PulseMacroNames.GoToTop);
                Layer = null;              // back at the top: the rock cap starts over
                return Enter(PulseState.Riding);

            default:
                return false;
        }
    }

    private bool Read(int pid, DateTimeOffset now)
    {
        var samples = _reader.Read(pid, _spots);
        if (samples is null || samples.Count == 0)
        {
            if (!_unreadableLogged) _log("could not read the ring (window hidden or gone); waiting");
            _unreadableLogged = true;
            return false;
        }
        _unreadableLogged = false;

        var ring = _ring!;
        var votes = RingTracker.Vote(ring, samples.Select(s => new SpotSample(s.Order, s.Sampled, s.ToleranceRgb)).ToList());
        var layer = RingTracker.Pick(ring, votes, Layer);
        if (layer is null)
        {
            var best = votes.Count == 0 ? 0 : votes.Values.Max();
            _log($"no layer on a calm frame ({best} of {samples.Count} spots at best): riding a burst");
            return Enter(PulseState.Bursting);
        }

        if (!string.Equals(layer, Layer, StringComparison.OrdinalIgnoreCase))
        {
            Layer = layer;
            _progressAt = now;
        }
        var number = LayerNumber(layer);
        var aim = _config.AimLayer;
        var aimName = ring.Layers[aim - 1].Name;
        var seen = $"layer {layer} ({votes[layer]} of {samples.Count} spots)";

        if (number > aim)
        {
            _log($"{seen} is past the target ({aimName}): going to top");
            return Enter(PulseState.GoingToTop);
        }
        if (number < aim)
        {
            _log($"{seen} is above the target ({aimName}): riding on");
            return Enter(PulseState.Riding);
        }
        // The rock cap counts only while clearing (spec step 5), so the slow pulsed descent never trips it.
        if ((now - _progressAt).TotalMinutes >= _config.RockCapMinutes)
        {
            var unit = _config.RockCapMinutes == 1 ? "minute" : "minutes";
            _log($"Went to top: {_config.RockCapMinutes} {unit} on the {layer} layer");
            return Enter(PulseState.GoingToTop);
        }

        var order = PulseOrder.Rank(samples, ring.Layers[number - 1].Rock);
        _queue.Clear();
        foreach (var o in order) _queue.Enqueue(o);
        _spot = -1;
        _cleared.Clear();
        _skipped.Clear();
        _log($"{seen} is the target: clearing {string.Join(" ", order.Select(SpotName))}");
        return Enter(PulseState.Clearing);
    }

    private bool ClearNext()
    {
        if (_spot < 0)
        {
            if (_queue.Count == 0) return EndPass();
            _spot = _queue.Dequeue();
        }
        return Begin(M.Clear[_spot], PulseMacroNames.Clear(SpotName(_spot)));
    }

    private bool EndPass()
    {
        if (_cleared.Count > 0)
        {
            var skipped = _skipped.Count > 0 ? $"; nothing to clear at {string.Join(" ", _skipped)}" : "";
            _log($"cleared {string.Join(" ", _cleared)}{skipped}: reading again");
            return Enter(PulseState.Pausing, macroDone: true);   // Auto Mine is still off: just settle
        }
        _log($"nothing in reach to clear ({_skipped.Count} spots skipped): riding a burst");
        return Enter(PulseState.Bursting);
    }

    private void OnCallEnded(CallResult r)
    {
        if (r.Status == CallStatus.Stop)
        {
            Stop(r.Detail ?? $"'{r.Label}' could not run.");
            return;
        }
        // A lost playback is a stop, never a re-run: nothing says whether it pressed anything, and
        // running it again has no cap on how often Ur Task could lose it.
        if (r.Status == CallStatus.Lost)
        {
            Stop($"{r.Detail} The pulse loop stopped rather than guess how it went.");
            return;
        }

        if (State == PulseState.Clearing)
        {
            var name = SpotName(_spot);
            switch (r.Status)
            {
                case CallStatus.Done:
                    _cleared.Add(name);
                    _progressAt = _clock.Now;
                    break;
                case CallStatus.CheckFailed:
                    // A missing outline is "skipped"; check-failed means the check could not run.
                    Stop($"'{r.Label}' stopped at its check ({r.Detail}): Ur Task could not check the spot " +
                         "(window hidden, or a box outside it), so the pulse loop stopped rather than ride on blind.");
                    return;
                default:
                    _skipped.Add(name);
                    break;
            }
            _spot = -1;
            return;
        }

        if (r.Status == CallStatus.CheckFailed)
        {
            Stop($"'{r.Label}' stopped at its colour check ({r.Detail}). Something may be over the game, " +
                 "such as a popup or captcha, so the pulse loop stopped rather than click it.");
            return;
        }
        _macroDone = true;
        _until = TimerFor(State);
    }

    private bool Begin(string macroId, string label)
    {
        _call.Begin(macroId, label);
        return true;
    }

    /// <summary>Always true, so Act can `return Enter(...)` and the tick carries on in the new state.</summary>
    private bool Enter(PulseState next, bool macroDone = false)
    {
        State = next;
        _macroDone = macroDone;
        _until = macroDone ? TimerFor(next) : null;
        return true;
    }

    private DateTimeOffset? TimerFor(PulseState state) => state switch
    {
        PulseState.Riding or PulseState.Bursting => _clock.Now.AddMilliseconds(_config.BurstMs),
        PulseState.Pausing => _clock.Now.AddMilliseconds(_config.SettleMs),
        _ => null,
    };

    private void Stop(string reason)
    {
        // Riding/Bursting run Auto Mine on; Pausing runs Auto Mine off, but a stop there always
        // catches that macro mid-flight (_macroDone is only ever true entering Reading, which
        // never stops), so it groups with "may still be on" too, same as a Go to Top in flight.
        // Reading and Clearing always start after Auto Mine off has completed, so it is off.
        var autoMine = State is PulseState.Reading or PulseState.Clearing
            ? "Auto Mine is off."
            : "Auto Mine may still be on.";
        var full = $"{reason} {autoMine}";
        State = PulseState.Stopped;
        StopReason = full;
        _log($"stopped: {full}");
    }

    private int LayerNumber(string name)
    {
        var layers = _ring!.Layers;
        for (var i = 0; i < layers.Count; i++)
            if (string.Equals(layers[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i + 1;
        return 0;
    }

    private static string SpotName(int order) =>
        order >= 0 && order < MeasuredRing.RingOrder.Length
            ? MeasuredRing.RingOrder[order]
            : order.ToString(CultureInfo.InvariantCulture);
}
