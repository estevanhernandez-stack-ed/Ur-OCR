using System.Windows;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var app = (App)Application.Current;
        DataContext = new MainViewModel(app.Runtime);
        Activity.Bind(app.Runtime.Activity);
        if (app.Runtime.LastDpiCheck == DisplayCheckResult.ChangedWithStaleRegions)
            Banner.Show("Display layout changed — some trigger regions may be off-screen. Re-pick or hide regions.");
        if (!app.Runtime.Text.IsAvailable)
            Banner.Show("OCR engine unavailable — text triggers won't fire. Install a language pack in Windows Settings → Time & language → Language.");
    }

    private void OnSettings(object sender, System.Windows.RoutedEventArgs e)
    {
        var app = (App)Application.Current;
        var dlg = new SettingsFlyout(app.Runtime.Settings.Current) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            app.Runtime.Settings.Save(dlg.Result);
            if (app.Runtime.Coordinator is not null)
                app.Runtime.Coordinator.TickRateHz = dlg.Result.TickRateHz;
            app.Runtime.Keys.HoldMs = dlg.Result.KeyHoldMs;
        }
    }
}
