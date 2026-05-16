using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.UI;

public partial class KeybindCapture : UserControl
{
    public static readonly DependencyProperty ComboProperty =
        DependencyProperty.Register(nameof(Combo), typeof(KeyCombo), typeof(KeybindCapture),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnComboChanged));

    public KeyCombo? Combo { get => (KeyCombo?)GetValue(ComboProperty); set => SetValue(ComboProperty, value); }

    public KeybindCapture() { InitializeComponent(); MouseLeftButtonDown += (_, _) => Focus(); }

    private static void OnComboChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (KeybindCapture)d;
        ctrl.Display.Text = e.NewValue is KeyCombo c ? Render(c) : "Click and press a key combo...";
    }

    private static string Render(KeyCombo c) =>
        c.Modifiers.Count == 0 ? c.Key : string.Join("+", c.Modifiers.Append(c.Key));

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var mods = new List<string>();
        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) mods.Add("Ctrl");
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) mods.Add("Shift");
        if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt)) mods.Add("Alt");
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) mods.Add("Win");

        var k = e.Key == Key.System ? e.SystemKey : e.Key;
        if (k is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
              or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;

        var candidate = new KeyCombo(k.ToString(), mods);
        var owner = Window.GetWindow(this);
        if (owner is not null && !KeybindValidation.Confirm(candidate, owner)) return;
        Combo = candidate;
    }
}
