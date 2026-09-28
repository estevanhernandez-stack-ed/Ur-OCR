using System.IO;
using System.Text.Json;
using RoRoRo.UrOcr.Storage;
using RoRoRo.UrOcr.Tests.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests;

public class RingImportCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "urocr-tests", "import-" + Guid.NewGuid().ToString("N"));

    public RingImportCommandTests() => Directory.CreateDirectory(Path.Combine(_dir, "macros"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Triggers => Path.Combine(_dir, "triggers.json");
    private string Report => Path.Combine(_dir, "ring-import.log");
    private string MacrosDir => Path.Combine(_dir, "macros");

    private string WriteMeasured(MeasuredRing? measured = null)
    {
        var path = Path.Combine(_dir, "mine8.measured.json");
        File.WriteAllText(path, JsonSerializer.Serialize(measured ?? RingImporterTests.Measured(), TriggerJsonOptions.Default));
        return path;
    }

    private void WriteMacros()
    {
        foreach (var mac in RingImporterTests.Macros())
            File.WriteAllText(Path.Combine(MacrosDir, mac.Id + ".json"), JsonSerializer.Serialize(new { id = mac.Id, name = mac.Name }));
    }

    private int Run(Func<bool> running, params string[] args) =>
        RingImportCommand.Run(args, Triggers, MacrosDir, running, Report, _ => { });

    [Fact]
    public void Imports_the_ring_and_reports_it()
    {
        WriteMacros();

        var code = Run(() => false, WriteMeasured());

        Assert.Equal(0, code);
        Assert.Equal(10, new TriggerStore(Triggers).All.Count);
        Assert.Contains("imported ring mine8", File.ReadAllText(Report));
    }

    [Fact]
    public void Reports_the_ore_finder_it_imported()
    {
        WriteMacros();

        var code = Run(() => false,
            WriteMeasured(RingImporterTests.Measured() with { Finders = new[] { RingImporterTests.Finder() } }));

        Assert.Equal(0, code);
        Assert.Contains("ore finder on grey (50 px blocks, 1 ore colour)", File.ReadAllText(Report));
        Assert.NotNull(Assert.Single(new TriggerStore(Triggers).Rings).Finders);
    }

    [Fact]
    public void Refuses_while_Ur_OCR_is_running()
    {
        WriteMacros();

        var code = Run(() => true, WriteMeasured());

        Assert.Equal(3, code);
        Assert.False(File.Exists(Triggers));
        Assert.Contains("Close it first", File.ReadAllText(Report));
    }

    [Fact]
    public void Missing_macros_exit_2_and_write_nothing()
    {
        var code = Run(() => false, WriteMeasured());

        Assert.Equal(2, code);
        Assert.False(File.Exists(Triggers));
        Assert.Contains("No Ur Task macro", File.ReadAllText(Report));
    }

    [Fact]
    public void A_missing_file_exits_2_and_says_why()
    {
        var code = Run(() => false, Path.Combine(_dir, "nope.json"));

        Assert.Equal(2, code);
        Assert.Contains("import failed", File.ReadAllText(Report));
    }

    [Fact]
    public void No_argument_prints_usage()
    {
        Assert.Equal(64, Run(() => false));
        Assert.Contains("usage", File.ReadAllText(Report));
    }
}
