using System.Globalization;
using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

public enum PulseState { Riding, Pausing, Reading, Clearing, Bursting, GoingToTop, Turning, Stopped }

/// <summary>
/// One account's ore stop pulse (spec 2026-09-28). Riding: Auto Mine on for BurstMs. Pausing: Auto
/// Mine off, then SettleMs for the effects to clear. Reading: read the layer on that calm frame, by
/// colour share around the character with an ore finder (LayerShare), else by the 8-spot vote.
/// Above the aim layer: ride again. Past it: Go to Top.
/// At it: Clearing. With an ore finder for the aim layer, one ClearAt call carries the finder's points
/// (ore patches, then a block grid) from a calm frame; without one, one Clear spot per ring spot,
/// ore first. With a guard in the finder and a block size read off the frame (or, without a read Ur
/// Task would take, the account's SweepBlockPx), the stone is swept instead (spec 2026-09-29):
/// ClearAt with the ore points only, then one SweepPath around the character, then a settle and a
/// read; the next read judges the sweep (SweepChange), and a sweep that changed nothing is a pass
/// with nothing in reach. Ur Task skips a point or spot with no
/// outline. A pass that cleared something settles and reads again; a pass that cleared nothing
/// first turns the camera (Turning, "Camera turn left", then SettleMs and a read) up to MaxTurns
/// times since the last progress, then rides a burst (Bursting), twice as long for each such burst
/// in a row (RideLonger). RockCapMinutes on one
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
    /// <summary>The longest burst a run of passes with nothing in reach grows to.</summary>
    public const int MaxBurstMs = 30000;
    /// <summary>Stone points checked when the block size could not be read off the frame.</summary>
    public const int UnreadStonePoints = 16;
    /// <summary>Camera turns after passes with nothing in reach before a burst: a quarter turn each,
    /// so three more angles and the full circle looked at.</summary>
    public const int MaxTurns = 3;
    /// <summary>With the account's SweepBlockPx set, a block size read off the frame under this share
    /// of it is ignored and the setting used (live 2026-09-30: real blocks near 110 px, reads of 29 to
    /// 47 px on later passes).</summary>
    public const double SettingReadMin = 0.75;
    /// <summary>With the account's SweepBlockPx set, a read over this share of it is ignored too.</summary>
    public const double SettingReadMax = 1.33;
    /// <summary>How many passes a spot that showed no outline stays left out of ClearAt (fix 1,
    /// live 2026-09-30: the same spots came back empty 11 to 14 passes running, about 0.6 s each).
    /// After that it is tried again, and remembered again if it is still empty.</summary>
    public const int NoOutlinePasses = 5;

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
    private bool _pitchUnread;                         // this pass uses the layer default block size
    private bool _pitchFromSetting;                    // this pass uses the account's SweepBlockPx over a far read
    private IReadOnlyList<FinderTarget>? _targets;     // this pass's ClearAt points; null on a Clear spot pass
    private bool _clearAtEnded;                        // this pass's ClearAt has ended
    private bool _frameLogged;
    private readonly int _toleranceRgb;               // the ring spots' rock tolerance, for the colour-share read
    private IReadOnlyList<SweepPoint>? _sweep;         // this pass's sweep path; null clears stone point by point
    private int _sweepStep;                            // the block size this pass's sweep moves by
    private string? _sweepLogged;                      // the last sweep block size line, logged only when it changes
    private FramePixels? _sweepFrame;                  // the calm frame the sweep pass was planned on
    private bool _sweeping;                            // the sweep's call is in flight
    private bool _sweepEnded;                          // this pass's sweep has ended
    private SweepCheck? _sweepCheck;                   // a finished sweep, judged on the next calm frame

    /// <summary>A sweep to judge on the next calm frame: the frame it was planned on, its path and the
    /// block size it moved by.</summary>
    private sealed record SweepCheck(FramePixels Before, IReadOnlyList<SweepPoint> Path, int Pitch);

    /// <summary>A ClearAt point that showed no outline, in the finder's measured pixels, and the pass
    /// that sent it.</summary>
    private sealed record NoOutlineSpot(int X, int Y, int Pass);
    private readonly List<NoOutlineSpot> _noOutline = new();   // cleared whenever the pulse rides (ForgetNoOutline)
    private int _passNumber;                                   // ClearAt passes planned, for NoOutlinePasses

    private bool _macroDone;               // this state's macro has ended
    private DateTimeOffset? _until;        // this state's timer: set when its macro ends
    private readonly Queue<int> _queue = new();
    private int _spot = -1;                // the ring order being cleared, -1 between clears
    private readonly List<string> _cleared = new();
    private readonly List<string> _skipped = new();
    private DateTimeOffset _progressAt;    // last new layer or cleared block, for the rock cap
    private bool _unreadableLogged;
    private DateTimeOffset? _behindSince;  // first tick behind (or held), for the rock cap
    private int _emptyPasses;              // passes in a row with nothing in reach, for the growing burst
    private int _burstMs;                  // this Bursting state's ride
    private int _turns;                    // camera turns since the last progress (cleared pass, new layer, Go to Top, burst)

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
            : $"the ore finder ({_finder.Pitch} px blocks, radius {_finder.RadiusBlocks})"
              + (_finder.Guard is null
                  ? ""
                  : $", sweeping stone ({config.SweepDwellMs} ms a point, near side {(config.SweepNearSide ? "on" : "off")}"
                    + (config.SweepBlockPx is { } px ? $", {px} px blocks without a read)" : ")"));
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
                _emptyPasses = 0;          // and so does the growing burst
                _turns = 0;                // and the looking around
                return Enter(PulseState.Riding);

            case PulseState.Turning:
                if (!_macroDone) return Begin(M.CameraTurnLeft!, PulseMacroNames.CameraTurnLeft);
                return now >= _until && Enter(PulseState.Reading);

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

        // The last pass swept and cleared no ore: this calm frame says whether the sweep broke anything.
        if (_sweepCheck is { } check)
        {
            _sweepCheck = null;
            var points = SweepChange.Points(check.Path);
            var changed = SweepChange.Count(check.Before, frame, check.Path, finder.ClientW, finder.ClientH, check.Pitch);
            if (changed == 0) return NothingInReach($"the sweep broke nothing (0 of {points} points changed)");
            _emptyPasses = 0;
            _turns = 0;
            _progressAt = now;
            _log($"the sweep changed {changed} of {points} points");
        }

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
        if (_pitchUnread) targets = NearestStone(targets, UnreadStonePoints);
        var found = targets.Count;
        targets = LeaveOutNoOutline(targets, pass.Pitch);
        _sweep = SweepFor(pass);
        if (_sweep is not null)
        {
            targets = targets.Where(t => t.Ore).ToList();   // the stone is swept, not pressed point by point
            _sweepFrame = frame;
        }
        else if (targets.Count == 0 && found > 0)
            return NothingInReach($"nothing in reach to clear (all {found} points showed no outline on a recent pass)");
        else if (targets.Count == 0)
        {
            _log($"{seen} is the target, but the ore finder has no point inside the window: riding a burst");
            return Enter(PulseState.Bursting);
        }
        _targets = targets;
        _pass = pass;
        var ore = targets.Count(t => t.Ore);
        _log(_sweep is { } path
            ? $"{seen} is the target: {ore} ore {(ore == 1 ? "point" : "points")}, then a sweep of {path.Count} points"
            : $"{seen} is the target: clearing at {targets.Count} points ({ore} ore, {targets.Count - ore} stone)");
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
        _sweep = null;
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
            _turns = 0;
            ForgetNoOutline();
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
        _sweepEnded = false;
        _sweeping = false;
        return null;
    }

    /// <summary>
    /// The no-outline memory (fix 1): a new pass drops every ore point within half a block of a spot a
    /// recent ClearAt said showed no outline, and logs how many once. Spots older than
    /// NoOutlinePasses passes are forgotten first, so an emptied spot is tried again now and then.
    /// Only ore points are dropped; stone points go as before. A sweep pass whose ore is all dropped
    /// goes straight to the sweep (ClearNext).
    /// </summary>
    private IReadOnlyList<FinderTarget> LeaveOutNoOutline(IReadOnlyList<FinderTarget> targets, int pitch)
    {
        _passNumber++;
        _noOutline.RemoveAll(s => _passNumber - s.Pass > NoOutlinePasses);
        if (_noOutline.Count == 0) return targets;
        var reach = pitch / 2.0;
        var kept = targets.Where(t => !t.Ore || !_noOutline.Any(s => Near(s, t, reach))).ToList();
        var dropped = targets.Count - kept.Count;
        if (dropped > 0)
            _log($"skipping {dropped} ore {(dropped == 1 ? "point" : "points")} that showed no outline on a recent pass");
        return kept;
    }

    private static bool Near(NoOutlineSpot s, FinderTarget t, double reach)
    {
        double dx = t.X - s.X, dy = t.Y - s.Y;
        return dx * dx + dy * dy <= reach * reach;
    }

    /// <summary>Remembers the points a finished ClearAt named (1-based into what this pass sent).
    /// An Ur Task too old to name them sends none, and nothing is remembered.</summary>
    private void RememberNoOutline(IReadOnlyList<int>? points)
    {
        if (points is null || _targets is not { } sent) return;
        foreach (var n in points)
            if (n >= 1 && n <= sent.Count)
                _noOutline.Add(new NoOutlineSpot(sent[n - 1].X, sent[n - 1].Y, _passNumber));
    }

    /// <summary>The screen has moved under the remembered spots: the pulse rode (a ride, a burst, Go
    /// to Top), the camera turned, or the layer changed.</summary>
    private void ForgetNoOutline() => _noOutline.Clear();

    /// <summary>Every ore point and the first <paramref name="stone"/> stone points (they come nearest first).</summary>
    private static IReadOnlyList<FinderTarget> NearestStone(IReadOnlyList<FinderTarget> targets, int stone) =>
        targets.Where(t => t.Ore).Concat(targets.Where(t => !t.Ore).Take(stone)).ToList();

    /// <summary>This pass's sweep path (its step in _sweepStep), or null to clear the stone point by point
    /// as before. The step is the block size read off the frame when there is one inside
    /// BridgeContract.MinSweepStep..MaxSweepStep (the range Ur Task takes); otherwise the account's
    /// SweepBlockPx, logged as "(account setting)" when it takes over: the read tops out near 100 px
    /// and a block down a shaft is 150 to 180. With neither (the path's step would be a guess), or a
    /// finder with no guard (a held button needs one), no sweep; and none when fewer than
    /// SweepPath.MinPoints points fit at that step, which is logged.</summary>
    private IReadOnlyList<SweepPoint>? SweepFor(FinderSetup pass)
    {
        if (pass.Guard is null) return null;
        var readFits = !_pitchUnread && pass.Pitch is >= BridgeContract.MinSweepStep and <= BridgeContract.MaxSweepStep;
        int step;
        string? line = null;
        if (readFits) step = pass.Pitch;                   // ForPass already said where the size came from
        else if (_config.SweepBlockPx is { } own)
        {
            step = own;
            line = $"block size {step} px (account setting)";
        }
        else
        {
            _sweepLogged = null;
            return null;
        }

        var path = SweepPath.Build(pass with { Pitch = step }, _config.SweepNearSide);
        if (path.Count < SweepPath.MinPoints)
            line = $"no sweep path fits at {step} px blocks ({(readFits && !_pitchFromSetting ? "read from the frame" : "account setting")}): clearing stone point by point";
        if (line is not null && line != _sweepLogged) _log(line);
        _sweepLogged = line;
        if (path.Count < SweepPath.MinPoints) return null;
        _sweepStep = step;
        return path;
    }

    private static string Percent(double share) =>
        (share * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    /// <summary>The live frame's scale against the finder's measured client.</summary>
    private static (double X, double Y) Scale(FramePixels frame, FinderSetup f) =>
        ((double)frame.Width / f.ClientW, (double)frame.Height / f.ClientH);

    /// <summary>The finder for this pass (spec "Block size is read every pass"): the camera pulls in to
    /// the first wall, so the block size is read off the calm frame and sets the grid, the reach and
    /// the outline box (w = h = the block, 16 to 240). A frame with no clear pattern falls back to the
    /// layer's measured pitch for the grid, with the largest box (240) and only the nearest stone points:
    /// a camera jammed against the character shows blocks far bigger than the default. A read under half
    /// the layer's measured pitch counts as unread too (Task 12 live finding): the fine crack texture
    /// inside a block reads as a much smaller, spurious pitch than the block itself; only this lower
    /// bound distrusts a read, a larger one is still trusted. With the account's SweepBlockPx set, a
    /// read outside SettingReadMin..SettingReadMax of it (including one under half the default) is
    /// ignored and the setting is this pass's block size, for the grid, the box and the sweep alike
    /// (live 2026-09-30: the read swung from 104 to 30 px while the blocks stayed near 110); no read
    /// at all still falls back to the layer default. The frame may be another size than the
    /// measured client: the centre is scaled into it and the block scaled back to measured pixels.</summary>
    private FinderSetup ForPass(FramePixels frame, FinderSetup f)
    {
        var (sx, sy) = Scale(frame, f);
        var read = PitchEstimator.Estimate(frame, (int)Math.Round(f.CenterX * sx), (int)Math.Round(f.CenterY * sy));
        var readPitch = read is null
            ? (int?)null
            : Math.Max(FinderSetup.MinPitch, (int)Math.Round(read.Pitch * 2 / (sx + sy), MidpointRounding.AwayFromZero));
        _pitchFromSetting = readPitch is { } near && _config.SweepBlockPx is { } own
            && (near < own * SettingReadMin || near > own * SettingReadMax);
        var tooSmall = !_pitchFromSetting && readPitch is { } rp && rp < f.Pitch / 2;
        _pitchUnread = read is null || tooSmall;
        var pitch = _pitchFromSetting ? _config.SweepBlockPx!.Value : _pitchUnread ? f.Pitch : readPitch!.Value;
        var side = _pitchUnread ? FinderSetup.MaxOutlineSide : Math.Clamp(pitch, MinOutlineSide, FinderSetup.MaxOutlineSide);
        var minCount = Math.Min(f.Outline.MinCount, side * side);

        var reason = read is null
            ? "layer default; no clear pattern"
            : _pitchFromSetting
                ? $"account setting; read {readPitch!.Value} px is far from it"
                : tooSmall
                    ? $"layer default; read {readPitch!.Value} px is under half the default"
                    : "read from the frame";
        var line = $"block size {pitch} px ({reason})";
        if (minCount < f.Outline.MinCount) line += $"; outline minCount {minCount} to fit the {side}x{side} box";
        if (line != _blockLogged) _log(line);
        _blockLogged = line;

        return f with { Pitch = pitch, Outline = f.Outline with { W = side, H = side, MinCount = minCount } };
    }

    private bool ClearNext()
    {
        if (_targets is { } targets)
        {
            var f = _pass!;
            var guard = f.Guard is { } g ? new ClearAtGuard(g.X, g.Y, g.W, g.H, g.Expect, g.Tolerance) : null;
            if (!_clearAtEnded && targets.Count > 0)      // a sweep pass with no ore goes straight to the sweep
            {
                _call.BeginClearAt(new ClearAtClient(f.ClientW, f.ClientH),
                    targets.Select(t => new ClearAtPoint(t.X, t.Y, t.Label)).ToList(),
                    new ClearAtOutline(f.Outline.W, f.Outline.H, f.Outline.MinCount, f.Outline.WhiteMin),
                    guard);
                return true;
            }
            if (_sweep is { } path && !_sweepEnded)
            {
                _sweeping = true;                          // SweepFor sweeps only with a guard
                _call.BeginSweep(new ClearAtClient(f.ClientW, f.ClientH), path, _sweepStep, _config.SweepDwellMs, guard!);
                return true;
            }
            return EndPass();
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
            if (_sweep is { } path) return EndSweepPass(path);
            if (_cleared.Count > 0)
            {
                _emptyPasses = 0;
                _turns = 0;
                _log($"cleared at the ore finder's points ({targets.Count} tried): reading again");
                return Enter(PulseState.Pausing, macroDone: true);   // Auto Mine is still off: just settle
            }
            return NothingInReach($"nothing in reach to clear (all {targets.Count} points skipped)");
        }
        if (_cleared.Count > 0)
        {
            _emptyPasses = 0;
            _turns = 0;
            var skipped = _skipped.Count > 0 ? $"; nothing to clear at {string.Join(" ", _skipped)}" : "";
            _log($"cleared {string.Join(" ", _cleared)}{skipped}: reading again");
            return Enter(PulseState.Pausing, macroDone: true);   // Auto Mine is still off: just settle
        }
        return NothingInReach($"nothing in reach to clear ({_skipped.Count} spots skipped)");
    }

    /// <summary>A sweep pass ends in a settle and a read, Auto Mine still off. Ore cleared is progress
    /// whatever the sweep did; otherwise the next calm frame judges the sweep (ReadByShare).</summary>
    private bool EndSweepPass(IReadOnlyList<SweepPoint> path)
    {
        if (_cleared.Count > 0)
        {
            _emptyPasses = 0;
            _turns = 0;
            _log($"cleared ore and swept {path.Count} points: reading again");
            return Enter(PulseState.Pausing, macroDone: true);
        }
        _sweepCheck = new SweepCheck(_sweepFrame!, path, _sweepStep);
        _log($"swept {path.Count} points: reading again");
        return Enter(PulseState.Pausing, macroDone: true);
    }

    /// <summary>A pass with nothing in reach usually means a jammed camera or faces turned away, not an
    /// empty area: turn the camera and read again, up to MaxTurns times, then ride a burst and start
    /// the turns over. Without a Camera turn left macro it rides the burst straight away.</summary>
    private bool NothingInReach(string why)
    {
        if (M.CameraTurnLeft is not null && _turns < MaxTurns)
        {
            _turns++;
            _log($"{why}: turning the camera ({_turns} of {MaxTurns})");
            return Enter(PulseState.Turning);
        }
        _turns = 0;
        return RideLonger(why);
    }

    /// <summary>A pass with nothing in reach rides a burst twice as long as the last one in a row
    /// (BurstMs, 2x, 4x, ... up to MaxBurstMs): a view that stays unclearable, such as a camera
    /// jammed close in a tunnel, then costs little. A pass that clears or a Go to Top starts it over.</summary>
    private bool RideLonger(string why)
    {
        var ms = (long)_config.BurstMs;
        for (var i = 0; i < _emptyPasses && ms < MaxBurstMs; i++) ms *= 2;
        _emptyPasses++;
        var burst = (int)Math.Min(ms, Math.Max(MaxBurstMs, _config.BurstMs));
        var length = burst > _config.BurstMs
            ? $" of {(burst / 1000.0).ToString("0.#", CultureInfo.InvariantCulture)} s"
            : "";
        _log($"{why}: riding a burst{length}");
        Enter(PulseState.Bursting);
        _burstMs = burst;
        return true;
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
            if (_sweeping)
            {
                // The sweep holds the button the whole way, so it always presses: only a stop matters here.
                // Whether it broke anything is read off the next calm frame (EndSweepPass, ReadByShare).
                _sweeping = false;
                _sweepEnded = true;
                if (r.Status == CallStatus.CheckFailed)
                    Stop($"'{r.Label}' stopped at its check ({r.Detail}). Something may be over the game, such as a " +
                         "menu or a player's profile, so the pulse loop stopped rather than click it.");
                return;
            }
            // A ClearAt is one playback for the whole pass: finished counts as cleared progress (rock
            // cap, no burst) exactly as a Clear spot does; finished + skipped means nothing cleared.
            var name = _targets is null ? SpotName(_spot) : r.Label;
            if (r.Status is CallStatus.Done or CallStatus.Skipped) RememberNoOutline(r.NoOutline);
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
        if (next is PulseState.Riding or PulseState.Bursting or PulseState.GoingToTop or PulseState.Turning)
            ForgetNoOutline();
        State = next;
        _burstMs = _config.BurstMs;       // a growing burst is set by RideLonger after this
        _macroDone = macroDone;
        _until = macroDone ? TimerFor(next) : null;
        return true;
    }

    private DateTimeOffset? TimerFor(PulseState state) => state switch
    {
        PulseState.Riding => _clock.Now.AddMilliseconds(_config.BurstMs),
        PulseState.Bursting => _clock.Now.AddMilliseconds(_burstMs),
        PulseState.Pausing => _clock.Now.AddMilliseconds(_config.SettleMs),
        PulseState.Turning => _clock.Now.AddMilliseconds(_config.SettleMs),
        _ => null,
    };

    private void Stop(string reason)
    {
        // Riding/Bursting run Auto Mine on; Pausing runs Auto Mine off, but a stop there always
        // catches that macro mid-flight (_macroDone is only ever true entering Reading, which
        // never stops), so it groups with "may still be on" too, same as a Go to Top in flight.
        // Reading, Clearing and Turning (only ever entered from Clearing) always start after Auto
        // Mine off has completed, so it is off.
        var autoMine = State is PulseState.Reading or PulseState.Clearing or PulseState.Turning
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
