using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

/// <summary>
/// Ride lengths (live 2026-09-30: 8 stop-read cycles of a 2 s ride and 2.5 s of stopping and reading
/// took 39 s to reach black). The first ride from the top is RideFirstMs, a ride after a read 2 or more
/// layers above the aim layer is RideBurstFarMs, and a ride after a read 1 above, or any burst, is
/// BurstMs. The ride usable fires again each everyMs while a long ride lasts. Ring layers: navy (1),
/// black (2), grey (3), aim 3 unless said. Every unscripted playback finishes on its first poll.
/// </summary>
public class PulseLoopRideTests
{
    private sealed record Rig(PulseLoop Loop, ScriptedMacros Macros, ScriptedReader Reader, PulseClock Clock, List<string> Log);

    /// <summary>The spec defaults for the rides: 8 s first, 5 s far, 2 s burst.</summary>
    private static PulseConfig Defaults(int target = 3) => new(42, "mine8", target, Macros: PulseFixtures.Macros());

    private static Rig Build(PulseConfig config, Rgb everywhere)
    {
        var macros = new ScriptedMacros();
        var reader = new ScriptedReader { Next = ScriptedReader.All(everywhere) };
        var clock = new PulseClock();
        var log = new List<string>();
        var loop = new PulseLoop(config, new[] { PulseFixtures.Ring() }, PulseFixtures.Spots(), reader, macros, clock, log.Add);
        return new Rig(loop, macros, reader, clock, log);
    }

    private static Task Tick(Rig r) => r.Loop.TickAsync(true, 7, CancellationToken.None);

    /// <summary>With Auto Mine on just finished (the ride's timer running): how long until Auto Mine
    /// off starts, in 1 ms steps past <paramref name="expect"/> - 1 so an early end shows.</summary>
    private static async Task AssertRideLasts(Rig r, int expect)
    {
        r.Clock.Advance(expect - 1);
        await Tick(r);
        Assert.Equal("id-on", r.Macros.RunIds.Last(id => id is "id-on" or "id-off"));
        r.Clock.Advance(1);
        await Tick(r);
        Assert.Equal("id-off", r.Macros.RunIds.Last());
    }

    /// <summary>From Auto Mine on begun: it ends, the ride of <paramref name="rideMs"/>, Auto Mine off,
    /// the settle, the read.</summary>
    private static async Task Cycle(Rig r, int rideMs)
    {
        await Tick(r);
        r.Clock.Advance(rideMs);
        await Tick(r);
        await Tick(r);
        r.Clock.Advance(1000);
        await Tick(r);
    }

    [Fact]
    public void The_defaults_are_8_s_first_and_5_s_far()
    {
        var p = Defaults();

        Assert.Equal((8000, 5000, 2000), (p.RideFirstMs, p.RideBurstFarMs, p.BurstMs));
    }

    [Fact]
    public async Task The_first_ride_from_the_start_lasts_rideFirstMs()
    {
        var rig = Build(Defaults(), PulseFixtures.Navy);

        await Tick(rig);                  // Auto Mine on begun
        await Tick(rig);                  // it finished: the first ride runs
        await AssertRideLasts(rig, 8000);

        Assert.Contains("rides: 8 s from the top, 5 s from 2 or more layers above the aim, 2 s from 1 above and for a burst", rig.Log);
    }

    [Fact]
    public async Task The_first_ride_after_Go_to_Top_lasts_rideFirstMs()
    {
        var rig = Build(Defaults(target: 2), PulseFixtures.Grey);   // grey is past black: Go to Top
        await Tick(rig);                  // Auto Mine on begun
        await Cycle(rig, 8000);
        Assert.Equal("id-top", rig.Macros.RunIds.Last());

        await Tick(rig);                  // Go to Top finished: riding, Auto Mine on begun
        Assert.Equal("id-on", rig.Macros.RunIds.Last());
        await Tick(rig);                  // Auto Mine on finished
        await AssertRideLasts(rig, 8000);

        Assert.Contains("at the top: riding 8 s before the first read", rig.Log);
    }

