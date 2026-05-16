using System;
using System.IO;

namespace RoRoRo.UrOcr.Storage;

public static class PluginPaths
{
    public static string PluginDataDir { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "626Labs", "rororo-ur-ocr");

    public static string TriggersFile => Path.Combine(PluginDataDir, "triggers.json");
    public static string SettingsFile => Path.Combine(PluginDataDir, "settings.json");
    public static string DisplayFingerprintFile => Path.Combine(PluginDataDir, "display-fingerprint.json");

    public static void EnsureDirectory() => Directory.CreateDirectory(PluginDataDir);
}
