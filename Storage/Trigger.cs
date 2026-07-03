using System.Text.Json.Serialization;

namespace RoRoRo.UrOcr.Storage;

public enum TriggerMode { Text, Color }
public enum TextMatchType { Contains, Exact, Regex }
public enum ColorSamplingMode { SinglePixel, RegionAverage }
public enum TriggerAction { KeyChord, RunMacro }

public sealed record RegionRect(int X, int Y, int Width, int Height);
public sealed record Rgb(int R, int G, int B);

public sealed record TextCriteria(string Needle, bool CaseSensitive, TextMatchType MatchType);
public sealed record ColorCriteria(Rgb TargetRgb, int ToleranceRgb, ColorSamplingMode SamplingMode);

public sealed record KeyCombo(string Key, IReadOnlyList<string> Modifiers);

public sealed class Trigger
{
    public required Guid Id { get; init; }
    public required string Name { get; set; }
    public bool Enabled { get; set; } = true;
    public required RegionRect Region { get; set; }
    public required TriggerMode Mode { get; set; }
    public TextCriteria? Text { get; set; }
    public ColorCriteria? Color { get; set; }
    public bool OcrPreprocess { get; set; }
    public bool AccountAware { get; set; } = true;
    public required KeyCombo Keybind { get; set; }
    // Window-anchoring (schema v2). "screen" = absolute pixels (legacy);
    // "client" = Region is relative to the foreground alt's client area at the
    // recorded client size, scaled to the live size at eval. Mirrors
    // Macro.CoordSpace in Ur Task.
    public string? CoordSpace { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? RecordedClientW { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? RecordedClientH { get; set; }

    public const string CoordSpaceScreen = "screen";
    public const string CoordSpaceClient = "client";
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsClientSpace =>
        string.Equals(CoordSpace, CoordSpaceClient, System.StringComparison.OrdinalIgnoreCase);
    // Fire action: press the keybind (default, legacy) or run a Ur Task macro
    // via the action bridge. Additive — legacy triggers with no "action" key
    // deserialize as KeyChord (System.Text.Json leaves the default).
    public TriggerAction Action { get; set; } = TriggerAction.KeyChord;
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? MacroId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? MacroTargets { get; set; }   // null => foreground alt
    public int CooldownMs { get; set; } = 2000;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastFiredAt { get; set; }
    public long HitCount { get; set; }
    public bool FirstFireConfirmed { get; set; }
}

public sealed class TriggersFile
{
    public int SchemaVersion { get; set; } = 2;
    public List<Trigger> Triggers { get; set; } = new();
}

internal static class TriggerJsonOptions
{
    public static readonly System.Text.Json.JsonSerializerOptions Default = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase) },
    };
}
