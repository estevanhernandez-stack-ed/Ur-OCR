using System.Text.Json.Serialization;

namespace RoRoRo.UrOcr.Storage;

public enum TriggerMode { Text, Color, Layer }
public enum TextMatchType { Contains, Exact, Regex }
public enum ColorSamplingMode { SinglePixel, RegionAverage }
public enum TriggerAction { KeyChord, RunMacro }

public sealed record RegionRect(int X, int Y, int Width, int Height);
public sealed record Rgb(int R, int G, int B);

public sealed record TextCriteria(string Needle, bool CaseSensitive, TextMatchType MatchType);
/// <summary>Where the picker was clicked, in pixels from the recorded region's top-left.</summary>
public sealed record PickPoint(int X, int Y);

/// <summary>
/// The box averaged around a PickPoint: an offset from the point plus a size.
/// Same shape as Ur Task's CheckBox ({ offsetX, offsetY, w, h }), so both
/// plugins sample colour the same way.
/// </summary>
public sealed record SampleBox(int OffsetX = -2, int OffsetY = -2, int W = 5, int H = 5)
{
    public const int MaxSide = 9;
    [JsonIgnore]
    public bool IsValid => W >= 1 && H >= 1 && W <= MaxSide && H <= MaxSide;
}

/// <summary>
/// Colour trigger criteria. When <see cref="Box"/> is set, the check averages
/// that box around <see cref="Point"/>, the same box the picker averaged, and
/// <see cref="SamplingMode"/> is ignored (new triggers write SinglePixel so an
/// older build still loads them). Without a box, legacy behaviour: SinglePixel
/// reads the region centre, RegionAverage the whole region. <see cref="Other"/>
/// is the opposite state's colour: matched means within tolerance of the
/// target AND closer to it than to Other.
/// <para>
/// <see cref="NoneOf"/> turns the check around: when set, the trigger matches
/// when the sample is more than <see cref="ToleranceRgb"/> from EVERY listed
/// colour, and <see cref="TargetRgb"/> is ignored (any in-range value; the ring
/// importer writes black). NoneOf cannot be combined with Other. An empty list
/// is valid only on a ring spot, where the ring's current layer adds its rock.
/// </para>
/// </summary>
public sealed record ColorCriteria(
    Rgb TargetRgb, int ToleranceRgb, ColorSamplingMode SamplingMode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PickPoint? Point = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SampleBox? Box = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Rgb? Other = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Rgb>? NoneOf = null)
{
    /// <summary>The largest possible RGB distance, sqrt(3 x 255^2) = 441.67, rounded up.</summary>
    public const int MaxTolerance = 442;

    [JsonIgnore]
    public bool IsNoneOf => NoneOf is not null;

    /// <summary>Null when valid, else one sentence naming the problem.</summary>
    public string? Validate(bool layerSupplied = false)
    {
        if (ToleranceRgb < 0 || ToleranceRgb > MaxTolerance)
            return $"Tolerance must be 0 to {MaxTolerance}, not {ToleranceRgb}.";
        if (TargetRgb is null || !InRange(TargetRgb)) return "The target colour has a channel outside 0 to 255.";
        if (Other is not null && !InRange(Other)) return "The other colour has a channel outside 0 to 255.";
        if (Box is not null && !Box.IsValid) return $"The sample box must be 1 to {SampleBox.MaxSide} pixels a side.";
        if (NoneOf is null) return null;
        if (Other is not null) return "A none-of check cannot also have an other colour.";
        if (NoneOf.Count == 0 && !layerSupplied) return "A none-of check needs at least one colour.";
        foreach (var c in NoneOf)
            if (c is null || !InRange(c)) return "A none-of colour has a channel outside 0 to 255.";
        return null;
    }

    public static bool InRange(Rgb c) =>
        c.R is >= 0 and <= 255 && c.G is >= 0 and <= 255 && c.B is >= 0 and <= 255;
}

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
    // Ore stop (0.5.0), all additive: absent keys load as null / 0.
    /// <summary>Set on the eight colour triggers of a ring.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public RingSpot? Ring { get; set; }
    /// <summary>Criteria of a <see cref="TriggerMode.Layer"/> trigger.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public LayerCriteria? Layer { get; set; }
    /// <summary>The match must hold unbroken this long before the trigger fires, and a fire
    /// starts a fresh hold. 0 = fire on the no-match to match edge, as before.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int HoldForMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastFiredAt { get; set; }
    public long HitCount { get; set; }
    public bool FirstFireConfirmed { get; set; }
}

public sealed class TriggersFile
{
    public int SchemaVersion { get; set; } = 2;
    public List<RingDefinition> Rings { get; set; } = new();
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
