using System.Windows;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.UI;

public partial class DangerousKeybindDialog : Window
{
    private static readonly HashSet<string> Bad = new(StringComparer.OrdinalIgnoreCase)
    {
        "Win+L", "Alt+F4", "Ctrl+W", "Ctrl+F4", "Win+D"
    };

    public static bool IsDangerous(KeyCombo c)
    {
        var label = c.Modifiers.Count == 0 ? c.Key : string.Join("+", c.Modifiers.Append(c.Key));
        return Bad.Contains(label);
    }

    public DangerousKeybindDialog(KeyCombo c)
    {
        InitializeComponent();
        var label = c.Modifiers.Count == 0 ? c.Key : string.Join("+", c.Modifiers.Append(c.Key));
        Body.Text = $"{label} is a system-level shortcut and may interrupt your session. Are you sure?";
    }

    private void OnConfirm(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void OnCancel(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}

public static class KeybindValidation
{
    public static bool Confirm(KeyCombo c, Window owner)
    {
        if (!DangerousKeybindDialog.IsDangerous(c)) return true;
        var dlg = new DangerousKeybindDialog(c) { Owner = owner };
        return dlg.ShowDialog() == true;
    }
}
