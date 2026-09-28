using System;
using System.IO;
using Xunit;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Tests.Storage;

public class TriggerStoreTests : IDisposable
{
    private readonly string _tempPath = Path.Combine(Path.GetTempPath(), $"urocr-test-{Guid.NewGuid()}.json");

    public void Dispose()
    {
        if (File.Exists(_tempPath)) File.Delete(_tempPath);
        foreach (var f in Directory.EnumerateFiles(Path.GetTempPath(), $"{Path.GetFileNameWithoutExtension(_tempPath)}*.corrupted-*"))
            File.Delete(f);
    }

    private Trigger NewTrigger() => new()
    {
        Id = Guid.NewGuid(),
        Name = "T",
        Region = new RegionRect(0, 0, 10, 10),
        Mode = TriggerMode.Color,
        Color = new ColorCriteria(new Rgb(0, 0, 0), 5, ColorSamplingMode.SinglePixel),
        Keybind = new KeyCombo("A", Array.Empty<string>()),
    };

    [Fact]
    public void Add_then_Reload_persists()
    {
        var s = new TriggerStore(_tempPath);
        var t = NewTrigger();
        s.Add(t);

        var s2 = new TriggerStore(_tempPath);
        Assert.Single(s2.All);
        Assert.Equal(t.Id, s2.All[0].Id);
    }

    [Fact]
    public void Update_changes_persisted_state()
    {
        var s = new TriggerStore(_tempPath);
        var t = NewTrigger();
        s.Add(t);
        t.Name = "renamed";
        s.Update(t);
        Assert.Equal("renamed", new TriggerStore(_tempPath).All[0].Name);
    }

    [Fact]
    public void Remove_drops_trigger()
    {
        var s = new TriggerStore(_tempPath);
        var t = NewTrigger();
        s.Add(t);
        s.Remove(t.Id);
        Assert.Empty(new TriggerStore(_tempPath).All);
    }

    [Fact]
    public void A_legacy_negative_cooldown_is_repaired_to_zero_on_load()
    {
        // 0.4.0's editor let -1 through; its ready check treated a negative cooldown the
        // same as 0 ("always ready"). 0.5.0's validation now rejects a negative cooldown
        // outright, so a legacy trigger must be repaired on load rather than go dead.
        var s = new TriggerStore(_tempPath);
        var t = NewTrigger();
        t.CooldownMs = -1;
        s.Add(t);

        var reloaded = new TriggerStore(_tempPath);

        Assert.Equal(0, reloaded.All[0].CooldownMs);
        Assert.Null(TriggerValidation.Validate(reloaded.All[0], Array.Empty<RingDefinition>()));
    }

    [Fact]
    public void A_hand_edited_null_pulse_entry_is_dropped_on_load()
    {
        // "pulses": [null] would otherwise NRE inside the coordinator's tick and stop every
        // trigger, not just this account's pulse. Pulses only ever arrive through the validating
        // importer, so a null entry can only come from a hand edit.
        File.WriteAllText(_tempPath, """
            {
              "schemaVersion": 2,
              "rings": [],
              "triggers": [],
              "pulses": [ null, { "accountUserId": 42, "ringId": "mine8", "targetLayer": 3 } ]
            }
            """);

        var s = new TriggerStore(_tempPath);

        var pulse = Assert.Single(s.Pulses);
        Assert.Equal(42, pulse.AccountUserId);
    }

    [Fact]
    public void Corrupted_file_backed_up_and_recovered_empty()
    {
        File.WriteAllText(_tempPath, "{not valid json");
        var s = new TriggerStore(_tempPath);
        Assert.Empty(s.All);
        Assert.NotNull(s.CorruptedBackupPath);
        Assert.True(File.Exists(s.CorruptedBackupPath));
    }
}
