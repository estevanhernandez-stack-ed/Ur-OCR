using System.IO.Pipes;
using System.Net.Http;
using Grpc.Core;
using Grpc.Net.Client;
using ROROROblox.PluginContract;

namespace RoRoRo.UrOcr.PluginHost;

public sealed class PluginClient : IAsyncDisposable
{
    public const string PluginId = "626labs.ur-ocr";
    public const string ContractVersion = "1.0";
    private const string PipeName = "rororo-plugin-host";

    private GrpcChannel? _channel;
    public RoRoRoHost.RoRoRoHostClient? Host { get; private set; }
    public HandshakeResponse? LastHandshake { get; private set; }
    public string? LastConnectError { get; private set; }

    public async Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        if (_channel is not null)
            throw new InvalidOperationException("Already connected. Dispose first.");

        _channel = GrpcChannel.ForAddress("http://pipe", new GrpcChannelOptions
        {
            HttpHandler = new SocketsHttpHandler
            {
                ConnectCallback = async (_, cancel) =>
                {
                    var pipe = new NamedPipeClientStream(
                        ".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                    await pipe.ConnectAsync(cancel);
                    return pipe;
                },
            },
        });

        var invoker = new HeaderInjectingCallInvoker(_channel.CreateCallInvoker(), PluginId);
        Host = new RoRoRoHost.RoRoRoHostClient(invoker);

        try
        {
            LastHandshake = await Host.HandshakeAsync(
                new HandshakeRequest { PluginId = PluginId, ContractVersion = ContractVersion },
                cancellationToken: ct);
            if (!LastHandshake.Accepted)
                LastConnectError = LastHandshake.RejectReason;
            return LastHandshake.Accepted;
        }
        catch (RpcException ex)
        {
            LastConnectError = $"{ex.StatusCode}: {ex.Status.Detail}";
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.ShutdownAsync();
            _channel.Dispose();
        }
    }
}
