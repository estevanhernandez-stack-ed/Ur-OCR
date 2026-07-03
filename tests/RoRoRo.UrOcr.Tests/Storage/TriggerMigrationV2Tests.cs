using System.IO;
using System.Text.Json;
using Xunit;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Tests.Storage;

public class TriggerMigrationV2Tests
{
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "urocr-tests", System.Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void LegacyV1Triggers_LoadAsScreen_AndBumpSchema()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // A v1 file: no schemaVersion default is 1, triggers have no coordSpace.
        File.WriteAllText(path, """
        {
          "schemaVersion": 1,
          "triggers": [
            { "id": "11111111-1111-1111-1111-111111111111", "name": "t",
              "enabled": true, "region": { "x": 10, "y": 20, "width": 30, "height": 40 },
              "mode": "color", "accountAware": true,
              "keybind": { "key": "F", "modifiers": [] } }
          ]
        }
        """);
        var store = new TriggerStore(path);
        var t = Assert.Single(store.All);
        Assert.Equal(Trigger.CoordSpaceScreen, t.CoordSpace);
        Assert.False(t.IsClientSpace);
        Assert.Null(t.RecordedClientW);
        Assert.Equal(10, t.Region.X); // region untouched

        // Sticky: the file now serializes at schemaVersion 2 with coordSpace.
        var reread = File.ReadAllText(path);
        Assert.Contains("\"schemaVersion\": 2", reread);
        Assert.Contains("\"coordSpace\": \"screen\"", reread);
    }

    [Fact]
    public void ClientTrigger_RoundTrips_WithRecordedClientSize()
    {
        var path = TempFile();
        var store = new TriggerStore(path);
        store.Add(new Trigger
        {
            Id = System.Guid.NewGuid(),
            Name = "c",
            Region = new RegionRect(5, 6, 7, 8),
            Mode = TriggerMode.Color,
            Keybind = new KeyCombo("F", System.Array.Empty<string>()),
            CoordSpace = Trigger.CoordSpaceClient,
            RecordedClientW = 816,
            RecordedClientH = 638,
        });

        var reloaded = new TriggerStore(path);
        var t = Assert.Single(reloaded.All);
        Assert.True(t.IsClientSpace);
        Assert.Equal(816, t.RecordedClientW);
        Assert.Equal(638, t.RecordedClientH);
    }

    [Fact]
    public void ScreenTrigger_OmitsRecordedClientSize_InJson()
    {
        var t = new Trigger
        {
            Id = System.Guid.NewGuid(), Name = "s",
            Region = new RegionRect(1, 2, 3, 4), Mode = TriggerMode.Color,
            Keybind = new KeyCombo("F", System.Array.Empty<string>()),
            CoordSpace = Trigger.CoordSpaceScreen,
        };
        var json = JsonSerializer.Serialize(t, TriggerJsonOptions.Default);
        Assert.DoesNotContain("recordedClientW", json);
        Assert.DoesNotContain("recordedClientH", json);
    }
}
