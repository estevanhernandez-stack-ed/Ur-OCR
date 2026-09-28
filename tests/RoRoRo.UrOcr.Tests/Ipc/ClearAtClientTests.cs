using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using RoRoRo.UrOcr.Ipc;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Ipc;

/// <summary>
/// ClearAt against a fake Ur Task on a real in-process pipe. The request is read back as raw JSON
/// so the wire names are pinned, not just the C# record.
/// </summary>
public class ClearAtClientTests
{
    private static ClearAtRequest Request() => BridgeContract.ForClearAt("42", new ClearAtClient(800, 599),
        new[] { new ClearAtPoint(412, 288, "ore 1") }, new ClearAtOutline(50, 50, 60, 225));

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
            "{\"contractVersion\":\"1.0\",\"method\":\"ClearAt\",\"callerPluginId\":\"626labs.ur-ocr\",\"target\":\"42\"," +
            "\"client\":{\"w\":800,\"h\":599},\"points\":[{\"x\":412,\"y\":288,\"label\":\"ore 1\"}]," +
            "\"outline\":{\"w\":50,\"h\":50,\"minCount\":60,\"whiteMin\":225},\"maxMsPerPoint\":null}",
            json);
    }

    [Fact]
    public async Task Sends_ClearAt_and_returns_the_playback_id()
    {
        var (resp, req) = await RoundTrip(
            c => ((IMacroRunClient)c).ClearAtAsync(Request(), default),
            "{\"ok\":true,\"playbackId\":\"pb-7\",\"queued\":false}");

        var root = req.RootElement;
        Assert.Equal("ClearAt", root.GetProperty("method").GetString());
        Assert.Equal("42", root.GetProperty("target").GetString());
        Assert.Equal("ore 1", root.GetProperty("points")[0].GetProperty("label").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("maxMsPerPoint").ValueKind);
        Assert.True(resp.Ok);
        Assert.Equal("pb-7", resp.PlaybackId);
    }

    [Fact]
    public async Task A_refusal_comes_back_with_its_reason_and_detail()
    {
        var (resp, _) = await RoundTrip(
            c => c.ClearAtAsync(Request(), default),
            "{\"ok\":false,\"reason\":\"refused\",\"detail\":\"Unknown method 'ClearAt'.\"}");

        Assert.False(resp.Ok);
        Assert.Equal(BridgeReasons.Refused, resp.Reason);
        Assert.Equal("Unknown method 'ClearAt'.", resp.Detail);
    }

    [Fact]
    public async Task No_Ur_Task_is_not_running_and_never_throws()
    {
        var client = new MacroRunClient(_ => Task.FromResult<Stream?>(null));

        var resp = await client.ClearAtAsync(Request(), default);

        Assert.False(resp.Ok);
        Assert.Equal(BridgeReasons.NotRunning, resp.Reason);
    }

    [Fact]
    public async Task A_client_that_cannot_send_ClearAt_refuses()
    {
        var resp = await ((IMacroRunClient)new OnlyRun()).ClearAtAsync(Request(), default);

        Assert.False(resp.Ok);
        Assert.Equal(BridgeReasons.Refused, resp.Reason);
    }
}
