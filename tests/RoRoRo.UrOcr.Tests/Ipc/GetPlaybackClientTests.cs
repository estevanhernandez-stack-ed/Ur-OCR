using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using RoRoRo.UrOcr.Ipc;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Ipc;

/// <summary>
/// GetPlayback against a fake Ur Task on a real in-process pipe. The request is read back as raw
/// JSON so the wire names are pinned, not just the C# record.
/// </summary>
public class GetPlaybackClientTests
{
    private static async Task<(TResponse Response, JsonDocument Request)> RoundTrip<TResponse>(
        Func<MacroRunClient, Task<TResponse>> call, string replyJson)
    {
        var pipeName = "626labs-ur-ocr-test-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        Task<Stream?> Open(CancellationToken ct)
        {
            var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            return client.ConnectAsync(2000, ct).ContinueWith<Stream?>(
                t => t.IsCompletedSuccessfully ? client : null, TaskContinuationOptions.ExecuteSynchronously);
        }

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            var request = await FrameCodec.ReadFrameAsync(server, default);
            await FrameCodec.WriteFrameAsync(server, Encoding.UTF8.GetBytes(replyJson), default);
            return JsonDocument.Parse(request!);
        });

        var response = await call(new MacroRunClient(Open));
        return (response, await serverTask);
    }

    [Fact]
    public void The_request_has_the_exact_wire_shape()
    {
        var json = JsonSerializer.Serialize(BridgeContract.ForPlayback("abc"), BridgeContract.Json);

        Assert.Equal(
            "{\"contractVersion\":\"1.0\",\"method\":\"GetPlayback\",\"playbackId\":\"abc\",\"callerPluginId\":\"626labs.ur-ocr\"}",
            json);
    }

    [Fact]
    public void A_response_reads_every_field()
    {
        var r = JsonSerializer.Deserialize<GetPlaybackResponse>(
            "{\"ok\":true,\"state\":\"failed\",\"reason\":\"check-failed\",\"detail\":\"step 2 saw red\",\"stepIndex\":2}",
            BridgeContract.Json)!;

        Assert.True(r.Ok);
        Assert.Equal(PlaybackStates.Failed, r.State);
        Assert.Equal(BridgeReasons.CheckFailed, r.Reason);
        Assert.Equal("step 2 saw red", r.Detail);
        Assert.Equal(2, r.StepIndex);
    }

    [Fact]
    public async Task Sends_GetPlayback_and_returns_the_state()
    {
        var (resp, req) = await RoundTrip(
            c => c.GetPlaybackAsync("pb-123", default),
            "{\"ok\":true,\"state\":\"finished\",\"reason\":\"skipped\",\"detail\":\"No outline.\"}");

        var root = req.RootElement;
        Assert.Equal("1.0", root.GetProperty("contractVersion").GetString());
        Assert.Equal("GetPlayback", root.GetProperty("method").GetString());
        Assert.Equal("pb-123", root.GetProperty("playbackId").GetString());
        Assert.Equal("626labs.ur-ocr", root.GetProperty("callerPluginId").GetString());
        Assert.True(resp.Ok);
        Assert.Equal("finished", resp.State);
        Assert.Equal("skipped", resp.Reason);
        Assert.Null(resp.StepIndex);
    }

    [Fact]
    public void A_finished_ClearAt_reads_its_no_outline_points()
    {
        var r = JsonSerializer.Deserialize<GetPlaybackResponse>(
            "{\"ok\":true,\"state\":\"finished\",\"noOutline\":[2,5]}", BridgeContract.Json)!;

        Assert.Equal(new[] { 2, 5 }, r.NoOutline);
    }

    [Fact]
    public void An_older_Ur_Task_without_noOutline_reads_null()
    {
        var r = JsonSerializer.Deserialize<GetPlaybackResponse>("{\"ok\":true,\"state\":\"finished\"}", BridgeContract.Json)!;

        Assert.Null(r.NoOutline);
    }

    [Fact]
    public async Task An_unknown_playback_comes_back_as_a_refusal()
    {
        var (resp, _) = await RoundTrip(
            c => c.GetPlaybackAsync("gone", default),
            "{\"ok\":false,\"reason\":\"unknown-playback\",\"detail\":\"No playback with id 'gone'. Finished playbacks are kept for 10 minutes.\"}");

        Assert.False(resp.Ok);
        Assert.Null(resp.State);
        Assert.Equal(BridgeReasons.UnknownPlayback, resp.Reason);
    }

    [Fact]
    public async Task Through_the_interface_it_reaches_Ur_Task_not_the_default()
    {
        var (resp, _) = await RoundTrip(
            c => ((IMacroRunClient)c).GetPlaybackAsync("pb-1", default),
            "{\"ok\":true,\"state\":\"running\"}");

        Assert.True(resp.Ok);
        Assert.Equal(PlaybackStates.Running, resp.State);
    }

    [Fact]
    public async Task No_Ur_Task_is_not_running_and_never_throws()
    {
        var client = new MacroRunClient(_ => Task.FromResult<Stream?>(null));

        var resp = await client.GetPlaybackAsync("pb-1", default);

        Assert.False(resp.Ok);
        Assert.Equal(BridgeReasons.NotRunning, resp.Reason);
    }

    [Fact]
    public async Task A_cancelled_wait_is_an_ack_timeout()
    {
        var client = new MacroRunClient(_ => Task.FromException<Stream?>(new OperationCanceledException()));

        var resp = await client.GetPlaybackAsync("pb-1", default);

        Assert.False(resp.Ok);
        Assert.Equal(BridgeReasons.AckTimeout, resp.Reason);
    }

    [Fact]
    public async Task RunAsync_still_sends_RunMacro()
    {
        var (resp, req) = await RoundTrip(
            c => c.RunAsync("macro-1", null, default),
            "{\"ok\":true,\"playbackId\":\"pb-9\",\"queued\":false}");

        Assert.Equal("RunMacro", req.RootElement.GetProperty("method").GetString());
        Assert.Equal("macro-1", req.RootElement.GetProperty("macroId").GetString());
        Assert.False(req.RootElement.TryGetProperty("interAltDelayMs", out _));
        Assert.True(resp.Ok);
        Assert.Equal("pb-9", resp.PlaybackId);
    }

    [Fact]
    public async Task RunAsync_can_ask_for_no_inter_alt_delay()
    {
        var (resp, req) = await RoundTrip(
            c => ((IMacroRunClient)c).RunAsync("macro-1", new[] { "42" }, 0, default),
            "{\"ok\":true,\"playbackId\":\"pb-9\",\"queued\":false}");

        var root = req.RootElement;
        Assert.Equal(0, root.GetProperty("interAltDelayMs").GetInt32());
        Assert.Equal("42", root.GetProperty("targets")[0].GetString());
        Assert.True(resp.Ok);
    }
}
