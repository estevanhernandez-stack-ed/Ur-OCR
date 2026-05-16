using System.Text.Json.Serialization;

namespace RoRoRo.UrOcr.Storage;

public enum TriggerMode { Text, Color }
public enum TextMatchType { Contains, Exact, Regex }
public enum ColorSamplingMode { SinglePixel, RegionAverage }

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
    public int CooldownMs { get; set; } = 2000;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastFiredAt { get; set; }
    public long HitCount { get; set; }
    public bool FirstFireConfirmed { get; set; }
}

public sealed class TriggersFile
{
    public int SchemaVersion { get; set; } = 1;
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
