using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

/// <summary>
/// Usables: charges fired by hotbar-key macros. The ride usable fires just after Auto Mine on, in a ride above
/// the aim layer, the target usable before a pass on the aim layer; each at most once per everyMs,
/// and a refusal skips that fire without stopping the loop. Every unscripted playback finishes on its
/// first poll.
/// </summary>
public class PulseLoopUsableTests
{
    private sealed record Rig(PulseLoop Loop, ScriptedMacros Macros, ScriptedReader Reader, PulseClock Clock, List<string> Log);

    private static PulseConfig With(int? rideEvery = null, int? targetEvery = null, int target = 3) =>
        PulseFixtures.Config(target: target) with
        {
            Usables = new PulseUsables(
                rideEvery is { } r ? new PulseUsable("id-rover", r) : null,
                targetEvery is { } t ? new PulseUsable("id-core", t) : null),
        };

    private static Rig Build(PulseConfig config, Rgb everywhere, RingDefinition? ring = null)
    {
        var macros = new ScriptedMacros();
        var reader = new ScriptedReader { Next = ScriptedReader.All(everywhere), Frame = PulseFixtures.Calm() };
        var clock = new PulseClock();
        var log = new List<string>();
        var loop = new PulseLoop(config, new[] { ring ?? PulseFixtures.Ring() }, PulseFixtures.Spots(), reader, macros, clock, log.Add);
        return new Rig(loop, macros, reader, clock, log);
    }

    private static Task Tick(Rig r, bool front = true, Func<bool>? inFront = null) =>
        r.Loop.TickAsync(front, 7, CancellationToken.None, inFront);

    /// <summary>Auto Mine on, ride 2 s, Auto Mine off, settle 1 s, read (t = 3 s): the first calm read.</summary>
    private static async Task FirstRead(Rig r, Func<bool>? lastInFront = null)
    {
        await Tick(r);
        await Tick(r);
        r.Clock.Advance(2000);
        await Tick(r);
        await Tick(r);
        r.Clock.Advance(1000);
        await Tick(r, inFront: lastInFront);
    }

    /// <summary>From an Auto Mine on just started: it ends, the ride, Auto Mine off, the settle, the read.</summary>
    private static async Task Cycle(Rig r)
    {
        await Tick(r);
        r.Clock.Advance(2000);
        await Tick(r);
        await Tick(r);
        r.Clock.Advance(1000);
        await Tick(r);
    }

    private static int Count(Rig r, string id) => r.Macros.RunIds.Count(x => x == id);

    [Fact]
    public async Task The_ride_usable_fires_after_Auto_Mine_on_and_before_Auto_Mine_off()
    {
        var rig = Build(With(rideEvery: 5000), PulseFixtures.Navy);

        await FirstRead(rig);             // t = 3 s: the ride begins with Auto Mine on, no usable yet
        Assert.Equal(new[] { "id-on", "id-off", "id-on" }, rig.Macros.RunIds);

        await Tick(rig);                  // Auto Mine on finished: the bomb drops while it runs
        Assert.Equal(new[] { "id-on", "id-off", "id-on", "id-rover" }, rig.Macros.RunIds);
        Assert.Equal(new[] { "42" }, rig.Macros.Runs[3].Targets);

        await Tick(rig);
        Assert.Contains("fired usable ride (macro id-rover)", rig.Log);
        Assert.Equal(PulseState.Riding, rig.Loop.State);

        rig.Clock.Advance(2000);
        await Tick(rig);                  // the burst is over: Auto Mine off
        Assert.Equal(new[] { "id-on", "id-off", "id-on", "id-rover", "id-off" }, rig.Macros.RunIds);
    }

    [Fact]
    public async Task The_burst_still_ends_on_time_when_the_ride_usable_fires()
    {
        var rig = Build(With(rideEvery: 5000), PulseFixtures.Navy);
        await FirstRead(rig);
        await Tick(rig);                  // Auto Mine on finished at t = 3 s (burst ends t = 5 s); rover begins
        await Tick(rig);

        rig.Clock.Advance(1999);
        await Tick(rig);
        Assert.Equal(PulseState.Riding, rig.Loop.State);
        Assert.Equal("id-rover", rig.Macros.RunIds.Last());

        rig.Clock.Advance(1);
        await Tick(rig);
        Assert.Equal("id-off", rig.Macros.RunIds.Last());
        Assert.Equal(1, Count(rig, "id-rover"));   // once per burst
    }

