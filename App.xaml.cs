using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using RoRoRo.UrOcr.Diagnostics;
using RoRoRo.UrOcr.Theming;
using RoRoRo.UrOcr.UI;

namespace RoRoRo.UrOcr;

public partial class App : Application
{
    public PluginRuntime Runtime { get; } = new();
    public TrayService Tray { get; private set; } = null!;
    private HostThemeService? _theme;
    private StartupWatchdog? _watchdog;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Evidence layer first — handlers, session header, and watchdog exist
        // before any construction step can crash or hang.
        RegisterExceptionEvidence();

        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "?";
        DiagLog.Write($"=== RoRoRo Ur OCR v{version} starting — pid {Environment.ProcessId}, " +
                      $"{Environment.OSVersion.VersionString}, .NET {Environment.Version} ===");
        _watchdog = new StartupWatchdog();

        // Manual-verify hook. Checked only when the variable is set.
        var testCrash = Environment.GetEnvironmentVariable("UROCR_TEST_CRASH");
        if (testCrash == "hang")
        {
            DiagLog.Write("UROCR_TEST_CRASH=hang — blocking OnStartup deliberately");
            Thread.Sleep(Timeout.Infinite); // windowless hang; the watchdog reports it
        }

        // Sync brushes to the RoRoRo host's active theme before the first
        // await lets the StartupUri window render, then follow switches live.
        DiagLog.Write("startup: theme sync");
        _theme = new HostThemeService();
        _theme.Start();

        DiagLog.Write("startup: runtime");
        await Runtime.StartAsync();

        DiagLog.Write("startup: tray");
        Tray = new TrayService();
        Tray.OpenRequested += () => MainWindow?.Show();
        Tray.OpenLogFolderRequested += OpenLogFolder;
        Tray.QuitRequested += () => Shutdown();
        Tray.TogglePauseRequested += TogglePause;

        if (MainWindow is not null)
        {
            DiagLog.Write("startup: hotkeys");
            Runtime.Hotkey.Attach(MainWindow);
            try
            {
                var key = (Key)Enum.Parse(typeof(Key), Runtime.Settings.Current.PauseAllHotkey, true);
                Runtime.Hotkey.RegisterPauseAll(key, TogglePause);
            }
            catch { /* fall back silently on parse fail */ }
        }

        if (testCrash == "dispatcher")
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) =>
                throw new InvalidOperationException("UROCR_TEST_CRASH=dispatcher — deliberate test crash");
            timer.Start();
        }

        DiagLog.Write("startup: complete");
        _watchdog.MarkComplete();
    }

    /// <summary>
    /// Log-then-crash-loud evidence handlers (host philosophy: silent crash is
    /// worse than loud crash — never set Handled, just leave a trace). Handlers
    /// alone can't see liveness bugs — that's the StartupWatchdog's job.
    /// </summary>
    private void RegisterExceptionEvidence()
    {
        DispatcherUnhandledException += (_, args) =>
            DiagLog.Write($"FATAL (dispatcher): {args.Exception}");

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            DiagLog.Write($"FATAL (appdomain, terminating={args.IsTerminating}): {args.ExceptionObject}");

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            DiagLog.Write($"UNOBSERVED task exception: {args.Exception}");
            args.SetObserved(); // behavior-preserving; evidence only
        };
    }

    private static void OpenLogFolder()
    {
        try
        {
            System.IO.Directory.CreateDirectory(DiagLog.Directory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = DiagLog.Directory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            DiagLog.Write($"open log folder failed: {ex.Message}");
        }
    }

    private void TogglePause()
    {
        if (Runtime.Coordinator is null) return;
        Runtime.Coordinator.Paused = !Runtime.Coordinator.Paused;
        Tray?.SetTooltip(Runtime.Coordinator.Paused
            ? "RoRoRo Ur OCR — Paused" : "RoRoRo Ur OCR — Watching");
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try { _theme?.Dispose(); } catch { }
        Tray?.Dispose();
        await Runtime.StopAsync();
        // Absence of this line at the end of a session = crash or hang, not exit.
        DiagLog.Write($"exiting cleanly (code {e.ApplicationExitCode})");
        base.OnExit(e);
    }
}
