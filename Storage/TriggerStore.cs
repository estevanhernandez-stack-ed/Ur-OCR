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
        }
        return changed;
    }

    public string? CorruptedBackupPath { get; private set; }
}
