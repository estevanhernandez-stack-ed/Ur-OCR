using System.Text.Json;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Storage;

public class TriggerActionTests
{
    private static Trigger NewColorTrigger() => new()
    {
        Id = Guid.NewGuid(), Name = "t", Region = new RegionRect(0, 0, 4, 4),
        Mode = TriggerMode.Color, Color = new ColorCriteria(new Rgb(1, 2, 3), 10, ColorSamplingMode.SinglePixel),
        Keybind = new KeyCombo("F", Array.Empty<string>()),
    };

    [Fact]
    public void Action_DefaultsToKeyChord()
        => Assert.Equal(TriggerAction.KeyChord, NewColorTrigger().Action);

    [Fact]
    public void RunMacroFields_RoundTrip_CamelCase()
    {
        var t = NewColorTrigger();
        t.Action = TriggerAction.RunMacro;
        t.MacroId = "f4e5d6c7-0000-0000-0000-000000000000";

        var json = JsonSerializer.Serialize(t, TriggerJsonOptions.Default);
        Assert.Contains("\"action\": \"runMacro\"", json);
        Assert.Contains("\"macroId\": \"f4e5d6c7", json);

        var back = JsonSerializer.Deserialize<Trigger>(json, TriggerJsonOptions.Default)!;
        Assert.Equal(TriggerAction.RunMacro, back.Action);
        Assert.Equal(t.MacroId, back.MacroId);
    }

    [Fact]
    public void LegacyTrigger_NoActionField_DeserializesAsKeyChord()
    {
        // A trigger written before this change has no "action" key.
        var legacy = "{\"id\":\"" + Guid.NewGuid() + "\",\"name\":\"t\",\"enabled\":true," +
                     "\"region\":{\"x\":0,\"y\":0,\"width\":4,\"height\":4},\"mode\":\"color\"," +
                     "\"color\":{\"targetRgb\":{\"r\":1,\"g\":2,\"b\":3},\"toleranceRgb\":10,\"samplingMode\":\"singlePixel\"}," +
                     "\"keybind\":{\"key\":\"F\",\"modifiers\":[]},\"cooldownMs\":2000}";
        var t = JsonSerializer.Deserialize<Trigger>(legacy, TriggerJsonOptions.Default)!;
        Assert.Equal(TriggerAction.KeyChord, t.Action);
    }
}
