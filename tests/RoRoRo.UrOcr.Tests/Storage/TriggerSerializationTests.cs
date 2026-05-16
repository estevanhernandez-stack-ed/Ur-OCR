using System.Text.Json;
using Xunit;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Tests.Storage;

public class TriggerSerializationTests
{
    [Fact]
    public void RoundTrip_text_trigger_preserves_fields()
    {
        var t = new Trigger
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = "Boss",
            Region = new RegionRect(800, 40, 320, 60),
            Mode = TriggerMode.Text,
            Text = new TextCriteria("BOSS", false, TextMatchType.Contains),
            Keybind = new KeyCombo("F", Array.Empty<string>()),
        };
        var f = new TriggersFile { Triggers = { t } };
        var json = JsonSerializer.Serialize(f, TriggerJsonOptions.Default);
        var back = JsonSerializer.Deserialize<TriggersFile>(json, TriggerJsonOptions.Default)!;
        Assert.Single(back.Triggers);
        Assert.Equal("Boss", back.Triggers[0].Name);
        Assert.Equal(TriggerMode.Text, back.Triggers[0].Mode);
        Assert.Equal("BOSS", back.Triggers[0].Text!.Needle);
        Assert.Equal(2000, back.Triggers[0].CooldownMs);
    }

    [Fact]
    public void Color_mode_writes_camelCase_enum()
    {
        var t = new Trigger
        {
            Id = Guid.NewGuid(),
            Name = "Yellow",
            Region = new RegionRect(0, 0, 40, 40),
            Mode = TriggerMode.Color,
            Color = new ColorCriteria(new Rgb(255, 215, 0), 25, ColorSamplingMode.RegionAverage),
            Keybind = new KeyCombo("G", new[] { "Shift" }),
        };
        var json = JsonSerializer.Serialize(t, TriggerJsonOptions.Default);
        Assert.Contains("\"mode\": \"color\"", json);
        Assert.Contains("\"samplingMode\": \"regionAverage\"", json);
        Assert.Contains("\"modifiers\":", json);
    }
}
