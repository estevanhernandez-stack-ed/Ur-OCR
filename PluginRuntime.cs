using System.Linq;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Hotkeys;
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;
using RoRoRo.UrOcr.UI;

namespace RoRoRo.UrOcr;

public sealed class PluginRuntime
{
    public PluginClient Client { get; } = new();
    public AccountRegistry Accounts { get; } = new();
    public ForegroundWatcher Foreground { get; }
    public ElevationProbe Elevation { get; } = new();
    public CaptureEngine Capture { get; } = new();
    public ColorMatcher Color { get; } = new();
    public TextMatcher Text { get; } = new();
    public KeySender Keys { get; } = new();
    public ActivityLog Activity { get; } = new();
    public TriggerStore Triggers { get; }
    public SettingsStore Settings { get; }
    public DpiGuard Dpi { get; } = new();
    public ToastService Toasts { get; } = new();
    public HotkeyService Hotkey { get; } = new();
    public Ipc.MacroRunClient MacroClient { get; } = new();
    public IWindowMetrics WindowMetrics { get; } = new WindowMetrics();
    public TriggerCoordinator? Coordinator { get; private set; }
    public PulseRunner? Pulse { get; private set; }
    public Engine.PreviewEvaluator Preview { get; }
    public DisplayCheckResult LastDpiCheck { get; private set; } = DisplayCheckResult.FirstRun;

    private CancellationTokenSource? _cts;

    public PluginRuntime()
    {
        PluginPaths.EnsureDirectory();
        Triggers = new TriggerStore();
        Settings = new SettingsStore();
        Foreground = new ForegroundWatcher(Accounts);
        // Preview meter anchors to the alt you last focused; before you've
        // focused any alt, fall back to the first running one.
        Preview = new Engine.PreviewEvaluator(Capture, Color, WindowMetrics,
            () => Foreground.LastForegroundAltPid != 0
                ? Foreground.LastForegroundAltPid
                : Accounts.Pids.FirstOrDefault());
    }

    public async Task StartAsync()
    {
        _cts = new CancellationTokenSource();
        var connected = await Client.ConnectAsync(_cts.Token);
        Diagnostics.DiagLog.Write(connected
            ? "host: connected to RoRoRo"
            : $"host: NOT connected to RoRoRo ({Client.LastConnectError ?? "no reason given"}); pulse and triggers cannot see which account is in front");
        if (connected)
            _ = Accounts.RunAsync(Client, _cts.Token);

        Pulse = new PulseRunner(
            Triggers, new SpotReader(Capture, WindowMetrics), MacroClient, Foreground, Elevation, Accounts,
            new SystemClock(), Activity, Diagnostics.DiagLog.Write)
        {
            TickRateHz = Settings.Current.TickRateHz,
        };

        Coordinator = new TriggerCoordinator(
            Triggers, Capture, Color, Text, Foreground, Elevation, Keys, Activity,
            new SystemClock(), WindowMetrics,
            onFirstFire: t => Toasts.Show(t.Action == Storage.TriggerAction.RunMacro
                ? $"✓ \"{t.Name}\" ran a macro"
                : $"✓ \"{t.Name}\" fired ({t.Keybind.Key})"),
            macroClient: MacroClient,
            diag: Diagnostics.DiagLog.Write,
            ringOwner: Pulse.OwnsRing)
        {
            TickRateHz = Settings.Current.TickRateHz,
        };
        // F9 (pause all) and dry run hold the pulse too.
        Pulse.Hold = () => Coordinator?.Paused == true || Coordinator?.DryRun == true;
        Coordinator.Start();
        Pulse.Start();
        LastDpiCheck = Dpi.Check(GetCurrentFingerprint(), Triggers.All.Where(t => !t.IsClientSpace).Select(t => t.Region));
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (Coordinator is not null) await Coordinator.StopAsync();
        if (Pulse is not null) await Pulse.StopAsync();
        Hotkey.Dispose();
        await Client.DisposeAsync();
    }

    private static DisplayFingerprint GetCurrentFingerprint()
    {
        var w = (int)System.Windows.SystemParameters.VirtualScreenWidth;
        var h = (int)System.Windows.SystemParameters.VirtualScreenHeight;
        var dpi = 96;
        try
        {
            var src = System.Windows.PresentationSource.FromVisual(System.Windows.Application.Current?.MainWindow);
            if (src?.CompositionTarget is not null)
                dpi = (int)(src.CompositionTarget.TransformToDevice.M11 * 96);
        }
        catch { /* keep dpi=96 */ }
        return new DisplayFingerprint(w, h, dpi);
    }
}
