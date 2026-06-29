// Ipc/MacroRunClient.cs
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
namespace RoRoRo.UrOcr.Ipc;

public sealed class MacroRunClient : IMacroRunClient
{
    private const int ConnectTimeoutMs = 2000;
    private readonly Func<CancellationToken, Task<Stream?>> _openPipe;

    /// <summary>Production constructor — opens the real named pipe.</summary>
    public MacroRunClient() : this(DefaultOpenAsync) { }

    /// <summary>Test constructor — inject any stream-opener (e.g. in-process pipe pair).</summary>
    internal MacroRunClient(Func<CancellationToken, Task<Stream?>> openPipe) => _openPipe = openPipe;

    public async Task<RunMacroResponse> RunAsync(string macroId, IReadOnlyList<string>? targets, CancellationToken ct)
    {
        Stream? pipe = null;
        try
        {
            pipe = await _openPipe(ct).ConfigureAwait(false);
            if (pipe is null)
                return new RunMacroResponse(false, null, false, "ur-task-not-running",
                    "Ur Task is not running or refused the connection.");

            var reqBytes = JsonSerializer.SerializeToUtf8Bytes(
                BridgeContract.ForMacro(macroId, targets), BridgeContract.Json);
            await FrameCodec.WriteFrameAsync(pipe, reqBytes, ct).ConfigureAwait(false);

            var respBytes = await FrameCodec.ReadFrameAsync(pipe, ct).ConfigureAwait(false);
            if (respBytes is null)
                return new RunMacroResponse(false, null, false, "refused",
                    "Ur Task closed the connection without a response.");

            return JsonSerializer.Deserialize<RunMacroResponse>(respBytes, BridgeContract.Json)
                   ?? new RunMacroResponse(false, null, false, "refused", "Empty response.");
        }
        catch (Exception ex)
        {
            return new RunMacroResponse(false, null, false, "ur-task-not-running", ex.Message);
        }
        finally
        {
            if (pipe is not null)
                await pipe.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task<Stream?> DefaultOpenAsync(CancellationToken ct)
    {
        var pipe = new NamedPipeClientStream(".", BridgeContract.PipeName,
            PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(ConnectTimeoutMs, ct).ConfigureAwait(false);
            return pipe;
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            return null;
        }
    }
}
