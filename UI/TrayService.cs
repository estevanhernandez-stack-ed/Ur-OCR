using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;

namespace RoRoRo.UrOcr.UI;

public sealed class TrayService : IDisposable
{
    private readonly TaskbarIcon _icon;
    public event Action? OpenRequested;
    public event Action? TogglePauseRequested;
    public event Action? QuitRequested;

    public TrayService()
    {
        _icon = new TaskbarIcon
        {
            ToolTipText = "RoRoRo Ur OCR — Watching",
        };
        // Icon source loaded from pack URI; if icon.png isn't in pack, the tray icon may render blank — acceptable for v0.1 dev
        try
        {
            _icon.IconSource = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/icon.png", UriKind.Absolute));
        }
        catch { /* icon load failure isn't fatal — tray still works */ }

        var menu = new System.Windows.Controls.ContextMenu();
        var open = new System.Windows.Controls.MenuItem { Header = "Open" };
        open.Click += (_, _) => OpenRequested?.Invoke();
        var pause = new System.Windows.Controls.MenuItem { Header = "Pause all watchers (F9)" };
        pause.Click += (_, _) => TogglePauseRequested?.Invoke();
        var quit = new System.Windows.Controls.MenuItem { Header = "Quit" };
        quit.Click += (_, _) => QuitRequested?.Invoke();
        menu.Items.Add(open);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(pause);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(quit);
        _icon.ContextMenu = menu;
    }

    public void SetTooltip(string t) => _icon.ToolTipText = t;
    public void Dispose() => _icon.Dispose();
}
