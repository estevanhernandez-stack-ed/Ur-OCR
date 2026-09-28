using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RoRoRo.UrOcr.Storage;

public sealed class TriggerStore
{
    private readonly string _path;
    private readonly object _lock = new();
    private TriggersFile _state = new();
    private DateTimeOffset _lastFlush = DateTimeOffset.MinValue;

    public TriggerStore() : this(PluginPaths.TriggersFile) { }

    public TriggerStore(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        Load();
    }

    public IReadOnlyList<Trigger> All
    {
        get { lock (_lock) return _state.Triggers.ToArray(); }
    }

    public IReadOnlyList<RingDefinition> Rings
    {
        get { lock (_lock) return _state.Rings.ToArray(); }
    }

    public IReadOnlyList<PulseConfig> Pulses
    {
        get { lock (_lock) return _state.Pulses.ToArray(); }
    }

    /// <summary>Adds the trigger, or replaces the one with the same id.</summary>
    public void Upsert(Trigger t)
    {
        lock (_lock)
        {
            var idx = _state.Triggers.FindIndex(x => x.Id == t.Id);
            if (idx < 0) _state.Triggers.Add(t); else _state.Triggers[idx] = t;
            WriteNow();
        }
    }

    /// <summary>Adds the ring, or replaces the one with the same id (compared ignoring case).</summary>
    public void UpsertRing(RingDefinition ring)
    {
        lock (_lock)
        {
            var idx = _state.Rings.FindIndex(x => string.Equals(x.Id, ring.Id, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) _state.Rings.Add(ring); else _state.Rings[idx] = ring;
            WriteNow();
        }
    }

    /// <summary>Adds the pulse, or replaces the one for the same account: one pulse per account.</summary>
    public void UpsertPulse(PulseConfig pulse)
    {
        lock (_lock)
        {
            var idx = _state.Pulses.FindIndex(x => x.AccountUserId == pulse.AccountUserId);
            if (idx < 0) _state.Pulses.Add(pulse); else _state.Pulses[idx] = pulse;
            WriteNow();
        }
    }

    public void Add(Trigger t)
    {
        lock (_lock) { _state.Triggers.Add(t); WriteNow(); }
    }

    public void Update(Trigger t)
    {
        lock (_lock)
        {
            var idx = _state.Triggers.FindIndex(x => x.Id == t.Id);
            if (idx < 0) throw new InvalidOperationException($"Trigger {t.Id} not found");
            _state.Triggers[idx] = t;
            WriteNow();
        }
    }

    public void Remove(Guid id)
    {
        lock (_lock) { _state.Triggers.RemoveAll(x => x.Id == id); WriteNow(); }
    }

    public void MarkFired(Guid id, DateTimeOffset when)
    {
        lock (_lock)
        {
            var t = _state.Triggers.FirstOrDefault(x => x.Id == id);
            if (t is null) return;
            t.LastFiredAt = when;
            t.HitCount++;
            t.FirstFireConfirmed = true;
            DebouncedFlush(when);
        }
    }

    public void FlushNow() { lock (_lock) WriteNow(); }

    private void DebouncedFlush(DateTimeOffset now)
    {
        if ((now - _lastFlush).TotalSeconds >= 5) WriteNow();
    }

    private void WriteNow()
    {
        var tmp = _path + ".tmp";
        var json = JsonSerializer.Serialize(_state, TriggerJsonOptions.Default);
        File.WriteAllText(tmp, json);
        File.Move(tmp, _path, overwrite: true);
        _lastFlush = DateTimeOffset.UtcNow;
    }

    private void Load()
    {
        if (!File.Exists(_path)) { _state = new TriggersFile(); return; }
        try
        {
            var json = File.ReadAllText(_path);
            _state = JsonSerializer.Deserialize<TriggersFile>(json, TriggerJsonOptions.Default)
                     ?? new TriggersFile();
            _state.Rings ??= new();   // "rings": null in a hand-edited file
            _state.Pulses ??= new();  // "pulses": null in a hand-edited file
            // "pulses": [null] in a hand-edited file: pulses only ever arrive through the
            // validating importer, so a null entry can only come from a hand edit. Left in,
            // it would NRE inside the coordinator's tick and stop every trigger, not just this
            // account's pulse.
            _state.Pulses.RemoveAll(p => p is null);
            if (MigrateToV2()) WriteNow(); // sticky
        }
        catch (JsonException)
        {
            var backup = _path + $".corrupted-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
            File.Copy(_path, backup, overwrite: false);
            _state = new TriggersFile();
            CorruptedBackupPath = backup;
        }
    }

    // v1 → v2: triggers with no coordSpace are absolute-screen. Returns true if
    // anything changed (so the caller persists the migration).
    private bool MigrateToV2()
    {
        var changed = false;
        if (_state.SchemaVersion < 2) { _state.SchemaVersion = 2; changed = true; }
        foreach (var t in _state.Triggers)
        {
            if (string.IsNullOrEmpty(t.CoordSpace)) { t.CoordSpace = Trigger.CoordSpaceScreen; changed = true; }
            // 0.4.0's editor bound the cooldown box to a raw int, so a legacy trigger can carry
            // a negative CooldownMs. 0.4.0's check (elapsed >= CooldownMs) was always true for a
            // negative value, i.e. "always ready" -- the same as 0 -- so repair it to 0 rather
            // than let TriggerValidation reject the trigger outright and take it out of service.
            if (t.CooldownMs < 0) { t.CooldownMs = 0; changed = true; }
        }
        return changed;
    }

    public string? CorruptedBackupPath { get; private set; }
}
