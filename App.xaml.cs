using System.Windows;
using System.Windows.Input;
using RoRoRo.UrOcr.UI;

namespace RoRoRo.UrOcr;

public partial class App : Application
{
    public PluginRuntime Runtime { get; } = new();
    public TrayService Tray { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        await Runtime.StartAsync();

        Tray = new TrayService();
        Tray.OpenRequested += () => MainWindow?.Show();
        Tray.QuitRequested += () => Shutdown();
        Tray.TogglePauseRequested += TogglePause;

        if (MainWindow is not null)
        {
            Runtime.Hotkey.Attach(MainWindow);
            try
            {
                var key = (Key)Enum.Parse(typeof(Key), Runtime.Settings.Current.PauseAllHotkey, true);
                Runtime.Hotkey.RegisterPauseAll(key, TogglePause);
            }
            catch { /* fall back silently on parse fail */ }
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
        Tray?.Dispose();
        await Runtime.StopAsync();
        base.OnExit(e);
    }
}
