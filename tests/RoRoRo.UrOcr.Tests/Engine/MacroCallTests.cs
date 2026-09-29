using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

public class MacroCallTests
{
    private sealed record Rig(MacroCall Call, ScriptedMacros Macros, PulseClock Clock, List<string> Log);

    private static Rig Build()
    {
        var macros = new ScriptedMacros();
        var clock = new PulseClock();
        var log = new List<string>();
        var call = new MacroCall(macros, "42", clock, log.Add);
        call.Begin("id-on", "Auto Mine on (checked)");
        return new Rig(call, macros, clock, log);
    }

    private static Task<CallResult> Step(Rig r, bool front = true) => r.Call.StepAsync(front, CancellationToken.None);

    private static readonly ClearAtClient Size = new(800, 599);
    private static readonly ClearAtOutline Outline = new(50, 50, 60, 225);

    private static Rig BuildClearAt(int points = 3)
    {
        var macros = new ScriptedMacros();
        var clock = new PulseClock();
        var log = new List<string>();
        var call = new MacroCall(macros, "42", clock, log.Add);
        call.BeginClearAt(Size,
            Enumerable.Range(1, points).Select(n => new ClearAtPoint(100 + n, 200, $"stone {n}")).ToList(), Outline);
        return new Rig(call, macros, clock, log);
    }

    [Fact]
    public async Task A_ClearAt_starts_on_the_account_and_is_followed_through_GetPlayback()
    {
        var rig = BuildClearAt();

        Assert.Equal(CallStatus.Waiting, (await Step(rig)).Status);
        var req = Assert.Single(rig.Macros.ClearAts);
        Assert.Equal("ClearAt", req.Method);
        Assert.Equal("42", req.Target);
        Assert.Equal(Size, req.Client);
        Assert.Equal(Outline, req.Outline);
        Assert.Equal(3, req.Points.Count);
        Assert.Null(req.MaxMsPerPoint);

        var end = await Step(rig);
        Assert.Equal(CallStatus.Done, end.Status);
        Assert.Equal("ClearAt (3 points)", end.Label);
        Assert.Equal(new[] { "pb1" }, rig.Macros.Polls);
        Assert.False(rig.Call.Active);
    }

    [Fact]
    public async Task A_ClearAt_with_a_guard_sends_it_on_the_wire()
    {
        var macros = new ScriptedMacros();
        var clock = new PulseClock();
        var log = new List<string>();
        var call = new MacroCall(macros, "42", clock, log.Add);
        var guard = new ClearAtGuard(55, 289, 3, 3, new Rgb(255, 19, 90), 30);
        call.BeginClearAt(Size, new[] { new ClearAtPoint(101, 200, "stone 1") }, Outline, guard);

        await call.StepAsync(true, CancellationToken.None);

        Assert.Equal(guard, Assert.Single(macros.ClearAts).Guard);
    }

    [Fact]
    public async Task A_ClearAt_with_no_guard_sends_none()
    {
        var rig = BuildClearAt(points: 1);

        await Step(rig);

        Assert.Null(Assert.Single(rig.Macros.ClearAts).Guard);
    }

    [Fact]
    public async Task A_ClearAt_that_skipped_every_point_is_Skipped()
    {
        var rig = BuildClearAt();
        rig.Macros.Script(ScriptedMacros.ClearAtId, ScriptedMacros.Skipped);

        await Step(rig);

        Assert.Equal(CallStatus.Skipped, (await Step(rig)).Status);
    }

    [Fact]
    public async Task A_ClearAt_waits_until_the_account_is_in_front_right_before_it_starts()
    {
        var rig = BuildClearAt(points: 1);

        Assert.Equal(CallStatus.Waiting, (await rig.Call.StepAsync(true, CancellationToken.None, () => false)).Status);
        Assert.Empty(rig.Macros.ClearAts);
        Assert.Equal("ClearAt (1 point)", rig.Call.Label);

        await rig.Call.StepAsync(true, CancellationToken.None, () => true);
        Assert.Single(rig.Macros.ClearAts);
    }

