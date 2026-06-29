namespace RoRoRo.UrOcr.Engine;

public enum ActivityKind { Fired, WouldFire, NoMatch, SkippedCooldown, SkippedNotAlt, BlockedElevated, Error }

public sealed record ActivityEntry(DateTimeOffset At, Guid TriggerId, string TriggerName, ActivityKind Kind, string? Detail);

public sealed class ActivityLog
{
    private readonly object _gate = new();
    private readonly LinkedList<ActivityEntry> _buf = new();
    public int Capacity { get; }
    public event Action? Changed;

    public ActivityLog(int capacity = 100) { Capacity = capacity; }

    public void Record(Guid triggerId, string name, ActivityKind kind, string? detail = null)
    {
        lock (_gate)
        {
            _buf.AddFirst(new ActivityEntry(DateTimeOffset.UtcNow, triggerId, name, kind, detail));
            while (_buf.Count > Capacity) _buf.RemoveLast();
        }
        Changed?.Invoke();
    }

    public IReadOnlyList<ActivityEntry> Snapshot()
    {
        lock (_gate) return _buf.ToArray();
    }
}