    [Fact]
    public async Task The_ride_usable_is_skipped_when_the_burst_has_no_room_for_it()
    {
        var config = With(rideEvery: 5000) with { BurstMs = PulseLoop.RideUsableRoomMs - 100 };
        var rig = Build(config, PulseFixtures.Navy);

        await FirstRead(rig);
        await Tick(rig);
        rig.Clock.Advance(config.BurstMs);
        await Tick(rig);

        Assert.Equal(0, Count(rig, "id-rover"));
        Assert.Equal("id-off", rig.Macros.RunIds.Last());
    }

    [Fact]
    public async Task The_ride_usable_fires_at_most_once_per_everyMs()
    {
        var rig = Build(With(rideEvery: 5000), PulseFixtures.Navy);
        await FirstRead(rig);             // t = 3 s
        await Tick(rig);                  // fired
        await Tick(rig);

        await Cycle(rig);                 // burst ends, off, settle, read (t = 6 s), ride begins: 3 s since, not yet
        await Tick(rig);
        Assert.Equal(1, Count(rig, "id-rover"));
        Assert.Equal("id-on", rig.Macros.RunIds.Last());

        await Cycle(rig);                 // t = 9 s: 6 s since, fires again once Auto Mine is on
        await Tick(rig);
        Assert.Equal(2, Count(rig, "id-rover"));
        Assert.Equal("id-rover", rig.Macros.RunIds.Last());
    }

    [Fact]
    public async Task The_ride_usable_does_not_fire_at_the_target_or_before_the_first_read()
    {
        var rig = Build(With(rideEvery: 1000), PulseFixtures.Grey);

        await FirstRead(rig);             // the first ride has no layer read yet; the read lands on the target
        await Tick(rig);

        Assert.Equal(0, Count(rig, "id-rover"));
        Assert.Equal(PulseState.Clearing, rig.Loop.State);
    }

    [Fact]
    public async Task The_ride_usable_does_not_fire_on_a_burst_after_a_read_with_no_layer()
    {
        // Live 2026-09-30: "no layer on a calm frame ... riding a burst" fired the Rover because the
        // last layer read was above the target, with black ore all round the character.
        var rig = Build(With(rideEvery: 1000), PulseFixtures.Navy);
        await FirstRead(rig);             // above the target: fired
        await Tick(rig);

        rig.Reader.Next = ScriptedReader.All(PulseFixtures.Orange);
        await Cycle(rig);                 // no layer: a burst, and no usable

        Assert.Contains(rig.Log, l => l.StartsWith("no layer on a calm frame"));
        Assert.Equal(PulseState.Bursting, rig.Loop.State);
        Assert.Equal(1, Count(rig, "id-rover"));
        Assert.Equal("id-on", rig.Macros.RunIds.Last());
    }

    [Fact]
    public async Task The_ride_usable_does_not_fire_on_a_burst_after_a_colour_share_read_with_no_layer()
    {
        var rig = Build(With(rideEvery: 1000), PulseFixtures.Grey, PulseFixtures.RingWithFinder());
        rig.Reader.Frame = new FramePixels(800, 599, Frames.Solid(800, 599, PulseFixtures.Navy));
        await FirstRead(rig);             // above the target: fired
        await Tick(rig);

        rig.Reader.Frame = new FramePixels(800, 599, Frames.Solid(800, 599, PulseFixtures.Orange));
        await Cycle(rig);

        Assert.Contains(rig.Log, l => l.StartsWith("no layer on a calm frame"));
        Assert.Equal(1, Count(rig, "id-rover"));
        Assert.Equal("id-on", rig.Macros.RunIds.Last());
    }

