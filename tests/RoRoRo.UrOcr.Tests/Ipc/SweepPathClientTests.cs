using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Ipc;

/// <summary>SweepPath against a fake Ur Task on a real in-process pipe. The request is read back as
/// raw JSON so the wire names are pinned; Ur Task's SweepPathMacroTests pins the same shape.</summary>
public class SweepPathClientTests
{
    private static SweepPathRequest Request() => BridgeContract.ForSweepPath("42", new ClearAtClient(800, 599),
        new[] { new SweepPoint(450, 300), new SweepPoint(450, 250), new SweepPoint(400, 250), new SweepPoint(450, 300) },
        50, 400, new ClearAtGuard(55, 289, 3, 3, new Rgb(255, 19, 90), 30));

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

    private sealed class OnlyRun : IMacroRunClient
    {
        public Task<RunMacroResponse> RunAsync(string macroId, IReadOnlyList<string>? targets, CancellationToken ct) =>
            Task.FromResult(new RunMacroResponse(true, "pb", false, null, null));
    }

    [Fact]
    public void The_request_has_the_exact_wire_shape()
    {
        var json = JsonSerializer.Serialize(Request(), BridgeContract.Json);

        Assert.Equal(
            "{\"contractVersion\":\"1.0\",\"method\":\"SweepPath\",\"callerPluginId\":\"626labs.ur-ocr\",\"target\":\"42\"," +
            "\"client\":{\"w\":800,\"h\":599}," +
            "\"path\":[{\"x\":450,\"y\":300},{\"x\":450,\"y\":250},{\"x\":400,\"y\":250},{\"x\":450,\"y\":300}]," +
            "\"step\":50,\"dwellMs\":400," +
            "\"guard\":{\"x\":55,\"y\":289,\"w\":3,\"h\":3,\"expect\":{\"r\":255,\"g\":19,\"b\":90},\"tolerance\":30}}",
            json);
    }

    [Fact]
    public void A_free_path_request_adds_freePath_with_step_0()
    {
        // The ore sweep: points wherever the ore is, so off any lattice. Ur Task's SweepPathMacroTests
        // takes freePath with the step left out or 0; an Ur Task without freePath ignores the field and
        // refuses step 0, which Ur OCR takes as "no free paths" and clears the ore through ClearAt.
        var request = BridgeContract.ForSweepPath("42", new ClearAtClient(800, 599),
            new[] { new SweepPoint(437, 281), new SweepPoint(461, 263), new SweepPoint(353, 322), new SweepPoint(437, 281) },
            0, 400, new ClearAtGuard(55, 289, 3, 3, new Rgb(255, 19, 90), 30), freePath: true);

        Assert.Equal(
            "{\"contractVersion\":\"1.0\",\"method\":\"SweepPath\",\"callerPluginId\":\"626labs.ur-ocr\",\"target\":\"42\"," +
            "\"client\":{\"w\":800,\"h\":599}," +
            "\"path\":[{\"x\":437,\"y\":281},{\"x\":461,\"y\":263},{\"x\":353,\"y\":322},{\"x\":437,\"y\":281}]," +
            "\"step\":0,\"dwellMs\":400," +
            "\"guard\":{\"x\":55,\"y\":289,\"w\":3,\"h\":3,\"expect\":{\"r\":255,\"g\":19,\"b\":90},\"tolerance\":30}," +
            "\"freePath\":true}",
            JsonSerializer.Serialize(request, BridgeContract.Json));
    }

    [Fact]
    public async Task Sends_SweepPath_and_returns_the_playback_id()
    {
        var (resp, req) = await RoundTrip(
            c => ((IMacroRunClient)c).SweepPathAsync(Request(), default),
            "{\"ok\":true,\"playbackId\":\"pb-9\",\"queued\":false}");

        var root = req.RootElement;
        Assert.Equal("SweepPath", root.GetProperty("method").GetString());
        Assert.Equal("42", root.GetProperty("target").GetString());
        Assert.Equal(4, root.GetProperty("path").GetArrayLength());
        Assert.True(resp.Ok);
        Assert.Equal("pb-9", resp.PlaybackId);
    }

    [Fact]
    public async Task A_refusal_comes_back_with_its_reason_and_detail()
    {
        var (resp, _) = await RoundTrip(
            c => c.SweepPathAsync(Request(), default),
            "{\"ok\":false,\"reason\":\"refused\",\"detail\":\"Unknown method 'SweepPath'.\"}");

        Assert.False(resp.Ok);
        Assert.Equal(BridgeReasons.Refused, resp.Reason);
        Assert.Equal("Unknown method 'SweepPath'.", resp.Detail);
    }

    [Fact]
    public async Task No_Ur_Task_is_not_running_and_never_throws()
    {
        var client = new MacroRunClient(_ => Task.FromResult<Stream?>(null));

        var resp = await client.SweepPathAsync(Request(), default);

        Assert.False(resp.Ok);
        Assert.Equal(BridgeReasons.NotRunning, resp.Reason);
    }

    [Fact]
    public async Task A_client_that_cannot_send_SweepPath_refuses()
    {
        var resp = await ((IMacroRunClient)new OnlyRun()).SweepPathAsync(Request(), default);

        Assert.False(resp.Ok);
        Assert.Equal(BridgeReasons.Refused, resp.Reason);
    }
}
