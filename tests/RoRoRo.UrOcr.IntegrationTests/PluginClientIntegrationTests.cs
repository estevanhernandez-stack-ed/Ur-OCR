using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.Plugins;
using ROROROblox.PluginContract;
using RoRoRo.UrOcr.PluginHost;

namespace RoRoRo.UrOcr.IntegrationTests;

/// <summary>
/// Integration tests that spin up a real Kestrel-hosted gRPC server — same pipeline as
/// production: named-pipe transport, CapabilityInterceptor, PluginHostService — and drive
/// the plugin's own types against it.
///
/// Pattern mirrors ROROROblox.PluginTestHarness/EndToEndContractTests.cs. There is no
/// fixture helper class in the harness; each test assembles the server inline. BuildServer()
/// encapsulates the boilerplate and returns a (startup, pipeName, eventBus) tuple.
///
/// AccountRegistry.RunAsync accepts a sealed PluginClient with a hard-coded production pipe
/// name, so integration tests that need to exercise the subscription+registry loop drive the
/// streaming RPCs directly via a TestPluginClient (same wiring as PluginClient but with an
/// injected pipe name) and call AccountRegistry.Add/Remove through the same streaming
/// consumer pattern AccountRegistry.RunAsync uses internally.
/// </summary>
public sealed class PluginClientIntegrationTests
{
    // Capabilities declared in 626labs.ur-ocr's manifest.json.
    // Only host.* ones are gated by CapabilityInterceptor; system.* are disclosure-only.
    private static readonly string[] UrOcrCapabilities =
    [
        PluginCapability.HostEventsAccountLaunched,
        PluginCapability.HostEventsAccountExited,
        PluginCapability.HostUITrayMenu,
        "system.read-screen",
        "system.synthesize-keyboard-input",
    ];

    // -------------------------------------------------------------------------
    // Test 1 — Handshake succeeds when ur-ocr is installed + consented
    // -------------------------------------------------------------------------

    /// <summary>
    /// The plugin's own PluginClient.PluginId / ContractVersion constants must be accepted
    /// by a host that has 626labs.ur-ocr installed. This is the M2 gate — nothing else can
    /// flow if the handshake is broken.
    ///
    /// TestPluginClient mirrors PluginClient.ConnectAsync exactly but accepts an injected
    /// pipe name so it can dial the per-test server instead of the production "rororo-plugin-host".
    /// </summary>
    [Fact]
    public async Task Handshake_Succeeds_Against_TestHost()
    {
        var (startup, pipeName, _) = BuildServer(
            installedPluginId: PluginClient.PluginId,
            grantedCapabilities: UrOcrCapabilities);

        await startup.StartAsync(CancellationToken.None);
        try
        {
            await using var client = new TestPluginClient(pipeName);
            var accepted = await client.ConnectAsync();

            Assert.True(accepted, $"Handshake rejected: {client.LastConnectError}");
            Assert.NotNull(client.LastHandshake);
            Assert.Equal("1.4.0", client.LastHandshake.HostVersion);
        }
        finally
        {
            await startup.StopAsync(CancellationToken.None);
            await startup.DisposeAsync();
        }
    }

    // -------------------------------------------------------------------------
    // Test 2 — Handshake rejected when the plugin is not installed
    // -------------------------------------------------------------------------

    /// <summary>
    /// When the host registry has no entry for 626labs.ur-ocr, ConnectAsync must return
    /// false without throwing. The production PluginClient swallows the rejection and
    /// surfaces it via LastConnectError — this confirms the full round-trip.
    /// </summary>
    [Fact]
    public async Task Handshake_Rejected_WhenPluginNotInstalled()
    {
        // Install a *different* plugin id — ur-ocr's id won't resolve in FindById.
        var (startup, pipeName, _) = BuildServer(
            installedPluginId: "626labs.some-other-plugin",
            grantedCapabilities: [PluginCapability.HostEventsAccountLaunched]);

        await startup.StartAsync(CancellationToken.None);
        try
        {
            await using var client = new TestPluginClient(pipeName);
            var accepted = await client.ConnectAsync();

            Assert.False(accepted);
            Assert.NotNull(client.LastConnectError);
        }
        finally
        {
            await startup.StopAsync(CancellationToken.None);
            await startup.DisposeAsync();
        }
    }

