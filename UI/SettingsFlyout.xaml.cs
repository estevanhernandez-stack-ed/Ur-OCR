using System.Windows;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.UI;

public partial class SettingsFlyout : Window
{
    public Settings Result { get; private set; }

    public SettingsFlyout(Settings current)
    {
        InitializeComponent();
        Result = current;
        TickRate.SelectedIndex = current.TickRateHz switch { 3 => 0, 10 => 2, _ => 1 };
        PauseHotkey.Combo = new KeyCombo(current.PauseAllHotkey, Array.Empty<string>());
        HoldMs.Text = current.KeyHoldMs.ToString();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Result = new Settings
        {
            TickRateHz = int.Parse(((System.Windows.Controls.ComboBoxItem)TickRate.SelectedItem).Content.ToString()!),
            PauseAllHotkey = PauseHotkey.Combo?.Key ?? "F9",
            KeyHoldMs = int.TryParse(HoldMs.Text, out var ms) ? Math.Clamp(ms, 10, 100) : 30,
        };
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
