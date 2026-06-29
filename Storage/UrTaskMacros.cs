using System.IO;
using System.Text.Json;

namespace RoRoRo.UrOcr.Storage;

public sealed record UrTaskMacro(string Id, string Name);

public static class UrTaskMacros
{
    public static string MacrosDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "626Labs", "RoRoRoUrTask", "macros");

    public static IReadOnlyList<UrTaskMacro> Load() => Load(MacrosDir);

    public static IReadOnlyList<UrTaskMacro> Load(string dir)
    {
        var result = new List<UrTaskMacro>();
        if (!Directory.Exists(dir)) return result;
        foreach (var path in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;
                if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                    result.Add(new UrTaskMacro(id.GetString()!,
                        root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : "(unnamed)"));
            }
            catch { /* skip unreadable */ }
        }
        return result;
    }
}
