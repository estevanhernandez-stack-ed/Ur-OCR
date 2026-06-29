using System.Diagnostics;
using System.Drawing;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

public interface ICaptureSource { Bitmap Capture(RegionRect region); }
public interface IColorMatchEngine
{
    bool Matches(Bitmap b, ColorCriteria c);
    ColorMatchResult Evaluate(Bitmap b, ColorCriteria c);
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
    Action<Trigger>? onFirstFire = null)
{
    public int TickRateHz { get; set; } = 5;
    public TimeSpan WatchdogTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public bool Paused { get; set; }
    public bool DryRun { get; set; }

    private readonly Dictionary<Guid, bool> _wasMatched = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

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
        foreach (var trig in store.All)
        {
            ct.ThrowIfCancellationRequested();
            if (!trig.Enabled) continue;

            if (trig.AccountAware)
            {
                if (!foreground.IsForegroundAnAlt())
                {
                    log.Record(trig.Id, trig.Name, ActivityKind.SkippedNotAlt);
                    _wasMatched[trig.Id] = false;
                    continue;
                }
                var pid = foreground.GetForegroundPid();
                if (elevation.IsForegroundProcessLikelyElevated(pid))
                {
                    log.Record(trig.Id, trig.Name, ActivityKind.BlockedElevated);
                    _wasMatched[trig.Id] = false;
                    continue;
                }
            }

            using var bmp = capture.Capture(trig.Region);
            bool matched;
            string detail = string.Empty;
            if (trig.Mode == TriggerMode.Text && trig.Text is not null)
            {
                var (m, t) = trig.OcrPreprocess
                    ? await text.RunWithPreprocessAsync(bmp, trig.Text)
                    : await text.RunAsync(bmp, trig.Text);
                matched = m; detail = t;
            }
            else if (trig.Mode == TriggerMode.Color && trig.Color is not null)
            {
                matched = color.Matches(bmp, trig.Color);
            }
            else { continue; }

            var was = _wasMatched.GetValueOrDefault(trig.Id, false);
            var now = clock.Now;
            var cooldownReady = trig.LastFiredAt is null
                || (now - trig.LastFiredAt.Value).TotalMilliseconds >= trig.CooldownMs;

            if (matched && !was && cooldownReady)
            {
                if (DryRun)
                {
                    // Log-only: prove detection without pressing keys or burning cooldown/hit-count.
                    log.Record(trig.Id, trig.Name, ActivityKind.WouldFire,
                        detail.Length > 0 ? $"OCR: {detail}" : null);
                }
                else
                {
                    keys.Press(trig.Keybind);
                    store.MarkFired(trig.Id, now);
                    log.Record(trig.Id, trig.Name, ActivityKind.Fired,
                        detail.Length > 0 ? $"OCR: {detail}" : null);
                    if (!trig.FirstFireConfirmed)
                        onFirstFire?.Invoke(trig);
                }
            }
            else if (!matched)
            {
                log.Record(trig.Id, trig.Name, ActivityKind.NoMatch,
                    detail.Length > 0 ? $"OCR: {detail}" : null);
            }
            else if (!cooldownReady)
            {
                var remain = trig.CooldownMs - (now - trig.LastFiredAt!.Value).TotalMilliseconds;
                log.Record(trig.Id, trig.Name, ActivityKind.SkippedCooldown, $"{remain:0}ms");
            }

            _wasMatched[trig.Id] = matched;
        }
    }
}
