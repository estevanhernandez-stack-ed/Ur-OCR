using System.Diagnostics;
using System.Drawing;
using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

public interface ICaptureSource { Bitmap Capture(RegionRect region); }
public interface IColorMatchEngine
{
    bool Matches(Bitmap b, ColorCriteria c);
    ColorMatchResult Evaluate(Bitmap b, ColorCriteria c);
    /// <summary>recordedRegion is the trigger's stored region, so a pick point
    /// scales when a client-anchored capture comes back at a different size.</summary>
    ColorMatchResult Evaluate(Bitmap b, ColorCriteria c, RegionRect recordedRegion) => Evaluate(b, c);
}
public interface ITextMatchEngine
{
    Task<(bool matched, string text)> RunAsync(Bitmap b, TextCriteria c);
    Task<(bool matched, string text)> RunWithPreprocessAsync(Bitmap b, TextCriteria c);
}
public interface IForegroundCheck { bool IsForegroundAnAlt(); int GetForegroundPid(); }
public interface IElevationCheck { bool IsForegroundProcessLikelyElevated(int pid); }
public interface IKeyPress { void Press(KeyCombo combo); }
public interface IClock { DateTimeOffset Now { get; } }

public sealed class SystemClock : IClock { public DateTimeOffset Now => DateTimeOffset.UtcNow; }

