using System;
using System.Collections.Generic;
using System.IO;

namespace RoRoRo.UrOcr;

/// <summary>
/// The write-a-report-then-mirror-it-to-diag tail shared by <see cref="PulseImportCommand"/> and
/// <see cref="RingImportCommand"/>: best-effort file (a locked file is not fatal to the import,
/// which has already succeeded or failed by the time this runs), then every line to diag with a
/// prefix so it also lands in ur-ocr.log.
/// </summary>
internal static class ImportReport
{
    public static void Write(string reportPath, IReadOnlyList<string> lines, string prefix, Action<string> diag)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            File.WriteAllLines(reportPath, lines);
        }
        catch (IOException) { }
        foreach (var line in lines) diag($"{prefix}: {line}");
    }
}