    [Fact]
    public async Task A_read_2_layers_above_the_aim_rides_rideBurstFarMs()
    {
        var rig = Build(Defaults(), PulseFixtures.Navy);
        await Tick(rig);                  // Auto Mine on begun
        await Cycle(rig, 8000);           // read navy: 2 above grey

        Assert.Contains("layer navy (8 of 8 spots) is above the target (grey): riding on for 5 s", rig.Log);
        await Tick(rig);
        await AssertRideLasts(rig, 5000);
    }

    [Fact]
    public async Task A_read_1_layer_above_the_aim_rides_burstMs()
    {
        var rig = Build(Defaults(), PulseFixtures.Black);
        await Tick(rig);                  // Auto Mine on begun
        await Cycle(rig, 8000);           // read black: 1 above grey

        Assert.Contains("layer black (8 of 8 spots) is above the target (grey): riding on", rig.Log);
        await Tick(rig);
        await AssertRideLasts(rig, 2000);
    }

    [Fact]
    public async Task A_read_with_no_layer_rides_burstMs()
    {
        var rig = Build(Defaults(), PulseFixtures.Navy);
        await Tick(rig);                  // Auto Mine on begun
        await Cycle(rig, 8000);           // navy: a far ride
        rig.Reader.Next = ScriptedReader.All(PulseFixtures.Orange);
        await Cycle(rig, 5000);           // no layer: a burst

        Assert.Equal(PulseState.Bursting, rig.Loop.State);
        await Tick(rig);
        await AssertRideLasts(rig, 2000);
    }

    [Fact]
    public async Task The_far_ride_follows_the_last_read_not_the_one_before()
    {
        var rig = Build(Defaults(), PulseFixtures.Navy);
        await Tick(rig);                  // Auto Mine on begun
        await Cycle(rig, 8000);           // navy: 5 s
        rig.Reader.Next = ScriptedReader.All(PulseFixtures.Black);
        await Cycle(rig, 5000);           // black: 2 s

        await Tick(rig);
        await AssertRideLasts(rig, 2000);
    }

    /// <summary>The ride usable's start times in a far ride of <paramref name="farMs"/> after a navy read,
    /// relative to the moment Auto Mine on finished, ticking every 250 ms until Auto Mine off.</summary>
    private static async Task<List<double>> RoverTimes(int farMs, int everyMs)
    {
        var config = Defaults() with { RideBurstFarMs = farMs, Usables = new PulseUsables(new PulseUsable("id-rover", everyMs)) };
        var rig = Build(config, PulseFixtures.Navy);
        await Tick(rig);                  // Auto Mine on begun
        await Cycle(rig, 8000);           // the first ride has no read: no rover; read navy, Auto Mine on begun
        Assert.Equal(0, rig.Macros.RunIds.Count(id => id == "id-rover"));

        var start = rig.Clock.Now;        // Auto Mine on finishes on the next tick, at this time
        var times = new List<double>();
        var seen = 0;
        for (var i = 0; i < 200 && rig.Macros.RunIds.Last() != "id-off"; i++)
        {
            await Tick(rig);
            var n = rig.Macros.RunIds.Count(id => id == "id-rover");
            if (n > seen) times.Add((rig.Clock.Now - start).TotalMilliseconds);
            seen = n;
            rig.Clock.Advance(250);
        }
        Assert.Equal("id-off", rig.Macros.RunIds.Last());
        return times;
    }

    [Fact]
    public async Task The_ride_usable_fires_twice_in_a_5_s_ride_at_everyMs_3000()
    {
        // At +0 (right after Auto Mine on) and +3.0 s (2 s left); at +6 s the ride is over.
        Assert.Equal(new[] { 0.0, 3000.0 }, await RoverTimes(5000, 3000));
    }

    [Fact]
    public async Task The_ride_usable_fires_three_times_in_an_8_s_ride_at_everyMs_3000()
    {
        // At +0, +3.0 s and +6.0 s (2 s left each time over the 0.5 s room).
        Assert.Equal(new[] { 0.0, 3000.0, 6000.0 }, await RoverTimes(8000, 3000));
    }

    [Fact]
    public async Task The_ride_usable_is_not_fired_again_with_under_the_room_left()
    {
        // Due again at +3.0 s with 0.4 s of the 3.4 s ride left: under RideUsableRoomMs, skipped.
        Assert.Equal(new[] { 0.0 }, await RoverTimes(3400, 3000));
    }
}