    [Fact]
    public async Task Once_the_target_was_read_the_ride_usable_stays_off_until_Go_to_Top()
    {
        var rig = Build(With(rideEvery: 1000, target: 2), PulseFixtures.Black);
        await FirstRead(rig);             // on the target: clearing

        rig.Reader.Next = ScriptedReader.All(PulseFixtures.Navy);   // a read above it (a misread, a drop back)
        for (var i = 0; i < 60 && !rig.Log.Any(l => l.Contains("is above the target")); i++)
        {
            await Tick(rig);
            rig.Clock.Advance(500);
        }
        Assert.Contains(rig.Log, l => l.Contains("is above the target"));
        await Cycle(rig);                 // and another ride above it
        Assert.Equal(0, Count(rig, "id-rover"));

        rig.Reader.Next = ScriptedReader.All(PulseFixtures.Grey);   // past the target: Go to Top
        for (var i = 0; i < 60 && !rig.Log.Any(l => l.Contains("going to top")); i++)
        {
            await Tick(rig);
            rig.Clock.Advance(500);
        }
        Assert.Equal(0, Count(rig, "id-rover"));

        rig.Reader.Next = ScriptedReader.All(PulseFixtures.Navy);   // from the top, above the target again
        for (var i = 0; i < 60 && Count(rig, "id-rover") == 0; i++)
        {
            await Tick(rig);
            rig.Clock.Advance(500);
        }
        Assert.Equal(1, Count(rig, "id-rover"));
    }

    [Fact]
    public async Task The_target_usable_fires_before_the_pass_and_settles_before_reading_again()
    {
        var rig = Build(With(targetEvery: 20000), PulseFixtures.Grey);

        await FirstRead(rig);
        Assert.Equal(new[] { "id-on", "id-off", "id-core" }, rig.Macros.RunIds);
        Assert.Equal(PulseState.Charging, rig.Loop.State);

        await Tick(rig);                  // it finished: settle
        Assert.Contains("fired usable target (macro id-core)", rig.Log);
        rig.Clock.Advance(999);
        await Tick(rig);
        Assert.Equal(1, rig.Reader.Reads);

        rig.Clock.Advance(1);
        await Tick(rig);                  // read again (the character may have dropped), then clear

        Assert.Equal(2, rig.Reader.Reads);
        Assert.Equal(PulseState.Clearing, rig.Loop.State);
        Assert.StartsWith("id-clear-", rig.Macros.RunIds.Last());
        Assert.Equal(1, Count(rig, "id-core"));
    }

    [Fact]
    public async Task The_target_usable_fires_at_most_once_per_everyMs()
    {
        var rig = Build(With(targetEvery: 20000), PulseFixtures.Grey);
        var fired = new List<DateTimeOffset>();
        var seen = 0;

        for (var i = 0; i < 400 && rig.Clock.Now < DateTimeOffset.UnixEpoch.AddSeconds(45); i++)
        {
            await Tick(rig);
            if (Count(rig, "id-core") > seen)
            {
                seen = Count(rig, "id-core");
                fired.Add(rig.Clock.Now);
            }
            rig.Clock.Advance(250);
        }

        Assert.NotEqual(PulseState.Stopped, rig.Loop.State);
        Assert.True(fired.Count is >= 2 and <= 3, $"fired {fired.Count} times");
        Assert.True(rig.Reader.Reads > fired.Count * 2, "passes ran between the fires");
        for (var i = 1; i < fired.Count; i++)
            Assert.True((fired[i] - fired[i - 1]).TotalMilliseconds >= 20000);
    }

    [Fact]
    public async Task The_target_usable_fires_on_the_aim_layer_in_one_above_mode()
    {
        var config = PulseFixtures.Config(target: 3, mode: PulseMode.OneAbove) with
        {
            Usables = new PulseUsables(new PulseUsable("id-rover", 1000), new PulseUsable("id-core", 20000)),
        };
        var rig = Build(config, PulseFixtures.Black);   // layer 2: the layer one above clears on

        await FirstRead(rig);

        Assert.Equal(new[] { "id-on", "id-off", "id-core" }, rig.Macros.RunIds);
    }

    [Fact]
    public async Task The_target_usable_fires_before_a_ClearAt_pass_too()
    {
        var rig = Build(With(targetEvery: 20000), PulseFixtures.Grey, PulseFixtures.RingWithFinder());

        await FirstRead(rig);
        Assert.Equal(new[] { "id-on", "id-off", "id-core" }, rig.Macros.RunIds);
        await Tick(rig);
        rig.Clock.Advance(1000);
        await Tick(rig);

        Assert.Equal(new[] { "id-on", "id-off", "id-core", ScriptedMacros.ClearAtId }, rig.Macros.RunIds);
    }

