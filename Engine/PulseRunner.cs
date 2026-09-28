using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

/// <summary>Which RoRoRo account a process is. AccountRegistry in production.</summary>
public interface IAccountLookup { bool TryGetUserId(int pid, out long userId); }

/// <summary>
/// Ticks every enabled pulse loop at TickRateHz. Only the loop of the foreground account (a
/// RoRoRo alt, not elevated) may act; the others only follow playbacks they already started. A
/// loop is built once per enabled pulse and keeps its state; a changed pulse needs a restart,
/// which --import-pulse requires anyway. Hold (pause, dry run) stops everything.
/// </summary>
public sealed class PulseRunner(
    TriggerStore store,
    ISpotReader reader,
    IMacroRunClient macros,
    IForegroundCheck foreground,
    IElevationCheck elevation,
    IAccountLookup accounts,
    IClock clock,
    ActivityLog log,
    Action<string>? diag = null)
{
    public int TickRateHz { get; set; } = 5;
    public TimeSpan WatchdogTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public Func<bool> Hold { get; set; } = () => false;

    private readonly Dictionary<long, PulseLoop> _loops = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public PulseLoop? LoopFor(long accountUserId) => _loops.GetValueOrDefault(accountUserId);

    /// <summary>
    /// True when the account in <paramref name="pid"/> has an enabled pulse on the ring, so the
    /// ring's 0.5.0 triggers stand down for it. Enabled, not running: a stopped loop still owns its
    /// ring, so the old path never takes over unannounced.
    /// </summary>
    public bool OwnsRing(int pid, string ringId)
    {
        if (pid == 0 || !accounts.TryGetUserId(pid, out var userId)) return false;
        return store.Pulses.Any(p => p.Enabled && p.AccountUserId == userId
                                     && string.Equals(p.RingId, ringId, StringComparison.OrdinalIgnoreCase));
    }

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_loop is not null)
        {
            try { await _loop; }
            catch (OperationCanceledException) { }
        }
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
                log.Record(Guid.Empty, "(pulse)", ActivityKind.Error, $"watchdog: tick exceeded {WatchdogTimeout.TotalSeconds:0}s");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                log.Record(Guid.Empty, "(pulse)", ActivityKind.Error, ex.Message);
            }
            var remain = period - (clock.Now - tickStart);
            if (remain > TimeSpan.Zero)
            {
                try { await Task.Delay(remain, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    public async Task TickOnceAsync(CancellationToken ct)
    {
        if (Hold())
        {
            // Paused or dry run: no loop acts, but TickAsync(false, ...) still polls a macro a
            // loop already started (it cannot act with foreground: false, and calls NoteBehind
            // itself), so an in-flight playback keeps being followed and the held time does not
            // count toward any rock cap.
            foreach (var loop in _loops.Values.ToList())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await loop.TickAsync(false, 0, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // One loop's failure must not stop the others.
                    Write(loop.AccountUserId, $"tick failed: {ex.Message}");
                }
            }
            return;
        }
        Sync();
        if (_loops.Count == 0) return;

        var (front, pid) = Front();
        foreach (var loop in _loops.Values.ToList())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // Ur Task focuses the target on every run, so the gate is read again right before one.
                var account = loop.AccountUserId;
                await loop.TickAsync(account == front, pid, ct, () => !Hold() && Front().Front == account);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One loop's failure must not stop the others.
                Write(loop.AccountUserId, $"tick failed: {ex.Message}");
            }
        }
    }

    /// <summary>The account in front, the same gate triggers use: a RoRoRo alt, not elevated.</summary>
    private (long? Front, int Pid) Front()
    {
        if (!foreground.IsForegroundAnAlt()) return (null, 0);
        var pid = foreground.GetForegroundPid();
        if (elevation.IsForegroundProcessLikelyElevated(pid)) return (null, 0);
        return accounts.TryGetUserId(pid, out var userId) ? (userId, pid) : (null, 0);
    }

    private void Sync()
    {
        var enabled = store.Pulses.Where(p => p.Enabled).ToList();
        foreach (var gone in _loops.Keys.Where(id => enabled.All(p => p.AccountUserId != id)).ToList())
        {
            _loops.Remove(gone);
            Write(gone, "pulse turned off");
        }
        foreach (var pulse in enabled)
        {
            if (_loops.ContainsKey(pulse.AccountUserId)) continue;   // first pulse per account wins
            var account = pulse.AccountUserId;
            _loops[account] = new PulseLoop(pulse, store.Rings, store.All, reader, macros, clock,
                message => Write(account, message));
        }
    }

    private void Write(long account, string message)
    {
        log.Record(Guid.Empty, $"(pulse {account})", ActivityKind.Pulse, message);
        diag?.Invoke($"pulse {account}: {message}");
    }
}
