using System.Globalization;
using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

public enum PulseState { Riding, Pausing, Reading, Clearing, Bursting, GoingToTop, Stopped }

/// <summary>
/// One account's ore stop pulse (spec 2026-09-28). Riding: Auto Mine on for BurstMs. Pausing: Auto
/// Mine off, then SettleMs for the effects to clear. Reading: read the layer on that calm frame, by
/// colour share around the character with an ore finder (LayerShare), else by the 8-spot vote.
/// Above the aim layer: ride again. Past it: Go to Top.
/// At it: Clearing. With an ore finder for the aim layer, one ClearAt call carries the finder's points
/// (ore patches, then a block grid) from a calm frame; without one, one Clear spot per ring spot,
/// ore first. Ur Task skips a point or spot with no outline. A pass that cleared something settles and
/// reads again; a pass that cleared nothing rides a burst (Bursting) first. RockCapMinutes on one
/// aim layer with nothing cleared (time behind or paused not counted): Go to Top. Each tick does
/// everything it can and returns at the first wait. Nothing starts unless the account is in front,
/// read again right before each macro; a playback already started is still followed.
/// A clear or macro whose check could not run (CheckFailed) or that Ur Task lost (Lost) stops the loop.
/// </summary>
public sealed class PulseLoop
{
    public const int MaxStepsPerTick = 12;
    /// <summary>The smallest outline box sent: the smallest block PitchEstimator reads.</summary>
    public const int MinOutlineSide = 16;

    private readonly PulseConfig _config;
    private readonly RingDefinition? _ring;
    private readonly IReadOnlyList<Trigger> _spots;
    private readonly ISpotReader _reader;
    private readonly IClock _clock;
    private readonly Action<string> _log;
    private readonly MacroCall _call;
    private readonly FinderSetup? _finder;            // the aim layer's ore finder; null clears the 8 spots
    private FinderSetup? _pass;                        // this pass's finder: the block size read off the frame
    private string? _blockLogged;                      // the last block size line, logged only when it changes
    private IReadOnlyList<FinderTarget>? _targets;     // this pass's ClearAt points; null on a Clear spot pass
    private bool _clearAtEnded;                        // this pass's ClearAt has ended
    private bool _frameLogged;
    private readonly int _toleranceRgb;               // the ring spots' rock tolerance, for the colour-share read

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
        _finder = _ring is null ? null : PulseValidation.FinderFor(_ring, config.AimLayer);
        _toleranceRgb = _spots.Select(s => s.Color?.ToleranceRgb ?? 0).DefaultIfEmpty(0).Max();
        _call = new MacroCall(macros, config.AccountUserId.ToString(CultureInfo.InvariantCulture), clock, log);

        if (PulseValidation.Validate(config, rings, triggers) is { } problem)
        {
            Stop(problem);
            return;
        }
        var mode = config.Mode == PulseMode.OneAbove ? "one above" : "top";
        var how = _finder is null
            ? "the 8 Clear spot macros"
            : $"the ore finder ({_finder.Pitch} px blocks, radius {_finder.RadiusBlocks})";
        _log($"started: ring {config.RingId}, target layer {config.TargetLayer} ({mode}), clearing on {_ring!.Layers[config.AimLayer - 1].Name} with {how}");
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

    private bool Read(int pid, DateTimeOffset now) =>
        _finder is { } finder ? ReadByShare(pid, now, finder) : ReadBySpots(pid, now);

    /// <summary>With an ore finder (spec "The layer is read by colour share, not 8 spots"): one calm
    /// frame gives the block size, the layer and the ClearAt points.</summary>
    private bool ReadByShare(int pid, DateTimeOffset now, FinderSetup finder)
    {
        var frame = _reader.ReadFrame(pid);
        if (frame is null)
        {
            if (!_frameLogged) _log("could not capture the window for the ore finder (hidden or gone); waiting");
            _frameLogged = true;
            return false;
        }
        _frameLogged = false;

        var ring = _ring!;
        var pass = ForPass(frame, finder);
        var (sx, sy) = Scale(frame, finder);
        var read = LayerShare.Read(frame, (int)Math.Round(finder.CenterX * sx), (int)Math.Round(finder.CenterY * sy),
            Math.Max(1, (int)Math.Round(pass.Pitch * (sx + sy) / 2)), ring.Layers, finder.Ore.Select(o => o.Rgb).ToList(),
            _toleranceRgb, ring.LayerMinShare ?? LayerShare.DefaultMinShare, ring.LayerLead ?? LayerShare.DefaultLead);
        var ranked = ring.Layers.Select(l => (l.Name, Share: read.Shares[l.Name])).OrderByDescending(x => x.Share).ToList();
        if (read.Layer is not { } layer)
        {
            _log($"no layer on a calm frame (best {ranked[0].Name} {Percent(ranked[0].Share)}): riding a burst");
            return Enter(PulseState.Bursting);
        }

        var next = ranked.FirstOrDefault(x => !string.Equals(x.Name, layer, StringComparison.OrdinalIgnoreCase));
        var seen = next.Name is null
            ? $"layer {layer} ({Percent(read.Shares[layer])} of the area)"
            : $"layer {layer} ({Percent(read.Shares[layer])} of the area, next {next.Name} {Percent(next.Share)})";
        if (Route(layer, seen, now) is { } moved) return moved;

        var targets = TargetFinder.Find(frame, pass);
        if (targets.Count == 0)
        {
            _log($"{seen} is the target, but the ore finder has no point inside the window: riding a burst");
            return Enter(PulseState.Bursting);
        }
        _targets = targets;
        _pass = pass;
        var ore = targets.Count(t => t.Ore);
        _log($"{seen} is the target: clearing at {targets.Count} points ({ore} ore, {targets.Count - ore} stone)");
        return Enter(PulseState.Clearing);
    }

