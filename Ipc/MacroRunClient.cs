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

    public Task<RunMacroResponse> RunAsync(string macroId, IReadOnlyList<string>? targets, CancellationToken ct) =>
        RunAsync(macroId, targets, null, ct);

    public Task<RunMacroResponse> RunAsync(string macroId, IReadOnlyList<string>? targets, int? interAltDelayMs, CancellationToken ct) =>
        ExchangeAsync(BridgeContract.ForMacro(macroId, targets, interAltDelayMs),
            (reason, detail) => new RunMacroResponse(false, null, false, reason, detail),
            // The wait was cancelled (e.g. the coordinator's per-tick watchdog) before Ur Task
            // acked. Ur Task acks as soon as it accepts or refuses a run (a running sequence is
            // refused as "busy"), so this means the pipe stalled and the run may or may not have
            // started. Report that honestly, not "not running".
            "Macro request sent; Ur Task did not ack within the tick window (long macro?).",
            ct);

    /// <summary>How a playback RunAsync started is going. Ur Task keeps an ended playback for 10
    /// minutes; after that, or after an Ur Task restart, the answer is "unknown-playback".</summary>
    public Task<GetPlaybackResponse> GetPlaybackAsync(string playbackId, CancellationToken ct) =>
        ExchangeAsync(BridgeContract.ForPlayback(playbackId),
            (reason, detail) => new GetPlaybackResponse(false, null, reason, detail, null),
            "Asked Ur Task how a playback is going; it did not answer within the tick window.",
            ct);

    /// <summary>One ClearAt playback. Ur Task acks with a playback id or a refusal: busy, a bad
    /// point or box, or "Unknown method 'ClearAt'." from an Ur Task without it.</summary>
    public Task<RunMacroResponse> ClearAtAsync(ClearAtRequest request, CancellationToken ct) =>
        ExchangeAsync(request,
            (reason, detail) => new RunMacroResponse(false, null, false, reason, detail),
            "ClearAt sent; Ur Task did not ack within the tick window.",
            ct);

    /// <summary>One SweepPath playback. Ur Task acks with a playback id or a refusal: busy, a bad
    /// path, or "Unknown method 'SweepPath'." from an Ur Task older than 0.12.0.</summary>
    public Task<RunMacroResponse> SweepPathAsync(SweepPathRequest request, CancellationToken ct) =>
        ExchangeAsync(request,
            (reason, detail) => new RunMacroResponse(false, null, false, reason, detail),
            "SweepPath sent; Ur Task did not ack within the tick window.",
            ct);

    /// <summary>One request, one response, one connection. Never throws: a missing Ur Task, a closed
    /// pipe or a cancelled wait each come back as a refusal built by <paramref name="fail"/>.</summary>
    private async Task<TResponse> ExchangeAsync<TRequest, TResponse>(
        TRequest request, Func<string, string, TResponse> fail, string timeoutDetail, CancellationToken ct)
    {
        Stream? pipe = null;
        try
        {
            pipe = await _openPipe(ct).ConfigureAwait(false);
            if (pipe is null)
                return fail(BridgeReasons.NotRunning, "Ur Task is not running or refused the connection.");

            var reqBytes = JsonSerializer.SerializeToUtf8Bytes(request, BridgeContract.Json);
            await FrameCodec.WriteFrameAsync(pipe, reqBytes, ct).ConfigureAwait(false);

            var respBytes = await FrameCodec.ReadFrameAsync(pipe, ct).ConfigureAwait(false);
            if (respBytes is null)
                return fail(BridgeReasons.Refused, "Ur Task closed the connection without a response.");

            return JsonSerializer.Deserialize<TResponse>(respBytes, BridgeContract.Json)
                   ?? fail(BridgeReasons.Refused, "Empty response.");
        }
        catch (OperationCanceledException)
        {
            return fail(BridgeReasons.AckTimeout, timeoutDetail);
        }
        catch (Exception ex)
        {
            return fail(BridgeReasons.NotRunning, ex.Message);
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
