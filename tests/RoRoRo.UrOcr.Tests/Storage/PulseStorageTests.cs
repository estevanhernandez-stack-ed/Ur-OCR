using System.IO;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Storage;

public class PulseStorageTests
{
    private static string TempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "urocr-tests", Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private static PulseMacros Macros() => new("id-off", "id-on", "id-top",
        MeasuredRing.RingOrder.Select(n => $"id-clear-{n}").ToList());

    private static PulseConfig Pulse(long account = 42, int target = 3, PulseMode mode = PulseMode.Top) =>
        new(account, "mine8", target, mode, Macros: Macros());

    [Fact]
    public void Pulses_survive_a_reload()
    {
        var path = TempFile();
        new TriggerStore(path).UpsertPulse(Pulse(mode: PulseMode.OneAbove) with { BurstMs = 1500, RockCapMinutes = 7 });

        var p = Assert.Single(new TriggerStore(path).Pulses);

        Assert.Equal(42, p.AccountUserId);
        Assert.Equal("mine8", p.RingId);
        Assert.Equal(3, p.TargetLayer);
        Assert.Equal(PulseMode.OneAbove, p.Mode);
        Assert.Equal(1500, p.BurstMs);
        Assert.Equal(1000, p.SettleMs);
        Assert.Equal(7, p.RockCapMinutes);
        Assert.True(p.Enabled);
        Assert.Equal("id-off", p.Macros!.AutoMineOff);
        Assert.Equal("id-on", p.Macros.AutoMineOn);
        Assert.Equal("id-top", p.Macros.GoToTop);
        Assert.Equal(MeasuredRing.RingOrder.Select(n => $"id-clear-{n}"), p.Macros.Clear);
    }

    [Fact]
    public void The_file_uses_camel_case_keys_and_the_spec_mode_names()
    {
        var path = TempFile();
        new TriggerStore(path).UpsertPulse(Pulse(mode: PulseMode.OneAbove));

        var json = File.ReadAllText(path);

        Assert.Contains("\"pulses\"", json);
        Assert.Contains("\"accountUserId\": 42", json);
        Assert.Contains("\"targetLayer\": 3", json);
        Assert.Contains("\"mode\": \"oneAbove\"", json);
        Assert.Contains("\"burstMs\": 2000", json);
        Assert.Contains("\"settleMs\": 1000", json);
        Assert.Contains("\"rockCapMinutes\": 5", json);
        Assert.Contains("\"autoMineOff\": \"id-off\"", json);
        Assert.DoesNotContain("aimLayer", json);
    }

    [Fact]
    public void A_legacy_file_without_pulses_loads_empty()
    {
        var path = TempFile();
        File.WriteAllText(path, "{\"schemaVersion\":2,\"rings\":[],\"triggers\":[]}");

        Assert.Empty(new TriggerStore(path).Pulses);
    }

    [Fact]
    public void A_null_pulses_list_loads_empty()
    {
        var path = TempFile();
        File.WriteAllText(path, "{\"schemaVersion\":2,\"rings\":[],\"triggers\":[],\"pulses\":null}");

        Assert.Empty(new TriggerStore(path).Pulses);
    }

    [Fact]
    public void Missing_settings_take_the_spec_defaults()
    {
        var path = TempFile();
        File.WriteAllText(path,
            "{\"schemaVersion\":2,\"rings\":[],\"triggers\":[],\"pulses\":[{\"accountUserId\":42,\"ringId\":\"mine8\",\"targetLayer\":2}]}");

        var p = Assert.Single(new TriggerStore(path).Pulses);

        Assert.Equal(PulseMode.Top, p.Mode);
        Assert.Equal(2000, p.BurstMs);
        Assert.Equal(1000, p.SettleMs);
        Assert.Equal(5, p.RockCapMinutes);
        Assert.True(p.Enabled);
        Assert.Null(p.Macros);
    }

    [Fact]
    public void The_camera_turn_macro_survives_a_reload_and_an_older_file_loads_without_it()
    {
        var path = TempFile();
        new TriggerStore(path).UpsertPulse(Pulse() with { Macros = Macros() with { CameraTurnLeft = "id-turn" } });
        Assert.Equal("id-turn", Assert.Single(new TriggerStore(path).Pulses).Macros!.CameraTurnLeft);

        var old = TempFile();
        File.WriteAllText(old,
            "{\"schemaVersion\":2,\"rings\":[],\"triggers\":[],\"pulses\":[{\"accountUserId\":42,\"ringId\":\"mine8\",\"targetLayer\":2," +
            "\"macros\":{\"autoMineOff\":\"a\",\"autoMineOn\":\"b\",\"goToTop\":\"c\",\"clear\":[\"1\",\"2\",\"3\",\"4\",\"5\",\"6\",\"7\",\"8\"]}}]}");
        var m = Assert.Single(new TriggerStore(old).Pulses).Macros!;
        Assert.Equal("c", m.GoToTop);
        Assert.Null(m.CameraTurnLeft);
    }

    [Fact]
    public void Without_a_camera_turn_macro_no_key_is_written()
    {
        var path = TempFile();
        new TriggerStore(path).UpsertPulse(Pulse());

        Assert.DoesNotContain("cameraTurnLeft", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpsertPulse_replaces_the_same_accounts_pulse()
    {
        var store = new TriggerStore(TempFile());
        store.UpsertPulse(Pulse(target: 3));
        store.UpsertPulse(Pulse(account: 43, target: 1));
        store.UpsertPulse(Pulse(target: 2));

        Assert.Equal(2, store.Pulses.Count);
        Assert.Equal(2, store.Pulses.Single(p => p.AccountUserId == 42).TargetLayer);
    }

    [Fact]
    public void The_aim_layer_is_the_target_or_the_one_above()
    {
        Assert.Equal(3, Pulse(target: 3).AimLayer);
        Assert.Equal(2, Pulse(target: 3, mode: PulseMode.OneAbove).AimLayer);
    }

    [Fact]
    public void A_pulse_file_loads_with_defaults()
    {
        var path = TempFile();
        File.WriteAllText(path,
            "{\"schema\":1,\"pulses\":[{\"accountUserId\":123456789,\"ringId\":\"mine8\",\"targetLayer\":3,\"mode\":\"oneAbove\"}]}");

        var f = PulseFile.Load(path);

        Assert.Equal(1, f.Schema);
        var p = Assert.Single(f.Pulses);
        Assert.Equal(123456789, p.AccountUserId);
        Assert.Equal(PulseMode.OneAbove, p.Mode);
        Assert.Equal(2000, p.BurstMs);
    }
}
