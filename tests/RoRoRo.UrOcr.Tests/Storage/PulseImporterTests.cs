using System.IO;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Storage;

public class PulseImporterTests
{
    internal static IReadOnlyList<UrTaskMacro> Macros(params string[] leaveOut) =>
        RingImporterTests.Macros()
            .Append(new UrTaskMacro("id-off", "Auto Mine off (checked)"))
            .Append(new UrTaskMacro("id-on", "Auto Mine on (checked)"))
            .Concat(MeasuredRing.RingOrder.Select(n => new UrTaskMacro($"id-clear-{n}", $"Clear spot {n}")))
            .Where(m => !leaveOut.Contains(m.Name))
            .ToList();

    internal static PulseConfig Entry(long account = 42, int target = 2) => new(account, "mine8", target);

    private static TriggerStore StoreWithRing()
    {
        var store = new TriggerStore(Path.Combine(Path.GetTempPath(), "urocr-tests", Guid.NewGuid().ToString("N") + ".json"));
        RingImporter.Apply(store, RingImporterTests.Measured(), RingImporterTests.Macros());
        return store;
    }

    private static IReadOnlyList<PulseConfig> Build(TriggerStore store, IReadOnlyList<UrTaskMacro>? macros, params PulseConfig[] entries) =>
        PulseImporter.Build(new PulseFile(1, entries), store.Rings, store.All, macros ?? Macros());

    [Fact]
    public void Builds_one_pulse_per_account_with_the_macros_looked_up()
    {
        var pulses = Build(StoreWithRing(), null, Entry(42, 2), Entry(43, 1));

        Assert.Equal(new long[] { 42, 43 }, pulses.Select(p => p.AccountUserId));
        var m = pulses[0].Macros!;
        Assert.Equal("id-off", m.AutoMineOff);
        Assert.Equal("id-on", m.AutoMineOn);
        Assert.Equal("id-go-to-top", m.GoToTop);
        Assert.Equal(MeasuredRing.RingOrder.Select(n => $"id-clear-{n}"), m.Clear);
    }

    [Fact]
    public void Macros_in_the_file_are_replaced_by_the_ones_looked_up()
    {
        var entry = Entry() with { Macros = new PulseMacros("x", "y", "z", new[] { "a" }) };

        var p = Assert.Single(Build(StoreWithRing(), null, entry));

        Assert.Equal("id-off", p.Macros!.AutoMineOff);
    }

