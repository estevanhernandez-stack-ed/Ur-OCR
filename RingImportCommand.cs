using System.Diagnostics;
using System.IO;
using System.Text.Json;
using RoRoRo.UrOcr.Diagnostics;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr;

/// <summary>
/// RoRoRo.UrOcr.exe --import-ring &lt;measured.json&gt; writes a measured ring (its layers,
/// eight spot triggers, the rock cap and the camera rule) into triggers.json and exits
/// without opening a window. Ur OCR must be closed: a running instance rewrites
/// triggers.json from memory. The result goes to ring-import.log in the plugin data
/// folder and to ur-ocr.log. Exit codes: 0 imported, 2 bad input, 3 Ur OCR running, 64 usage.
/// </summary>
internal static class RingImportCommand
{
    public const string Flag = "--import-ring";

    public static string ReportPath => Path.Combine(PluginPaths.PluginDataDir, "ring-import.log");

    public static int Run(IReadOnlyList<string> args) =>
        Run(args, PluginPaths.TriggersFile, UrTaskMacros.MacrosDir, OtherInstanceRunning, ReportPath, DiagLog.Write);

    public static int Run(IReadOnlyList<string> args, string triggersPath, string macrosDir,
        Func<bool> otherInstanceRunning, string reportPath, Action<string> diag)
    {
        var lines = new List<string>();
        int code;
        if (args.Count != 1)
        {
            lines.Add($"usage: RoRoRo.UrOcr.exe {Flag} <measured.json>");
            code = 64;
        }
        else if (otherInstanceRunning())
        {
            lines.Add("Ur OCR is running. Close it first: a running Ur OCR rewrites triggers.json and would undo the import.");
            code = 3;
        }
        else
        {
            try
            {
                var measured = MeasuredRing.Load(args[0]);
                var macros = UrTaskMacros.Load(macrosDir);
                var result = RingImporter.Build(measured, macros);   // fails before the store is touched
                var store = new TriggerStore(triggersPath);
                if (store.CorruptedBackupPath is { } backup)
                    lines.Add($"triggers.json was unreadable; the old file is kept at {backup}");
                RingImporter.Apply(store, measured, macros);
                lines.Add($"imported ring {result.Ring.Id} ({result.Ring.Name}): {result.Ring.Layers.Count} layers, " +
                          $"{result.Triggers.Count} triggers into {triggersPath}");
                foreach (var t in result.Triggers) lines.Add($"  {t.Name} -> macro {t.MacroId}");
                code = 0;
            }
            catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException or UnauthorizedAccessException)
            {
                lines.Add($"import failed: {ex.Message}");
                code = 2;
            }
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            File.WriteAllLines(reportPath, lines);
        }
        catch (IOException) { }
        foreach (var line in lines) diag($"ring import: {line}");
        return code;
    }

    private static bool OtherInstanceRunning()
    {
        var me = Environment.ProcessId;
        return Process.GetProcessesByName("RoRoRo.UrOcr").Any(p => p.Id != me);
    }
}