    [Fact]
    public async Task Without_usables_nothing_changes()
    {
        var plain = Build(PulseFixtures.Config(), PulseFixtures.Navy);
        var empty = Build(PulseFixtures.Config() with { Usables = new PulseUsables() }, PulseFixtures.Navy);

        foreach (var rig in new[] { plain, empty })
        {
            await FirstRead(rig);
            await Cycle(rig);
        }

        Assert.Equal(new[] { "id-on", "id-off", "id-on", "id-off", "id-on" }, plain.Macros.RunIds);
        Assert.Equal(plain.Macros.RunIds, empty.Macros.RunIds);
    }

    [Fact]
    public async Task Neither_fires_on_the_way_to_the_top()
    {
        var rig = Build(With(rideEvery: 1000, targetEvery: 1000, target: 1), PulseFixtures.Grey);   // grey is past layer 1

        await FirstRead(rig);
        await Tick(rig);

        Assert.Equal(new[] { "id-on", "id-off", "id-top", "id-on" }, rig.Macros.RunIds);
    }

    [Fact]
    public async Task Nothing_is_sent_while_held_or_behind()
    {
        var rig = Build(With(rideEvery: 1000), PulseFixtures.Navy);

        await FirstRead(rig, lastInFront: () => false);   // held (pause, dry run) as the ride begins
        await Tick(rig, front: false);
        rig.Clock.Advance(5000);
        await Tick(rig, front: false);

        Assert.Equal(new[] { "id-on", "id-off" }, rig.Macros.RunIds);
    }

    [Theory]
    [InlineData("busy")]
    [InlineData("ack-timeout")]
    [InlineData("unknown-macro")]
    public async Task A_refused_ride_usable_is_skipped_and_the_ride_goes_on(string reason)
    {
        var rig = Build(With(rideEvery: 5000), PulseFixtures.Navy);

        await FirstRead(rig);             // t = 3 s: Auto Mine on begun
        rig.Macros.RunReplies.Enqueue(ScriptedMacros.Refusal(reason));
        await Tick(rig);                  // Auto Mine on finished: the ride usable is refused
        await Tick(rig);

        Assert.Equal(PulseState.Riding, rig.Loop.State);
        Assert.Equal("id-rover", rig.Macros.RunIds.Last());
        Assert.Contains(rig.Log, l => l.StartsWith("skipped usable ride (macro id-rover)") && l.Contains(reason));
        Assert.DoesNotContain(rig.Log, l => l.Contains("trying again"));

        rig.Clock.Advance(2000);
        await Tick(rig);                  // the burst ends on time all the same
        Assert.Equal("id-off", rig.Macros.RunIds.Last());
        Assert.Equal(1, Count(rig, "id-rover"));
    }

    [Fact]
    public async Task A_ride_usable_that_fails_in_Ur_Task_is_skipped_too()
    {
        var rig = Build(With(rideEvery: 5000), PulseFixtures.Navy);
        rig.Macros.Script("id-rover", ScriptedMacros.CheckFailed);

        await FirstRead(rig);
        await Tick(rig);
        await Tick(rig);

        Assert.Equal(PulseState.Riding, rig.Loop.State);
        Assert.Contains(rig.Log, l => l.StartsWith("skipped usable ride (macro id-rover)"));
        rig.Clock.Advance(2000);
        await Tick(rig);
        Assert.Equal("id-off", rig.Macros.RunIds.Last());
    }

    [Fact]
    public async Task A_refused_target_usable_reads_again_at_once_and_clears()
    {
        var rig = Build(With(targetEvery: 20000), PulseFixtures.Grey);

        await Tick(rig);
        await Tick(rig);
        rig.Clock.Advance(2000);
        await Tick(rig);
        await Tick(rig);
        rig.Clock.Advance(1000);
        rig.Macros.RunReplies.Enqueue(ScriptedMacros.Refusal("busy"));
        await Tick(rig);

        Assert.Equal(PulseState.Clearing, rig.Loop.State);
        Assert.Equal(2, rig.Reader.Reads);
        Assert.StartsWith("id-clear-", rig.Macros.RunIds.Last());
        Assert.Contains(rig.Log, l => l.StartsWith("skipped usable target (macro id-core)"));
        Assert.Equal(1, Count(rig, "id-core"));   // not tried again before everyMs
    }

    [Fact]
    public async Task Started_line_names_the_usables()
    {
        var rig = Build(With(rideEvery: 3000, targetEvery: 20000), PulseFixtures.Navy);

        await Tick(rig);

        Assert.Contains(rig.Log, l => l.StartsWith("started") && l.EndsWith("; usables: ride every 3 s, target every 20 s"));
    }
}
