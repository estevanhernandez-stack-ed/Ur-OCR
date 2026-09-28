using System.IO;
using System.Text.Json;
using RoRoRo.UrOcr.Diagnostics;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr;

/// <summary>
/// RoRoRo.UrOcr.exe --import-pulse &lt;pulse.json&gt; writes one ore stop pulse per account into
/// triggers.json and exits without opening a window. Import the ring first (--import-ring): a pulse
/// reads that ring's layers and spot triggers. Ur OCR must be closed, and Ur Task's macros must be
/// in its macro folder (the names are looked up there). The result goes to pulse-import.log in the
/// plugin data folder and to ur-ocr.log. Exit codes: 0 imported, 2 bad input, 3 Ur OCR running, 64 usage.
/// </summary>
internal static class PulseImportCommand
{
    public const string Flag = "--import-pulse";

    public static string ReportPath => Path.Combine(PluginPaths.PluginDataDir, "pulse-import.log");

    public static int Run(IReadOnlyList<string> args) =>
        Run(args, PluginPaths.TriggersFile, UrTaskMacros.MacrosDir, RingImportCommand.OtherInstanceRunning, ReportPath, DiagLog.Write);

    public static int Run(IReadOnlyList<string> args, string triggersPath, string macrosDir,
        Func<bool> otherInstanceRunning, string reportPath, Action<string> diag)
    {
        var lines = new List<string>();
        int code;
        if (args.Count != 1)
        {
            lines.Add($"usage: RoRoRo.UrOcr.exe {Flag} <pulse.json>");
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
                var file = PulseFile.Load(args[0]);
                var macros = UrTaskMacros.Load(macrosDir);
                var store = new TriggerStore(triggersPath);
                if (store.CorruptedBackupPath is { } backup)
                    lines.Add($"triggers.json was unreadable; the old file is kept at {backup}");
                var pulses = PulseImporter.Apply(store, file, macros);   // builds first: a bad file writes nothing
                lines.Add($"imported {pulses.Count} pulse loop{(pulses.Count == 1 ? "" : "s")} into {triggersPath}");
                foreach (var p in pulses) lines.Add("  " + Describe(p));
                code = 0;
            }
            catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException or UnauthorizedAccessException)
            {
                lines.Add($"import failed: {ex.Message}");
                code = 2;
            }
        }

        ImportReport.Write(reportPath, lines, "pulse import", diag);
        return code;
    }

    internal static string Describe(PulseConfig p) =>
        $"account {p.AccountUserId}: ring {p.RingId}, target layer {p.TargetLayer} " +
        $"({(p.Mode == PulseMode.OneAbove ? "one above" : "top")}), burst {p.BurstMs} ms, " +
        $"settle {p.SettleMs} ms, rock cap {p.RockCapMinutes} min{(p.Enabled ? "" : ", turned off")}";
}