    [Fact]
    public async Task An_Ur_Task_without_ClearAt_stops_with_its_reason()
    {
        var rig = BuildClearAt();
        rig.Macros.ClearAtReplies.Enqueue(new RunMacroResponse(false, null, false, "refused", "Unknown method 'ClearAt'."));

        var end = await Step(rig);

        Assert.Equal(CallStatus.Stop, end.Status);
        Assert.Contains("ClearAt (3 points)", end.Detail);
        Assert.Contains("Unknown method 'ClearAt'", end.Detail);
        Assert.False(rig.Call.Active);
    }

    [Fact]
    public async Task A_busy_ClearAt_tries_again_after_a_second_with_the_same_points()
    {
        var rig = BuildClearAt();
        rig.Macros.ClearAtReplies.Enqueue(ScriptedMacros.Refusal("busy"));

        Assert.Equal(CallStatus.Waiting, (await Step(rig)).Status);
        Assert.Equal(CallStatus.Waiting, (await Step(rig)).Status);
        Assert.Single(rig.Macros.ClearAts);

        rig.Clock.Advance(MacroCall.RetryMs);
        Assert.Equal(CallStatus.Waiting, (await Step(rig)).Status);
        Assert.Equal(2, rig.Macros.ClearAts.Count);
        Assert.Same(rig.Macros.ClearAts[0], rig.Macros.ClearAts[1]);
        Assert.Equal(CallStatus.Done, (await Step(rig)).Status);
    }

    [Fact]
    public async Task Starts_on_the_account_then_reports_the_end()
    {
        var rig = Build();

        Assert.Equal(CallStatus.Waiting, (await Step(rig)).Status);
        var run = Assert.Single(rig.Macros.Runs);
        Assert.Equal("id-on", run.MacroId);
        Assert.Equal(new[] { "42" }, run.Targets);
        Assert.Equal(0, run.Delay);

        var end = await Step(rig);
        Assert.Equal(CallStatus.Done, end.Status);
        Assert.Equal("Auto Mine on (checked)", end.Label);
        Assert.False(rig.Call.Active);
    }

    [Fact]
    public async Task Does_not_start_while_the_account_is_not_in_front()
    {
        var rig = Build();

        Assert.Equal(CallStatus.Waiting, (await Step(rig, front: false)).Status);
        Assert.Empty(rig.Macros.Runs);
        Assert.True(rig.Call.Active);
    }

    [Fact]
    public async Task Does_not_start_when_the_account_left_the_front_during_the_tick()
    {
        var rig = Build();

        Assert.Equal(CallStatus.Waiting, (await rig.Call.StepAsync(true, CancellationToken.None, () => false)).Status);
        Assert.Empty(rig.Macros.Runs);
        Assert.True(rig.Call.Active);

        Assert.Equal(CallStatus.Waiting, (await rig.Call.StepAsync(true, CancellationToken.None, () => true)).Status);
        Assert.Single(rig.Macros.Runs);
    }

    [Fact]
    public async Task Follows_a_started_playback_in_the_background()
    {
        var rig = Build();
        await Step(rig);

        var end = await Step(rig, front: false);

        Assert.Equal(CallStatus.Done, end.Status);
        Assert.Single(rig.Macros.Polls);
    }

    [Fact]
    public async Task Running_keeps_waiting()
    {
        var rig = Build();
        rig.Macros.Script("id-on", ScriptedMacros.Running, ScriptedMacros.Running, ScriptedMacros.Finished);
        await Step(rig);

        Assert.Equal(CallStatus.Waiting, (await Step(rig)).Status);
        Assert.Equal(CallStatus.Waiting, (await Step(rig)).Status);
        Assert.Equal(CallStatus.Done, (await Step(rig)).Status);
        Assert.Single(rig.Macros.Runs);
    }

    [Fact]
    public async Task Busy_waits_a_second_then_tries_again()
    {
        var rig = Build();
        rig.Macros.RunReplies.Enqueue(ScriptedMacros.Refusal("busy"));

        Assert.Equal(CallStatus.Waiting, (await Step(rig)).Status);
        Assert.Equal(CallStatus.Waiting, (await Step(rig)).Status);
        Assert.Single(rig.Macros.Runs);

        rig.Clock.Advance(MacroCall.RetryMs);
        Assert.Equal(CallStatus.Waiting, (await Step(rig)).Status);
        Assert.Equal(2, rig.Macros.Runs.Count);
        Assert.Equal(CallStatus.Done, (await Step(rig)).Status);
        Assert.Single(rig.Log, l => l.Contains("busy"));
    }

