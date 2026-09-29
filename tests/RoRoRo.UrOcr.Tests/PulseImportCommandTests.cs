using System.IO;
using System.Text.Json;
using RoRoRo.UrOcr.Storage;
using RoRoRo.UrOcr.Tests.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests;

public class PulseImportCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "urocr-tests", "pulse-" + Guid.NewGuid().ToString("N"));

    public PulseImportCommandTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "macros"));
        RingImporter.Apply(new TriggerStore(Triggers), RingImporterTests.Measured(), RingImporterTests.Macros());
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Triggers => Path.Combine(_dir, "triggers.json");
    private string Report => Path.Combine(_dir, "pulse-import.log");
    private string MacrosDir => Path.Combine(_dir, "macros");

    private string WritePulses()
    {
        var path = Path.Combine(_dir, "pulse.json");
        File.WriteAllText(path,
            "{\"schema\":1,\"pulses\":[" +
            "{\"accountUserId\":42,\"ringId\":\"mine8\",\"targetLayer\":2}," +
            "{\"accountUserId\":43,\"ringId\":\"mine8\",\"targetLayer\":2,\"mode\":\"oneAbove\",\"rockCapMinutes\":1}]}");
        return path;
    }

    private void WriteMacros()
    {
        foreach (var mac in PulseImporterTests.Macros())
            File.WriteAllText(Path.Combine(MacrosDir, mac.Id + ".json"), JsonSerializer.Serialize(new { id = mac.Id, name = mac.Name }));
    }

    private int Run(Func<bool> running, params string[] args) =>
        PulseImportCommand.Run(args, Triggers, MacrosDir, running, Report, _ => { });

    [Fact]
    public void Imports_the_pulses_and_reports_them()
    {
        WriteMacros();

        var code = Run(() => false, WritePulses());

        Assert.Equal(0, code);
        Assert.Equal(2, new TriggerStore(Triggers).Pulses.Count);
        var report = File.ReadAllText(Report);
        Assert.Contains("imported 2 pulse loops", report);
        Assert.Contains("account 42: ring mine8, target layer 2 (top), burst 2000 ms, settle 1000 ms, rock cap 5 min", report);
        Assert.Contains("account 43: ring mine8, target layer 2 (one above)", report);
    }

    [Fact]
    public void Without_a_camera_turn_macro_the_report_says_the_pulse_will_not_look_around()
    {
        WriteMacros();

        Assert.Equal(0, Run(() => false, WritePulses()));

        Assert.Contains("no Camera turn left macro: the pulse won't look around", File.ReadAllText(Report));
    }

    [Fact]
    public void With_a_camera_turn_macro_the_report_says_nothing_about_it()
    {
        WriteMacros();
        File.WriteAllText(Path.Combine(MacrosDir, "id-turn.json"), JsonSerializer.Serialize(new { id = "id-turn", name = "Camera turn left" }));

        Assert.Equal(0, Run(() => false, WritePulses()));

        Assert.DoesNotContain("Camera turn left", File.ReadAllText(Report));
        Assert.All(new TriggerStore(Triggers).Pulses, p => Assert.Equal("id-turn", p.Macros!.CameraTurnLeft));
    }

    [Fact]
    public void Refuses_while_Ur_OCR_is_running()
    {
        WriteMacros();

        var code = Run(() => true, WritePulses());

        Assert.Equal(3, code);
        Assert.Empty(new TriggerStore(Triggers).Pulses);
        Assert.Contains("Close it first", File.ReadAllText(Report));
    }

    [Fact]
    public void Missing_macros_exit_2_and_write_no_pulse()
    {
        var code = Run(() => false, WritePulses());

        Assert.Equal(2, code);
        Assert.Empty(new TriggerStore(Triggers).Pulses);
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
    public void No_file_named_is_a_usage_error()
    {
        Assert.Equal(64, Run(() => false));
        Assert.Contains("usage", File.ReadAllText(Report));
    }
}