    // -------------------------------------------------------------------------
    // Test 3 — AccountLaunched event populates AccountRegistry within 500 ms
    // -------------------------------------------------------------------------

    /// <summary>
    /// Raising an AccountLaunched event on the InProcessPluginEventBus must flow through
    /// the SubscribeAccountLaunched stream and be recorded in AccountRegistry within 500 ms.
    ///
    /// AccountRegistry.RunAsync takes a sealed PluginClient (hard-coded pipe), so instead
    /// we subscribe directly via the TestPluginClient's Host and drive the registry via the
    /// same ConsumeAsync pattern AccountRegistry uses internally. This tests the actual
    /// stream + registry interaction, not the RunAsync plumbing.
    /// </summary>
    [Fact]
    public async Task Subscribe_AccountLaunched_PopulatesRegistry()
    {
        var (startup, pipeName, eventBus) = BuildServer(
            installedPluginId: PluginClient.PluginId,
            grantedCapabilities: UrOcrCapabilities);

        await startup.StartAsync(CancellationToken.None);
        try
        {
            await using var client = new TestPluginClient(pipeName);
            var accepted = await client.ConnectAsync();
            Assert.True(accepted, $"Handshake rejected: {client.LastConnectError}");

            var registry = new AccountRegistry();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // Subscribe to the launched stream and pump events into the registry.
            // Mirrors the ConsumeAsync pattern in AccountRegistry exactly.
            // The x-plugin-id header is required by CapabilityInterceptor for gated RPCs.
            var launchedStream = client.Host!.SubscribeAccountLaunched(
                new SubscriptionRequest(), headers: client.PluginIdHeaders, cancellationToken: cts.Token);
            var consumeTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (var evt in launchedStream.ResponseStream.ReadAllAsync(cts.Token))
                        registry.Add((int)evt.ProcessId, evt.RobloxUserId);
                }
                catch (OperationCanceledException) { }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled) { }
                finally { launchedStream.Dispose(); }
            }, cts.Token);

            // Give the subscription stream a moment to establish before firing the event.
            await Task.Delay(50);

            const int testPid = 55_555;
            const long testUserId = 123_456_789L;
            eventBus.RaiseAccountLaunched(new RunningAccountSnapshot(
                AccountId: Guid.NewGuid().ToString(),
                RobloxUserId: testUserId,
                DisplayName: "TestPlayer",
                ProcessId: testPid));

            // Poll for up to 500 ms — the registry is written on the gRPC streaming thread.
            var deadline = DateTime.UtcNow.AddMilliseconds(500);
            while (DateTime.UtcNow < deadline && !registry.IsAltPid(testPid))
                await Task.Delay(10);

            Assert.True(registry.IsAltPid(testPid),
                "AccountRegistry did not record the pid within 500 ms.");
            Assert.True(registry.TryGetUserId(testPid, out var storedUserId));
            Assert.Equal(testUserId, storedUserId);

            cts.Cancel();
            await consumeTask;
        }
        finally
        {
            await startup.StopAsync(CancellationToken.None);
            await startup.DisposeAsync();
        }
    }

    // -------------------------------------------------------------------------
    // Test 4 — AccountExited removes the pid from the registry
    // -------------------------------------------------------------------------

    /// <summary>
    /// Companion to Test 3. After an AccountLaunched populates the registry, an
    /// AccountExited event must remove the entry within 500 ms.
    /// </summary>
    [Fact]
    public async Task Subscribe_AccountExited_RemovesPidFromRegistry()
    {
        var (startup, pipeName, eventBus) = BuildServer(
            installedPluginId: PluginClient.PluginId,
            grantedCapabilities: UrOcrCapabilities);

        await startup.StartAsync(CancellationToken.None);
        try
        {
            await using var client = new TestPluginClient(pipeName);
            var accepted = await client.ConnectAsync();
            Assert.True(accepted, $"Handshake rejected: {client.LastConnectError}");

            var registry = new AccountRegistry();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // Subscribe to both launched and exited streams simultaneously.
            var launchedStream = client.Host!.SubscribeAccountLaunched(
                new SubscriptionRequest(), headers: client.PluginIdHeaders, cancellationToken: cts.Token);
            var exitedStream = client.Host!.SubscribeAccountExited(
                new SubscriptionRequest(), headers: client.PluginIdHeaders, cancellationToken: cts.Token);

            var launchedTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (var evt in launchedStream.ResponseStream.ReadAllAsync(cts.Token))
                        registry.Add((int)evt.ProcessId, evt.RobloxUserId);
                }
                catch (OperationCanceledException) { }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled) { }
                finally { launchedStream.Dispose(); }
            }, cts.Token);

            var exitedTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (var evt in exitedStream.ResponseStream.ReadAllAsync(cts.Token))
                        registry.Remove((int)evt.ProcessId);
                }
                catch (OperationCanceledException) { }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled) { }
                finally { exitedStream.Dispose(); }
            }, cts.Token);

            await Task.Delay(50);

            const int testPid = 66_666;
            const long testUserId = 987_654_321L;
            var snapshot = new RunningAccountSnapshot(
                AccountId: Guid.NewGuid().ToString(),
                RobloxUserId: testUserId,
                DisplayName: "TestPlayer2",
                ProcessId: testPid);

            // Launch first, wait for registry to catch it.
            eventBus.RaiseAccountLaunched(snapshot);
            var launchDeadline = DateTime.UtcNow.AddMilliseconds(500);
            while (DateTime.UtcNow < launchDeadline && !registry.IsAltPid(testPid))
                await Task.Delay(10);
            Assert.True(registry.IsAltPid(testPid), "Pid was not recorded before exit test.");

            // Now exit.
            eventBus.RaiseAccountExited(snapshot, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var exitDeadline = DateTime.UtcNow.AddMilliseconds(500);
            while (DateTime.UtcNow < exitDeadline && registry.IsAltPid(testPid))
                await Task.Delay(10);

            Assert.False(registry.IsAltPid(testPid),
                "AccountRegistry still contains the pid 500 ms after AccountExited.");

            cts.Cancel();
            await Task.WhenAll(launchedTask, exitedTask);
        }
        finally
        {
            await startup.StopAsync(CancellationToken.None);
            await startup.DisposeAsync();
        }
    }

    // -------------------------------------------------------------------------
    // BuildServer helper
    // -------------------------------------------------------------------------

    /// <summary>
    /// Assembles a PluginHostStartupService on a unique per-test named pipe.
    /// PluginHostStartupService is sealed, so the pipe name is returned as a plain string.
    /// </summary>
    private static (PluginHostStartupService startup, string pipeName, InProcessPluginEventBus eventBus)
        BuildServer(string installedPluginId, IReadOnlyList<string> grantedCapabilities)
    {
        var pipeName = $"rororo-urocr-test-{Guid.NewGuid():N}";

        var manifest = new PluginManifest
        {
            SchemaVersion = 1,
            Id = installedPluginId,
            Name = installedPluginId,
            Version = "0.1.0",
            ContractVersion = "1.0",
            Publisher = "626 Labs LLC",
            Description = "Integration test stub",
            Capabilities = grantedCapabilities,
        };

        var installed = new InstalledPlugin
        {
            Manifest = manifest,
            InstallDir = Path.GetTempPath(),
            Consent = new ConsentRecord
            {
                PluginId = installedPluginId,
                GrantedCapabilities = grantedCapabilities,
                AutostartEnabled = false,
            },
        };

        var pluginLookup = new SinglePluginLookup(installed);
        var eventBus = new InProcessPluginEventBus();

        var hostService = new PluginHostService(
            pluginLookup,
            hostVersion: "1.4.0",
            supportedContractVersion: "1.0",
            hostState: new FixedHostState("On"),
            runningAccounts: new EmptyAccounts(),
            eventBus: eventBus,
            launcher: new NoOpLauncher(),
            uiTranslator: new PluginUITranslator(new NullUIHost()));

        // Production-shape interceptor: accessor returns null, plugin id comes from the
        // x-plugin-id request header (injected by HeaderInjectingCallInvoker in TestPluginClient).
        var interceptor = new CapabilityInterceptor(
            currentPluginAccessor: () => null,
            consentLookup: id => id == installedPluginId
                ? grantedCapabilities
                : Array.Empty<string>());

        var startup = new PluginHostStartupService(
            hostService, interceptor,
            NullLogger<PluginHostStartupService>.Instance,
            pipeName);

        return (startup, pipeName, eventBus);
    }

    // -------------------------------------------------------------------------
    // Stubs — same pattern as EndToEndContractTests in the harness
    // -------------------------------------------------------------------------

    private sealed class SinglePluginLookup(InstalledPlugin plugin) : IInstalledPluginsLookup
    {
        public InstalledPlugin? FindById(string id) => id == plugin.Manifest.Id ? plugin : null;
    }

    private sealed class FixedHostState(string state) : IPluginHostStateProvider
    {
        public bool MultiInstanceEnabled => state == "On";
        public string MultiInstanceState => state;
    }

    private sealed class EmptyAccounts : IRunningAccountsProvider
    {
        public IReadOnlyList<RunningAccountSnapshot> Snapshot() => [];
    }

    private sealed class NoOpLauncher : IPluginLaunchInvoker
    {
        public Task<(bool ok, string? failureReason, int processId)> RequestLaunchAsync(string accountId)
            => Task.FromResult<(bool, string?, int)>((false, "test stub", 0));
    }

    private sealed class NullUIHost : IPluginUIHost
    {
        public string AddTrayMenuItem(string p, string l, string? t, bool e, Action c) => string.Empty;
        public string AddRowBadge(string p, string t, string? c, string? tt) => string.Empty;
        public string AddStatusPanel(string p, string t, string b) => string.Empty;
        public void Update(string h, string l) { }
        public void Remove(string h) { }
    }

    // -------------------------------------------------------------------------
    // TestPluginClient
    // -------------------------------------------------------------------------

    /// <summary>
    /// PluginClient is sealed with a hard-coded production pipe name "rororo-plugin-host".
    /// TestPluginClient mirrors its ConnectAsync logic with an injected pipe name so
    /// integration tests can dial the per-test Kestrel server. The Host property is
    /// exposed so tests can call streaming RPCs directly.
    /// </summary>
    private sealed class TestPluginClient(string pipeName) : IAsyncDisposable
    {
        private GrpcChannel? _channel;
        public RoRoRoHost.RoRoRoHostClient? Host { get; private set; }
        public HandshakeResponse? LastHandshake { get; private set; }
        public string? LastConnectError { get; private set; }

        // Headers sent on every RPC to satisfy the CapabilityInterceptor's x-plugin-id check.
        // Production PluginClient does this via HeaderInjectingCallInvoker (internal sealed),
        // which is inaccessible from this test assembly. Integration tests pass the header
        // explicitly on each call instead.
        public Metadata PluginIdHeaders { get; } = new() { { "x-plugin-id", PluginClient.PluginId } };

        public async Task<bool> ConnectAsync(CancellationToken ct = default)
        {
            _channel = GrpcChannel.ForAddress("http://pipe", new GrpcChannelOptions
            {
                HttpHandler = new SocketsHttpHandler
                {
                    ConnectCallback = async (_, cancel) =>
                    {
                        var pipe = new NamedPipeClientStream(".", pipeName,
                            PipeDirection.InOut, PipeOptions.Asynchronous);
                        await pipe.ConnectAsync(cancel);
                        return pipe;
                    },
                },
            });

            Host = new RoRoRoHost.RoRoRoHostClient(_channel);

            try
            {
                LastHandshake = await Host.HandshakeAsync(
                    new HandshakeRequest
                    {
                        PluginId = PluginClient.PluginId,
                        ContractVersion = PluginClient.ContractVersion,
                    },
                    headers: PluginIdHeaders,
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
}