    [Fact]
    public void A_target_past_the_rings_layers_is_refused()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Build(StoreWithRing(), null, Entry(target: 3)));

        Assert.Contains("Account 42", ex.Message);
        Assert.Contains("targetLayer must be 1 to 2", ex.Message);
    }

    [Fact]
    public void A_missing_clear_macro_is_refused_by_name()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Build(StoreWithRing(), Macros("Clear spot W"), Entry()));

        Assert.Contains("Clear spot W", ex.Message);
    }

    [Fact]
    public void The_same_account_twice_is_refused()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Build(StoreWithRing(), null, Entry(42), Entry(42, 1)));

        Assert.Contains("42 is listed twice", ex.Message);
    }

    [Fact]
    public void Without_the_ring_it_says_to_import_the_ring_first()
    {
        var empty = new TriggerStore(Path.Combine(Path.GetTempPath(), "urocr-tests", Guid.NewGuid().ToString("N") + ".json"));

        var ex = Assert.Throws<InvalidDataException>(() => Build(empty, null, Entry()));

        Assert.Contains("import the ring first", ex.Message);
    }

    [Fact]
    public void A_wrong_schema_or_an_empty_file_is_refused()
    {
        var store = StoreWithRing();

        Assert.Contains("schema must be 1",
            Assert.Throws<InvalidDataException>(() => PulseImporter.Build(new PulseFile(2, new[] { Entry() }), store.Rings, store.All, Macros())).Message);
        Assert.Contains("pulses is empty",
            Assert.Throws<InvalidDataException>(() => PulseImporter.Build(new PulseFile(1, Array.Empty<PulseConfig>()), store.Rings, store.All, Macros())).Message);
    }

    [Fact]
    public void Apply_replaces_the_accounts_pulse_on_reimport_and_a_bad_file_writes_nothing()
    {
        var store = StoreWithRing();
        PulseImporter.Apply(store, new PulseFile(1, new[] { Entry(target: 2) }), Macros());
        PulseImporter.Apply(store, new PulseFile(1, new[] { Entry(target: 1) }), Macros());

        Assert.Throws<InvalidDataException>(() =>
            PulseImporter.Apply(store, new PulseFile(1, new[] { Entry(43, 1), Entry(44, 9) }), Macros()));

        var p = Assert.Single(store.Pulses);
        Assert.Equal(1, p.TargetLayer);
    }

    [Fact]
    public void The_camera_turn_macro_is_looked_up_when_it_is_there()
    {
        var macros = Macros().Append(new UrTaskMacro("id-turn", "Camera turn left")).ToList();

        var p = Assert.Single(Build(StoreWithRing(), macros, Entry()));

        Assert.Equal("id-turn", p.Macros!.CameraTurnLeft);
    }

    [Fact]
    public void Without_a_camera_turn_macro_the_pulse_still_imports()
    {
        var p = Assert.Single(Build(StoreWithRing(), null, Entry()));

        Assert.Null(p.Macros!.CameraTurnLeft);
    }

    [Fact]
    public void Two_camera_turn_macros_are_refused_by_name()
    {
        var macros = Macros().Append(new UrTaskMacro("id-turn", "Camera turn left"))
            .Append(new UrTaskMacro("id-turn-2", "Camera turn left")).ToList();

        var ex = Assert.Throws<InvalidDataException>(() => Build(StoreWithRing(), macros, Entry()));

        Assert.Contains("Camera turn left", ex.Message);
    }

    [Fact]
    public void The_sweep_settings_in_the_file_are_imported()
    {
        var file = Path.Combine(Path.GetTempPath(), "urocr-tests", Guid.NewGuid().ToString("N") + ".pulse.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "{\"schema\":1,\"pulses\":[{\"accountUserId\":42,\"ringId\":\"mine8\",\"targetLayer\":2,"
            + "\"sweepDwellMs\":600,\"sweepNearSide\":false,\"sweepBlockPx\":160}]}");
        var store = StoreWithRing();

        var p = Assert.Single(PulseImporter.Build(PulseFile.Load(file), store.Rings, store.All, Macros()));

        Assert.Equal((600, false, (int?)160), (p.SweepDwellMs, p.SweepNearSide, p.SweepBlockPx));
    }

    [Fact]
    public void A_sweep_block_size_out_of_range_is_refused()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Build(StoreWithRing(), null, Entry() with { SweepBlockPx = 300 }));

        Assert.Contains("Account 42", ex.Message);
        Assert.Contains("sweepBlockPx must be 8 to 240", ex.Message);
    }

    [Fact]
    public void The_usables_in_the_file_are_imported_by_id()
    {
        var file = Path.Combine(Path.GetTempPath(), "urocr-tests", Guid.NewGuid().ToString("N") + ".pulse.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "{\"schema\":1,\"pulses\":[{\"accountUserId\":42,\"ringId\":\"mine8\",\"targetLayer\":2,"
            + "\"usables\":{\"ride\":{\"macro\":\"id-rover\",\"everyMs\":3000},\"target\":{\"macro\":\"id-core\",\"everyMs\":20000}}}]}");
        var store = StoreWithRing();
        var macros = Macros().Append(new UrTaskMacro("id-rover", "Usable: Rover (key 5)"))
            .Append(new UrTaskMacro("id-core", "Usable: Core Charge (key 1)")).ToList();

        var p = Assert.Single(PulseImporter.Build(PulseFile.Load(file), store.Rings, store.All, macros));

        Assert.Equal(new PulseUsable("id-rover", 3000), p.Usables!.Ride);
        Assert.Equal(new PulseUsable("id-core", 20000), p.Usables.Target);
    }

    [Fact]
    public void A_usable_macro_Ur_Task_does_not_have_is_refused()
    {
        var entry = Entry() with { Usables = new PulseUsables(Target: new PulseUsable("id-missing", 20000)) };

        var ex = Assert.Throws<InvalidDataException>(() => Build(StoreWithRing(), null, entry));

        Assert.Contains("Account 42", ex.Message);
        Assert.Contains("usables.target.macro id-missing is not one of Ur Task's macros", ex.Message);
    }

    [Fact]
    public void A_usable_every_out_of_range_is_refused_at_import()
    {
        var macros = Macros().Append(new UrTaskMacro("id-rover", "Usable: Rover (key 5)")).ToList();
        var entry = Entry() with { Usables = new PulseUsables(Ride: new PulseUsable("id-rover", 500)) };

        var ex = Assert.Throws<InvalidDataException>(() => Build(StoreWithRing(), macros, entry));

        Assert.Contains("usables.ride.everyMs must be 1000 to 600000", ex.Message);
    }
}