    [Theory]
    [InlineData("ack-timeout")]
    [InlineData("no-targets-resolved")]
    public async Task Other_passing_refusals_also_retry(string reason)
    {
        var rig = Build();
        rig.Macros.RunReplies.Enqueue(ScriptedMacros.Refusal(reason));

        await Step(rig);
        rig.Clock.Advance(MacroCall.RetryMs);
        await Step(rig);

        Assert.Equal(2, rig.Macros.Runs.Count);
        Assert.True(rig.Call.Active);
    }

    [Fact]
    public async Task Missing_Ur_Task_stops()
    {
        var rig = Build();
        rig.Macros.RunReplies.Enqueue(ScriptedMacros.Refusal("ur-task-not-running"));

        var end = await Step(rig);

        Assert.Equal(CallStatus.Stop, end.Status);
        Assert.Contains("Ur Task is not running", end.Detail);
        Assert.False(rig.Call.Active);
    }

    [Fact]
    public async Task An_unknown_macro_stops()
    {
        var rig = Build();
        rig.Macros.RunReplies.Enqueue(ScriptedMacros.Refusal("unknown-macro"));

        var end = await Step(rig);

        Assert.Equal(CallStatus.Stop, end.Status);
        Assert.Contains("unknown-macro", end.Detail);
    }

    [Fact]
    public async Task A_skip_is_reported()
    {
        var rig = Build();
        rig.Macros.Script("id-on", ScriptedMacros.Skipped);
        await Step(rig);

        var end = await Step(rig);

        Assert.Equal(CallStatus.Skipped, end.Status);
        Assert.Equal(ScriptedMacros.Skipped.Detail, end.Detail);
    }

    [Fact]
    public async Task A_failed_check_is_reported()
    {
        var rig = Build();
        rig.Macros.Script("id-on", ScriptedMacros.CheckFailed);
        await Step(rig);

        var end = await Step(rig);

        Assert.Equal(CallStatus.CheckFailed, end.Status);
        Assert.Equal(ScriptedMacros.CheckFailed.Detail, end.Detail);
    }

    [Fact]
    public async Task A_lost_playback_is_reported()
    {
        var rig = Build();
        rig.Macros.Script("id-on", ScriptedMacros.Unknown);
        await Step(rig);

        var end = await Step(rig);

        Assert.Equal(CallStatus.Lost, end.Status);
        Assert.Contains("no longer knows", end.Detail);
    }

    [Fact]
    public async Task Stopped_in_Ur_Task_stops()
    {
        var rig = Build();
        rig.Macros.Script("id-on", ScriptedMacros.Stopped);
        await Step(rig);

        var end = await Step(rig);

        Assert.Equal(CallStatus.Stop, end.Status);
        Assert.Contains("stopped in Ur Task", end.Detail);
    }

    [Fact]
    public async Task Aborted_after_the_account_left_the_front_runs_again()
    {
        var rig = Build();
        rig.Macros.Script("id-on", ScriptedMacros.Aborted, ScriptedMacros.Finished);
        await Step(rig);

        Assert.Equal(CallStatus.Waiting, (await Step(rig, front: false)).Status);
        Assert.Single(rig.Macros.Runs);

        rig.Clock.Advance(MacroCall.RetryMs);   // the rerun is paced, same as a busy retry
        Assert.Equal(CallStatus.Waiting, (await Step(rig)).Status);
        Assert.Equal(2, rig.Macros.Runs.Count);
        Assert.Equal(CallStatus.Done, (await Step(rig)).Status);
        Assert.Contains(rig.Log, l => l.Contains("interrupted"));
    }

    [Fact]
    public async Task Aborted_while_in_front_stops()
    {
        var rig = Build();
        rig.Macros.Script("id-on", ScriptedMacros.Aborted);
        await Step(rig);

        var end = await Step(rig);

        Assert.Equal(CallStatus.Stop, end.Status);
        Assert.Contains("Esc", end.Detail);
        Assert.Single(rig.Macros.Runs);
    }

