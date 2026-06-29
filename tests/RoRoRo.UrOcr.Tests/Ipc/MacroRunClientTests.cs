// tests/RoRoRo.UrOcr.Tests/Ipc/MacroRunClientTests.cs
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using RoRoRo.UrOcr.Ipc;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Ipc;

public class MacroRunClientTests
{
    // Happy-path: client sends RunMacro, fake server reads + validates + replies Accepted.
    [Fact]
    public async Task RunAsync_HappyPath_ReturnsAcceptedResponse()
    {
        var macroId = Guid.NewGuid().ToString();
        var pipeName = "626labs-ur-ocr-test-" + Guid.NewGuid().ToString("N");

        // Set up in-process server-side pipe.
        await using var serverPipe = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        // Injected opener: creates a real client-side pipe connected to serverPipe.
        Task<Stream?> OpenPipeAsync(CancellationToken ct)
        {
            var clientPipe = new NamedPipeClientStream(".", pipeName,
                PipeDirection.InOut, PipeOptions.Asynchronous);
            // WaitForConnection happens concurrently below — this connect completes once the server waits.
            return clientPipe.ConnectAsync(2000, ct).ContinueWith<Stream?>(
                t => t.IsCompletedSuccessfully ? clientPipe : null,
                TaskContinuationOptions.ExecuteSynchronously);
        }

        // Fake server: wait for connection, read the request frame, assert fields, write Accepted.
        var serverTask = Task.Run(async () =>
        {
            await serverPipe.WaitForConnectionAsync();

            var reqBytes = await FrameCodec.ReadFrameAsync(serverPipe, default);
            Assert.NotNull(reqBytes);

            var req = JsonSerializer.Deserialize<RunMacroRequest>(reqBytes!, BridgeContract.Json);
            Assert.NotNull(req);
            Assert.Equal("RunMacro", req!.Method);
            Assert.Equal(macroId, req.MacroId);
            Assert.Equal("626labs.ur-ocr", req.CallerPluginId);

            var respBytes = JsonSerializer.SerializeToUtf8Bytes(
                new RunMacroResponse(true, "01OK", false, null, null),
                BridgeContract.Json);
            await FrameCodec.WriteFrameAsync(serverPipe, respBytes, default);
        });

        var client = new MacroRunClient(OpenPipeAsync);
        var resp = await client.RunAsync(macroId, null, default);

        await serverTask; // surface any assertion failures from the background task

        Assert.True(resp.Ok);
        Assert.Equal("01OK", resp.PlaybackId);
    }

    // Not-running: injected opener returns null — RunAsync returns a synthetic refusal, no throw.
    [Fact]
    public async Task RunAsync_NullStream_ReturnsNotRunningRefusal()
    {
        Task<Stream?> NullOpener(CancellationToken _) => Task.FromResult<Stream?>(null);

        var client = new MacroRunClient(NullOpener);
        var ex = await Record.ExceptionAsync(() =>
            client.RunAsync("any-macro-id", null, default));

        Assert.Null(ex); // must NOT throw

        // Run it again to get the actual response value.
        var resp = await client.RunAsync("any-macro-id", null, default);
        Assert.False(resp.Ok);
        Assert.Equal("ur-task-not-running", resp.Reason);
    }
}
