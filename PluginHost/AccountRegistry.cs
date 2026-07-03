using System.Collections.Concurrent;
using System.Linq;
using Grpc.Core;
using ROROROblox.PluginContract;

namespace RoRoRo.UrOcr.PluginHost;

public sealed class AccountRegistry
{
    private readonly ConcurrentDictionary<int, long> _pidToUserId = new();

    public bool TryGetUserId(int pid, out long userId) => _pidToUserId.TryGetValue(pid, out userId);
    public bool IsAltPid(int pid) => _pidToUserId.ContainsKey(pid);
    public int Count => _pidToUserId.Count;
    public IReadOnlyCollection<int> Pids => _pidToUserId.Keys.OrderBy(p => p).ToArray();

    public void Add(int pid, long userId) => _pidToUserId[pid] = userId;
    public void Remove(int pid) => _pidToUserId.TryRemove(pid, out _);
    public void Clear() => _pidToUserId.Clear();

    public async Task RunAsync(PluginClient client, CancellationToken ct)
    {
        if (client.Host is null) throw new InvalidOperationException("Connect first.");

        var launchedTask = ConsumeAsync(client.Host.SubscribeAccountLaunched(
            new SubscriptionRequest(), cancellationToken: ct), ct, evt => Add((int)evt.ProcessId, evt.RobloxUserId));
        var exitedTask = ConsumeAsync(client.Host.SubscribeAccountExited(
            new SubscriptionRequest(), cancellationToken: ct), ct, evt => Remove((int)evt.ProcessId));

        await Task.WhenAll(launchedTask, exitedTask);
    }

    private static async Task ConsumeAsync<T>(Grpc.Core.AsyncServerStreamingCall<T> stream,
        CancellationToken ct, Action<T> onEach)
    {
        try
        {
            await foreach (var evt in stream.ResponseStream.ReadAllAsync(ct))
                onEach(evt);
        }
        catch (OperationCanceledException) { }
        finally { stream.Dispose(); }
    }
}