    [Fact]
    public async Task Interrupted_too_often_stops()
    {
        var rig = Build();
        rig.Macros.Script("id-on", ScriptedMacros.RefusedFocus);

        CallResult end = new(CallStatus.Waiting, "");
        for (var i = 0; i <= MacroCall.MaxInterruptions; i++)
        {
            if (i > 0) rig.Clock.Advance(MacroCall.RetryMs);   // each rerun is paced; let it come due
            await Step(rig);
            end = await Step(rig);
        }

        Assert.Equal(CallStatus.Stop, end.Status);
        // MaxInterruptions (5) reruns plus the original run is 6 attempts in a row.
        Assert.Contains($"interrupted {MacroCall.MaxInterruptions + 1} times", end.Detail);
        Assert.Equal(MacroCall.MaxInterruptions + 1, rig.Macros.Runs.Count);
    }

    [Fact]
    public async Task An_interruption_rerun_is_paced_like_a_busy_retry()
    {
        var rig = Build();
        rig.Macros.Script("id-on", ScriptedMacros.RefusedFocus, ScriptedMacros.Finished);
        await Step(rig);

        Assert.Equal(CallStatus.Waiting, (await Step(rig)).Status);   // refused: logged, not yet due
        Assert.Single(rig.Macros.Runs);                               // does not rerun on the very next tick

        Assert.Equal(CallStatus.Waiting, (await Step(rig)).Status);   // still not due
        Assert.Single(rig.Macros.Runs);

        rig.Clock.Advance(MacroCall.RetryMs);
        Assert.Equal(CallStatus.Waiting, (await Step(rig)).Status);   // due now: reruns
        Assert.Equal(2, rig.Macros.Runs.Count);
    }

    [Fact]
    public async Task Ur_Task_gone_while_waiting_stops()
    {
        var rig = Build();
        rig.Macros.Script("id-on", ScriptedMacros.Gone);
        await Step(rig);

        Assert.Equal(CallStatus.Stop, (await Step(rig)).Status);
    }

    [Fact]
    public async Task An_Ur_Task_without_GetPlayback_stops()
    {
        var rig = Build();
        rig.Macros.Script("id-on", new GetPlaybackResponse(false, null, "refused", "Unknown method 'GetPlayback'.", null));
        await Step(rig);

        var end = await Step(rig);

        Assert.Equal(CallStatus.Stop, end.Status);
        Assert.Contains("Unknown method 'GetPlayback'", end.Detail);
    }

    [Fact]
    public async Task A_poll_that_timed_out_polls_again()
    {
        var rig = Build();
        rig.Macros.Script("id-on", new GetPlaybackResponse(false, null, "ack-timeout", "slow", null), ScriptedMacros.Finished);
        await Step(rig);

        Assert.Equal(CallStatus.Waiting, (await Step(rig)).Status);
        Assert.Equal(CallStatus.Done, (await Step(rig)).Status);
        Assert.Single(rig.Macros.Runs);
    }

    [Fact]
    public async Task A_poll_that_stalls_on_ack_timeout_logs_once()
    {
        var rig = Build();
        var stall = new GetPlaybackResponse(false, null, "ack-timeout", "slow", null);
        rig.Macros.Script("id-on", stall, stall, ScriptedMacros.Finished);
        await Step(rig);

        Assert.Equal(CallStatus.Waiting, (await Step(rig)).Status);   // first stall: logs
        Assert.Equal(CallStatus.Waiting, (await Step(rig)).Status);   // still stalled: no second log line
        Assert.Equal(CallStatus.Done, (await Step(rig)).Status);      // recovers

        Assert.Single(rig.Log, l => l.Contains("ack-timeout"));
    }

    [Fact]
    public async Task A_later_stall_after_a_good_answer_logs_again()
    {
        var rig = Build();
        var stall = new GetPlaybackResponse(false, null, "ack-timeout", "slow", null);
        rig.Macros.Script("id-on", stall, ScriptedMacros.Running, stall, ScriptedMacros.Finished);
        await Step(rig);

        await Step(rig);   // stalls: logs
        await Step(rig);   // a good "running" answer clears the stall flag
        await Step(rig);   // stalls again: logs again
        await Step(rig);   // finishes

        Assert.Equal(2, rig.Log.Count(l => l.Contains("ack-timeout")));
    }
}
