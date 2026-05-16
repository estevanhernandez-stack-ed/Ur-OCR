using Grpc.Core;
using Grpc.Core.Interceptors;

namespace RoRoRo.UrOcr.PluginHost;

internal sealed class HeaderInjectingCallInvoker(CallInvoker inner, string pluginId) : CallInvoker
{
    private readonly CallInvoker _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly Metadata.Entry _header =
        new("x-plugin-id", pluginId ?? throw new ArgumentNullException(nameof(pluginId)));

    private CallOptions With(CallOptions options)
    {
        var headers = options.Headers ?? new Metadata();
        if (!headers.Any(h => h.Key == "x-plugin-id"))
            headers.Add(_header);
        return options.WithHeaders(headers);
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        => _inner.AsyncUnaryCall(method, host, With(options), request);

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        => _inner.AsyncServerStreamingCall(method, host, With(options), request);

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options)
        => _inner.AsyncClientStreamingCall(method, host, With(options));

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options)
        => _inner.AsyncDuplexStreamingCall(method, host, With(options));

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        => _inner.BlockingUnaryCall(method, host, With(options), request);
}
