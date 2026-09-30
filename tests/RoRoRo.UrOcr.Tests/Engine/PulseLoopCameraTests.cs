using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

/// <summary>
/// The pulse sets its own camera (owner, 2026-09-30): with a "Camera top-down" macro in Ur Task it
/// runs it at the start and after every Go to Top, before the ride. Live that day a fresh client
/// started zoomed in close and looking level: Auto Mine dug nowhere and every read came back dark.
/// Every unscripted playback finishes on its first poll.
/// </summary>
public class PulseLoopCameraTests
{
    private const string CameraId = "id-camera";

    private sealed record Rig(PulseLoop Loop, ScriptedMacros Macros, ScriptedReader Reader, PulseClock Clock, List<string> Log);

    private static PulseConfig WithCamera(int target = 3) =>
        PulseFixtures.Config(target) with { Macros = PulseFixtures.Macros() with { CameraTopDown = CameraId } };

    private static Rig Build(PulseConfig config)
    {
        var macros = new ScriptedMacros();
        var reader = new ScriptedReader { Next = ScriptedReader.All(PulseFixtures.Grey) };
        var clock = new PulseClock();
        var log = new List<string>();
        var loop = new PulseLoop(config, new[] { PulseFixtures.Ring() }, PulseFixtures.Spots(), reader, macros, clock, log.Add);
        return new Rig(loop, macros, reader, clock, log);
    }

    private static Task Tick(Rig r) => r.Loop.TickAsync(true, 7, CancellationToken.None);

    [Fact]
    public async Task The_camera_is_set_before_the_first_ride()
    {
        var rig = Build(WithCamera());
        Assert.Equal(PulseState.SettingCamera, rig.Loop.State);

        await Tick(rig);                     // the camera macro started
        Assert.Equal(new[] { CameraId }, rig.Macros.RunIds);

        await Tick(rig);                     // it finished: the ride starts with Auto Mine on
        Assert.Equal(PulseState.Riding, rig.Loop.State);
        Assert.Equal(new[] { CameraId, "id-on" }, rig.Macros.RunIds);
        Assert.Contains(rig.Log, l => l == "setting the camera (Camera top-down)");
    }

    [Fact]
    public async Task The_camera_is_set_again_after_every_Go_to_Top()
    {
        var rig = Build(WithCamera(target: 1));
        await Until(rig, PulseState.GoingToTop);    // camera, ride, read: past the target
        await Until(rig, PulseState.Riding);        // Go to Top finished: the camera again, then the ride

        var top = rig.Macros.RunIds.IndexOf("id-top");
        Assert.Equal(new[] { CameraId, "id-on" }, rig.Macros.RunIds.Skip(top + 1));
        Assert.Equal(2, rig.Log.Count(l => l == "setting the camera (Camera top-down)"));
    }

    /// <summary>Ticks, a second apart, until the loop is in <paramref name="state"/>.</summary>
    private static async Task Until(Rig r, PulseState state)
    {
        for (var i = 0; i < 30 && r.Loop.State != state; i++)
        {
            await Tick(r);
            if (r.Loop.State != state) r.Clock.Advance(1000);
        }
        Assert.Equal(state, r.Loop.State);
    }

    [Fact]
    public async Task Without_the_camera_macro_the_pulse_rides_straight_away_as_before()
    {
        var rig = Build(PulseFixtures.Config());
        Assert.Equal(PulseState.Riding, rig.Loop.State);

        await Tick(rig);

        Assert.Equal(new[] { "id-on" }, rig.Macros.RunIds);
        Assert.DoesNotContain(rig.Log, l => l.StartsWith("setting the camera"));
    }
}
