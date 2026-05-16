using System;
using System.IO;
using System.Text.Json;

namespace RoRoRo.UrOcr.Storage;

public sealed class SettingsStore
{
    private readonly string _path;
    public Settings Current { get; private set; }

    public SettingsStore() : this(PluginPaths.SettingsFile) { }

    public SettingsStore(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        Current = File.Exists(_path)
            ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(_path), TriggerJsonOptions.Default) ?? new Settings()
            : new Settings();
    }

    public void Save(Settings s)
    {
        Current = s;
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(s, TriggerJsonOptions.Default));
        File.Move(tmp, _path, overwrite: true);
    }
}