/// <summary>
/// Each tick: read every trigger that may run (valid, foreground gate), update
/// every ring's layer from its spots' samples and judge the spots against that
/// layer, judge layer triggers from the rings, then decide and fire. Ring spots
/// fire in ring order, at most one per ring per tick; the rest stay armed for
/// the next tick. A trigger with HoldForMs fires only after its match has held
/// that long, and starts a fresh hold when it fires. The foreground gate is read
/// once per tick, and one trigger's failed read does not stop the others.
/// </summary>
public sealed class TriggerCoordinator(
    TriggerStore store,
    ICaptureSource capture,
    IColorMatchEngine color,
    ITextMatchEngine text,
    IForegroundCheck foreground,
    IElevationCheck elevation,
    IKeyPress keys,
    ActivityLog log,
    IClock clock,
    IWindowMetrics metrics,
    Action<Trigger>? onFirstFire = null,
    IMacroRunClient? macroClient = null,
    Action<string>? diag = null)
{
    /// <summary>The refusal reason Ur Task returns while a sequence is running.</summary>
    public const string BusyReason = "busy";

    public int TickRateHz { get; set; } = 5;
    public TimeSpan WatchdogTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public bool Paused { get; set; }
    public bool DryRun { get; set; }

    /// <summary>The current layer of every ring, updated each tick.</summary>
    public RingTracker Rings { get; } = new();

    private readonly Dictionary<Guid, bool> _wasMatched = new();
    // Trigger id -> earliest retry after a busy refusal. A trigger in here stays armed.
    private readonly Dictionary<Guid, DateTimeOffset> _retryAt = new();
    // Trigger id -> the problem last reported (invalid, or its read failed), so each problem is logged once.
    private readonly Dictionary<Guid, string> _lastProblem = new();
    // Ring id -> the problem last reported, so a broken or duplicate ring logs once, not every tick.
    private readonly Dictionary<string, string> _lastRingProblem = new(StringComparer.OrdinalIgnoreCase);
    // Trigger id -> what has been matching and since when, for HoldForMs.
    private readonly Dictionary<Guid, (string Key, DateTimeOffset Since)> _holding = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>One trigger's reading this tick. A ring spot carries its sample until its ring is judged.</summary>
    private sealed class Reading(Trigger trigger)
    {
        public Trigger Trigger { get; } = trigger;
        public bool Matched { get; set; }
        public string Detail { get; set; } = "";
        public Rgb? Sampled { get; set; }
        /// <summary>What the match is "of", for holds: a change restarts the hold (the layer name for SameLayer).</summary>
        public string HoldKey { get; set; } = "";
    }

    private enum FireOutcome { Fired, Busy }

    private enum GateStatus { Open, NotAlt, Elevated }

    /// <summary>The foreground decision for one tick: every gated trigger in the tick sees the same one.</summary>
    private readonly record struct Gate(GateStatus Status, int Pid);

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_loop is not null) await _loop;
        _loop = null;
        _cts = null;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        var period = TimeSpan.FromSeconds(1.0 / TickRateHz);
        while (!ct.IsCancellationRequested)
        {
            var tickStart = clock.Now;
            try
            {
                using var tickCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                tickCts.CancelAfter(WatchdogTimeout);
                await TickOnceAsync(tickCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                log.Record(Guid.Empty, "(coordinator)", ActivityKind.Error, "watchdog: tick exceeded 5s");
            }
            catch (Exception ex)
            {
                log.Record(Guid.Empty, "(coordinator)", ActivityKind.Error, ex.Message);
            }
            var elapsed = clock.Now - tickStart;
            var remain = period - elapsed;
            if (remain > TimeSpan.Zero) await Task.Delay(remain, ct);
        }
    }

    public async Task TickOnceAsync(CancellationToken ct)
    {
        if (Paused) return;
        var rings = store.Rings;

        var readings = new List<Reading>();
        Gate? gate = null;   // read on first need, then shared by the whole tick
        foreach (var trig in store.All)
        {
            ct.ThrowIfCancellationRequested();
            if (!trig.Enabled) continue;
            if (!IsValid(trig, rings)) continue;
            var pid = 0;
            if (IsGated(trig))
            {
                gate ??= ReadGate();
                if (!PassesGate(trig, gate.Value)) continue;
                pid = gate.Value.Pid;
            }

            Reading? reading;
            try
            {
                reading = await ReadAsync(trig, pid);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One trigger's failed read (OCR, capture) must not stop every other trigger.
                ReportProblem(trig, $"read failed: {ex.Message}");
                Disarm(trig.Id);
                continue;
            }
            _lastProblem.Remove(trig.Id);
            if (reading is not null) readings.Add(reading);
        }

        var now = clock.Now;
        var seenRingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ring in rings)
        {
            // A hand-edited file can carry the same ring id twice (UpsertRing dedupes,
            // a text editor does not). Skip a duplicate or a ring that fails its own
            // validation (e.g. null layers) rather than let it throw and take every
            // other trigger's tick down with it.
            if (!seenRingIds.Add(ring.Id))
            {
                ReportRingProblem(ring, $"Ring {ring.Id} is defined more than once; only the first definition is used.");
                continue;
            }
            if (TriggerValidation.Validate(ring) is { } problem)
            {
                ReportRingProblem(ring, problem);
                continue;
            }
            JudgeRing(ring, readings, now);
        }
        JudgeLayerTriggers(readings);

        var claimedRings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reading in InFiringOrder(readings))
        {
            ct.ThrowIfCancellationRequested();
            await DecideAsync(reading, now, claimedRings, ct);
        }
    }

    private bool IsValid(Trigger trig, IReadOnlyList<RingDefinition> rings)
    {
        // A valid trigger's record is cleared by its next clean read (TickOnceAsync), not here,
        // so a read failure on a valid trigger is logged once rather than every tick.
        var problem = TriggerValidation.Validate(trig, rings);
        if (problem is null) return true;
        ReportProblem(trig, problem);
        Disarm(trig.Id);
        return false;
    }

    /// <summary>Logs a trigger's problem once: again only after it changes or a clean read clears it.</summary>
    private void ReportProblem(Trigger trig, string problem)
    {
        if (_lastProblem.TryGetValue(trig.Id, out var last) && last == problem) return;
        _lastProblem[trig.Id] = problem;
        log.Record(trig.Id, trig.Name, ActivityKind.Error, problem);
        diag?.Invoke($"trigger \"{trig.Name}\" skipped: {problem}");
    }

    /// <summary>Logs a ring's problem once: again only after the message changes.</summary>
    private void ReportRingProblem(RingDefinition ring, string problem)
    {
        if (_lastRingProblem.TryGetValue(ring.Id, out var last) && last == problem) return;
        _lastRingProblem[ring.Id] = problem;
        log.Record(Guid.Empty, $"(ring {ring.Name})", ActivityKind.Error, problem);
        diag?.Invoke($"ring {ring.Name} skipped: {problem}");
    }

    private static bool IsGated(Trigger trig) => trig.AccountAware || trig.IsClientSpace;

    private Gate ReadGate()
    {
        if (!foreground.IsForegroundAnAlt()) return new Gate(GateStatus.NotAlt, 0);
        var pid = foreground.GetForegroundPid();
        return elevation.IsForegroundProcessLikelyElevated(pid)
            ? new Gate(GateStatus.Elevated, pid)
            : new Gate(GateStatus.Open, pid);
    }

    private bool PassesGate(Trigger trig, Gate gate)
    {
        switch (gate.Status)
        {
            case GateStatus.NotAlt:
                log.Record(trig.Id, trig.Name, ActivityKind.SkippedNotAlt);
                Disarm(trig.Id);
                return false;
            case GateStatus.Elevated:
                log.Record(trig.Id, trig.Name, ActivityKind.BlockedElevated);
                Disarm(trig.Id);
                return false;
            default:
                return true;
        }
    }

    /// <summary>pid is this tick's foreground pid for a gated trigger, else 0.</summary>
    private async Task<Reading?> ReadAsync(Trigger trig, int pid)
    {
        // Layer triggers read the ring, not the screen (JudgeLayerTriggers).
        if (trig.Mode == TriggerMode.Layer) return new Reading(trig);

        var captureRegion = TriggerRegionResolver.Resolve(trig, trig.IsClientSpace ? pid : 0, metrics);
        if (captureRegion is null || captureRegion.Width < 1 || captureRegion.Height < 1)
        {
            // client trigger whose anchor window vanished mid-tick
            log.Record(trig.Id, trig.Name, ActivityKind.SkippedNotAlt, "anchor window unavailable");
            Disarm(trig.Id);
            return null;
        }
        using var bmp = capture.Capture(captureRegion);
        if (trig.Mode == TriggerMode.Text && trig.Text is not null)
        {
            var (m, t) = trig.OcrPreprocess
                ? await text.RunWithPreprocessAsync(bmp, trig.Text)
                : await text.RunAsync(bmp, trig.Text);
            return new Reading(trig) { Matched = m, Detail = t.Length > 0 ? $"OCR: {t}" : "" };
        }
        if (trig.Mode == TriggerMode.Color && trig.Color is not null)
        {
            var r = color.Evaluate(bmp, trig.Color, trig.Region);
            // A ring spot is judged once its ring's layer is known (JudgeRing).
            if (trig.Ring is not null) return new Reading(trig) { Sampled = r.Sampled };
            // Logged every match so the default tolerance can be tuned from real runs.
            return new Reading(trig) { Matched = r.Matched, Detail = ColorDetail(r) };
        }
        return null;
    }

    private void JudgeRing(RingDefinition ring, List<Reading> readings, DateTimeOffset now)
    {
        var spots = readings
            .Where(r => r.Sampled is not null && r.Trigger.Ring is { } s
                        && string.Equals(s.RingId, ring.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var samples = spots
            .Select(r => new SpotSample(r.Trigger.Ring!.Order, r.Sampled!, r.Trigger.Color!.ToleranceRgb))
            .ToList();

        var (state, changed) = Rings.Update(ring, samples, now);
        if (changed)
        {
            log.Record(Guid.Empty, $"(ring {ring.Name})", ActivityKind.LayerChanged, state.Describe());
            diag?.Invoke($"ring {ring.Name}: {state.Describe()}");
        }

        var layer = state.Status == RingStatus.OnLayer
            ? ring.Layers.First(l => string.Equals(l.Name, state.Layer, StringComparison.OrdinalIgnoreCase))
            : null;
        foreach (var r in spots)
        {
            if (layer is null)
            {
                // Without a layer there is no rock to compare against, so nothing counts as ore.
                r.Matched = false;
                r.Detail = $"{ColorNamer.Describe(r.Sampled!)} {state.Describe()}";
                continue;
            }
            var j = ColorMatcher.Judge(r.Sampled!, r.Trigger.Color!, layer.Rock);
            r.Matched = j.Matched;
            r.Detail = $"{layer.Name}: {ColorDetail(j)}";
        }
    }

    private void JudgeLayerTriggers(List<Reading> readings)
    {
        foreach (var r in readings)
        {
            if (r.Trigger.Mode != TriggerMode.Layer || r.Trigger.Layer is not { } c) continue;
            var s = Rings.Get(c.RingId);
            switch (c.Condition)
            {
                case LayerCondition.SameLayer:
                    r.Matched = s.Status == RingStatus.OnLayer;
                    r.HoldKey = s.Layer ?? "";
                    break;
                case LayerCondition.NoLayer:
                    // Unknown (not visible) is not NoLayer: tabbing away never moves the camera.
                    r.Matched = s.Status == RingStatus.NoLayer;
                    r.HoldKey = "no-layer";
                    break;
            }
            r.Detail = s.Describe();
        }
    }

    /// <summary>
    /// Store order, except that a ring's spots are taken together, at the place of
    /// the ring's first spot, sorted by ring order.
    /// </summary>
    private static IEnumerable<Reading> InFiringOrder(List<Reading> readings)
    {
        var firstIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < readings.Count; i++)
            if (readings[i].Trigger.Ring is { } s) firstIndex.TryAdd(s.RingId, i);

        return readings
            .Select((r, i) => (r, i))
            .OrderBy(x => x.r.Trigger.Ring is { } s ? firstIndex[s.RingId] : x.i)
            .ThenBy(x => x.r.Trigger.Ring?.Order ?? 0)
            .ThenBy(x => x.i)
            .Select(x => x.r);
    }

    private async Task DecideAsync(Reading r, DateTimeOffset now, HashSet<string> claimedRings, CancellationToken ct)
    {
        var trig = r.Trigger;
        var detail = r.Detail.Length > 0 ? r.Detail : null;

        if (!r.Matched)
        {
            _retryAt.Remove(trig.Id);
            _holding.Remove(trig.Id);
            _wasMatched[trig.Id] = false;
            log.Record(trig.Id, trig.Name, ActivityKind.NoMatch, detail);
            return;
        }

        if (trig.HoldForMs > 0)
        {
            var heldMs = HeldMs(trig.Id, r.HoldKey, now);
            if (heldMs < trig.HoldForMs)
            {
                // Not held long enough yet. Armed, so it fires the tick the hold completes.
                log.Record(trig.Id, trig.Name, ActivityKind.Holding,
                    $"{r.Detail} held {heldMs / 1000:0.0}s of {trig.HoldForMs / 1000.0:0.#}s".TrimStart());
                _wasMatched[trig.Id] = false;
                return;
            }
        }

        // A completed hold is its own edge: the fire restarted the hold, so a held trigger that
        // reaches here has held again since, even when no tick in between saw it holding.
        var was = trig.HoldForMs == 0 && _wasMatched.GetValueOrDefault(trig.Id, false);

        DateTimeOffset retryAt = default;
        var retrying = !was && _retryAt.TryGetValue(trig.Id, out retryAt);
        if (retrying && now < retryAt)
        {
            // Ur Task was busy: stay armed, and keep this spot's turn in its ring.
            if (trig.Ring is { } waiting) claimedRings.Add(waiting.RingId);
            log.Record(trig.Id, trig.Name, ActivityKind.SkippedCooldown,
                $"{(retryAt - now).TotalMilliseconds:0}ms (Ur Task was busy)");
            return;
        }

        // A due busy retry fires whatever LastFiredAt says: nothing ran on the busy
        // attempt, and its wait was the cooldown. Checking LastFiredAt here would
        // spend the edge whenever that cooldown lags the retry (an edited cooldown).
        var cooldownReady = retrying
            || trig.LastFiredAt is null
            || (now - trig.LastFiredAt.Value).TotalMilliseconds >= trig.CooldownMs;
        if (!cooldownReady)
        {
            // An edge that lands inside the cooldown is spent, as it always has been. A held
            // trigger is not an edge: its hold is complete, so it stays armed and fires the tick
            // the cooldown ends (otherwise a hold shorter than the cooldown would never fire again).
            var remain = trig.CooldownMs - (now - trig.LastFiredAt!.Value).TotalMilliseconds;
            log.Record(trig.Id, trig.Name, ActivityKind.SkippedCooldown, $"{remain:0}ms");
            _wasMatched[trig.Id] = trig.HoldForMs == 0;
            return;
        }
        if (was) return;   // edge already spent

        if (trig.Ring is { } spot)
        {
            if (claimedRings.Contains(spot.RingId))
            {
                // A spot earlier in ring order went this tick; this one stays armed for the next.
                log.Record(trig.Id, trig.Name, ActivityKind.Deferred, "another ring spot went first");
                return;
            }
            claimedRings.Add(spot.RingId);
        }

        // Spec decision 7 (amended): Ur OCR says why it sends the account up; Ur Task only
        // logs the Go to Top playback's ending, since RunMacro carries no reason.
        if (trig.Layer is { Condition: LayerCondition.SameLayer })
        {
            var minutes = RockCapMinutes(trig.HoldForMs);
            var unit = minutes == "1" ? "minute" : "minutes";
            detail = $"Went to top: {minutes} {unit} on the {r.HoldKey} layer";
        }

        var outcome = await FireAsync(trig, detail, now, ct);
        if (outcome == FireOutcome.Busy)
        {
            _retryAt[trig.Id] = now.AddMilliseconds(trig.CooldownMs);
            _wasMatched[trig.Id] = false;
            return;
        }
        _retryAt.Remove(trig.Id);
        _wasMatched[trig.Id] = true;
        // A held trigger starts a fresh hold when it fires: it fires again only after another full hold.
        if (trig.HoldForMs > 0) _holding[trig.Id] = (r.HoldKey, now);
    }

    /// <summary>300000 ms is "5", 90000 ms is "1.5". Invariant culture, so the log reads the same everywhere.</summary>
    private static string RockCapMinutes(int holdForMs) =>
        (holdForMs / 60000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    private async Task<FireOutcome> FireAsync(Trigger trig, string? detail, DateTimeOffset now, CancellationToken ct)
    {
        if (DryRun)
        {
            log.Record(trig.Id, trig.Name, ActivityKind.WouldFire, detail);
            return FireOutcome.Fired;
        }
        if (trig.Action == TriggerAction.RunMacro && macroClient is not null && trig.MacroId is not null)
        {
            var resp = await macroClient.RunAsync(trig.MacroId, trig.MacroTargets, ct).ConfigureAwait(false);
            if (!resp.Ok && string.Equals(resp.Reason, BusyReason, StringComparison.OrdinalIgnoreCase))
            {
                // Not a fire: nothing ran. The caller keeps the trigger armed.
                log.Record(trig.Id, trig.Name, ActivityKind.Busy, $"Ur Task busy, retry in {trig.CooldownMs}ms");
                Diag(trig, $"Ur Task busy, retry in {trig.CooldownMs}ms");
                return FireOutcome.Busy;
            }
            store.MarkFired(trig.Id, now);
            var what = resp.Ok ? $"macro {trig.MacroId}" : $"macro refused: {resp.Reason}";
            log.Record(trig.Id, trig.Name, ActivityKind.Fired, what);
            Diag(trig, detail is null ? what : $"{what} ({detail})");
            if (!trig.FirstFireConfirmed) onFirstFire?.Invoke(trig);
            return FireOutcome.Fired;
        }
        keys.Press(trig.Keybind);
        store.MarkFired(trig.Id, now);
        log.Record(trig.Id, trig.Name, ActivityKind.Fired, detail);
        if (!trig.FirstFireConfirmed) onFirstFire?.Invoke(trig);
        return FireOutcome.Fired;
    }

    /// <summary>How long the same match (same key) has held; starts the clock on a new key.</summary>
    private double HeldMs(Guid id, string key, DateTimeOffset now)
    {
        if (_holding.TryGetValue(id, out var h) && h.Key == key) return (now - h.Since).TotalMilliseconds;
        _holding[id] = (key, now);
        return 0;
    }

    private void Disarm(Guid id)
    {
        _wasMatched[id] = false;
        _retryAt.Remove(id);
        _holding.Remove(id);
    }

    // ur-ocr.log carries the ring's decisions so a session can read them after a run.
    private void Diag(Trigger trig, string message)
    {
        if (trig.Ring is null && trig.Mode != TriggerMode.Layer) return;
        diag?.Invoke($"trigger \"{trig.Name}\": {message}");
    }

    internal static string ColorDetail(ColorMatchResult r)
    {
        if (r.Nearest is { } n)
            return $"{ColorNamer.Describe(r.Sampled)} nearest {ColorNamer.Hex(n)} d={FormatDistance(r.Distance)}";
        return $"{ColorNamer.Describe(r.Sampled)} d={FormatDistance(r.Distance)}"
            + (r.DistanceToOther is { } o ? $" other={o:F1}" : "");
    }

    // An empty none-of list has no nearest colour, so Distance is +Infinity. Write "none", never the raw value.
    private static string FormatDistance(double d) => double.IsFinite(d) ? d.ToString("F1") : "none";
}
