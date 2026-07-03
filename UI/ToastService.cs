using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace RoRoRo.UrOcr.UI;

/// <summary>
/// Brand-themed corner toast. Pulls the 626 Labs palette from App.xaml
/// (navy fill, cyan accent, divider border, brand font) so notifications
/// match the plugin window instead of the old generic gray box. Falls back
/// to the brand hex values if a resource can't be resolved.
/// </summary>
public sealed class ToastService
{
    public void Show(string message)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var bg = Brush("BgBrush", "#0F1F31");
            var accent = Brush("CyanBrush", "#17D4FA");
            var borderBrush = Brush("DividerBrush", "#1F3149");
            var fg = Brush("WhiteBrush", "#FFFFFF");
            var font = Application.Current.TryFindResource("BodyFont") as FontFamily
                       ?? new FontFamily("Segoe UI Variable Text, Segoe UI");

            var w = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.Manual,
            };

            // Cyan accent stripe on the left edge — the brand tell.
            var stripe = new Border
            {
                Background = accent,
                Width = 3,
                CornerRadius = new CornerRadius(2, 0, 0, 2),
            };
            var text = new TextBlock
            {
                Text = message,
                Foreground = fg,
                FontFamily = font,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(11, 8, 14, 8),
                MaxWidth = 360,
                TextWrapping = TextWrapping.Wrap,
            };
            var row = new DockPanel();
            DockPanel.SetDock(stripe, Dock.Left);
            row.Children.Add(stripe);
            row.Children.Add(text);

            var border = new Border
            {
                Background = bg,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = row,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 16,
                    ShadowDepth = 2,
                    Opacity = 0.45,
                },
                Margin = new Thickness(16), // room for the shadow inside the transparent window
            };

            w.Content = border;
            w.Loaded += (_, _) =>
            {
                w.Left = SystemParameters.WorkArea.Right - w.ActualWidth - 12;
                w.Top = SystemParameters.WorkArea.Bottom - w.ActualHeight - 12;
            };
            w.Show();

            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            t.Tick += (_, _) => { t.Stop(); w.Close(); };
            t.Start();
        });
    }

    private static Brush Brush(string key, string fallbackHex)
        => Application.Current.TryFindResource(key) as Brush
           ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallbackHex));
}
