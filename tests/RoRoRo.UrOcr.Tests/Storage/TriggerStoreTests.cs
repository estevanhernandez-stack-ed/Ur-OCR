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
    public void Corrupted_file_backed_up_and_recovered_empty()
    {
        File.WriteAllText(_tempPath, "{not valid json");
        var s = new TriggerStore(_tempPath);
        Assert.Empty(s.All);
        Assert.NotNull(s.CorruptedBackupPath);
        Assert.True(File.Exists(s.CorruptedBackupPath));
    }
}
