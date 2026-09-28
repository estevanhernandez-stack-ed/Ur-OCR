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
/// layer, then decide and fire. Ring spots fire in ring order, at most one per
/// ring per tick; the rest stay armed for the next tick.
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
    // Trigger id -> the validation problem last reported, so each problem is logged once.
    private readonly Dictionary<Guid, string> _lastProblem = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>One trigger's reading this tick. A ring spot carries its sample until its ring is judged.</summary>
    private sealed class Reading(Trigger trigger)
    {
        public Trigger Trigger { get; } = trigger;
        public bool Matched { get; set; }
        public string Detail { get; set; } = "";
        public Rgb? Sampled { get; set; }
    }

    private enum FireOutcome { Fired, Busy }

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
        foreach (var trig in store.All)
        {
            ct.ThrowIfCancellationRequested();
            if (!trig.Enabled) continue;
            if (!IsValid(trig, rings)) continue;
            if (!PassesGate(trig)) continue;
            var reading = await ReadAsync(trig);
            if (reading is not null) readings.Add(reading);
        }

        var now = clock.Now;
        foreach (var ring in rings) JudgeRing(ring, readings, now);

        var claimedRings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reading in InFiringOrder(readings))
        {
            ct.ThrowIfCancellationRequested();
            await DecideAsync(reading, now, claimedRings, ct);
        }
    }

    private bool IsValid(Trigger trig, IReadOnlyList<RingDefinition> rings)
    {
        var problem = TriggerValidation.Validate(trig, rings);
        if (problem is null)
        {
            _lastProblem.Remove(trig.Id);
            return true;
        }
        if (!_lastProblem.TryGetValue(trig.Id, out var last) || last != problem)
        {
            _lastProblem[trig.Id] = problem;
            log.Record(trig.Id, trig.Name, ActivityKind.Error, problem);
            diag?.Invoke($"trigger \"{trig.Name}\" skipped: {problem}");
        }
        Disarm(trig.Id);
        return false;
    }

    private bool PassesGate(Trigger trig)
    {
        if (!trig.AccountAware && !trig.IsClientSpace) return true;
        if (!foreground.IsForegroundAnAlt())
        {
            log.Record(trig.Id, trig.Name, ActivityKind.SkippedNotAlt);
            Disarm(trig.Id);
            return false;
        }
        var pid = foreground.GetForegroundPid();
        if (elevation.IsForegroundProcessLikelyElevated(pid))
        {
            log.Record(trig.Id, trig.Name, ActivityKind.BlockedElevated);
            Disarm(trig.Id);
            return false;
        }
        return true;
    }

    private async Task<Reading?> ReadAsync(Trigger trig)
    {
        // Layer triggers read the ring, not the screen.
        if (trig.Mode == TriggerMode.Layer) return null;

        var captureRegion = TriggerRegionResolver.Resolve(trig, trig.IsClientSpace ? foreground.GetForegroundPid() : 0, metrics);
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
            _wasMatched[trig.Id] = false;
            log.Record(trig.Id, trig.Name, ActivityKind.NoMatch, detail);
            return;
        }

        var was = _wasMatched.GetValueOrDefault(trig.Id, false);

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
            // An edge that lands inside the cooldown is spent, as it always has been.
            var remain = trig.CooldownMs - (now - trig.LastFiredAt!.Value).TotalMilliseconds;
            log.Record(trig.Id, trig.Name, ActivityKind.SkippedCooldown, $"{remain:0}ms");
            _wasMatched[trig.Id] = true;
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

        var outcome = await FireAsync(trig, detail, now, ct);
        if (outcome == FireOutcome.Busy)
        {
            _retryAt[trig.Id] = now.AddMilliseconds(trig.CooldownMs);
            _wasMatched[trig.Id] = false;
            return;
        }
        _retryAt.Remove(trig.Id);
        _wasMatched[trig.Id] = true;
    }

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

    private void Disarm(Guid id)
    {
        _wasMatched[id] = false;
        _retryAt.Remove(id);
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