    /// <summary>Without an ore finder: the 8-spot vote, and one Clear spot per ring spot.</summary>
    private bool ReadBySpots(int pid, DateTimeOffset now)
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

        var seen = $"layer {layer} ({votes[layer]} of {samples.Count} spots)";
        if (Route(layer, seen, now) is { } moved) return moved;

        _targets = null;
        var order = PulseOrder.Rank(samples, ring.Layers[LayerNumber(layer) - 1].Rock);
        foreach (var o in order) _queue.Enqueue(o);
        _log($"{seen} is the target: clearing {string.Join(" ", order.Select(SpotName))}");
        return Enter(PulseState.Clearing);
    }

    /// <summary>Notes the layer just read and leaves the target to clear (null, with the pass reset), or
    /// moves on: past the target goes to top, above it rides, the rock cap goes to top.</summary>
    private bool? Route(string layer, string seen, DateTimeOffset now)
    {
        if (!string.Equals(layer, Layer, StringComparison.OrdinalIgnoreCase))
        {
            Layer = layer;
            _progressAt = now;
        }
        var number = LayerNumber(layer);
        var aim = _config.AimLayer;
        var aimName = _ring!.Layers[aim - 1].Name;

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

        _queue.Clear();
        _spot = -1;
        _cleared.Clear();
        _skipped.Clear();
        _clearAtEnded = false;
        return null;
    }

    private static string Percent(double share) =>
        (share * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    /// <summary>The live frame's scale against the finder's measured client.</summary>
    private static (double X, double Y) Scale(FramePixels frame, FinderSetup f) =>
        ((double)frame.Width / f.ClientW, (double)frame.Height / f.ClientH);

    /// <summary>The finder for this pass (spec "Block size is read every pass"): the camera pulls in to
    /// the first wall, so the block size is read off the calm frame and sets the grid, the reach and
    /// the outline box (w = h = the block, 16 to 240). A frame with no clear pattern falls back to the
    /// layer's measured pitch. The frame may be another size than the measured client: the centre is
    /// scaled into it and the block scaled back to measured pixels.</summary>
    private FinderSetup ForPass(FramePixels frame, FinderSetup f)
    {
        var (sx, sy) = Scale(frame, f);
        var read = PitchEstimator.Estimate(frame, (int)Math.Round(f.CenterX * sx), (int)Math.Round(f.CenterY * sy));
        var pitch = read is null
            ? f.Pitch
            : Math.Max(FinderSetup.MinPitch, (int)Math.Round(read.Pitch * 2 / (sx + sy), MidpointRounding.AwayFromZero));
        var side = Math.Clamp(pitch, MinOutlineSide, FinderSetup.MaxOutlineSide);
        var minCount = Math.Min(f.Outline.MinCount, side * side);

        var line = $"block size {pitch} px ({(read is null ? "layer default; no clear pattern" : "read from the frame")})";
        if (minCount < f.Outline.MinCount) line += $"; outline minCount {minCount} to fit the {side}x{side} box";
        if (line != _blockLogged) _log(line);
        _blockLogged = line;

        return f with { Pitch = pitch, Outline = f.Outline with { W = side, H = side, MinCount = minCount } };
    }

    private bool ClearNext()
    {
        if (_targets is { } targets)
        {
            if (_clearAtEnded) return EndPass();
            var f = _pass!;
            _call.BeginClearAt(new ClearAtClient(f.ClientW, f.ClientH),
                targets.Select(t => new ClearAtPoint(t.X, t.Y, t.Label)).ToList(),
                new ClearAtOutline(f.Outline.W, f.Outline.H, f.Outline.MinCount, f.Outline.WhiteMin));
            return true;
        }
        if (_spot < 0)
        {
            if (_queue.Count == 0) return EndPass();
            _spot = _queue.Dequeue();
        }
        return Begin(M.Clear[_spot], PulseMacroNames.Clear(SpotName(_spot)));
    }

    private bool EndPass()
    {
        if (_targets is { } targets)
        {
            if (_cleared.Count > 0)
            {
                _log($"cleared at the ore finder's points ({targets.Count} tried): reading again");
                return Enter(PulseState.Pausing, macroDone: true);   // Auto Mine is still off: just settle
            }
            _log($"nothing in reach to clear (all {targets.Count} points skipped): riding a burst");
            return Enter(PulseState.Bursting);
        }
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
            // A ClearAt is one playback for the whole pass: finished counts as cleared progress (rock
            // cap, no burst) exactly as a Clear spot does; finished + skipped means nothing cleared.
            var name = _targets is null ? SpotName(_spot) : r.Label;
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
            _clearAtEnded = _targets is not null;
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
