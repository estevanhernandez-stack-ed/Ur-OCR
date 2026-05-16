using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace RoRoRo.UrOcr.UI;

public sealed class ToastService
{
    public void Show(string message)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
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
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(230, 30, 30, 30)),
                Padding = new Thickness(14, 8, 14, 8),
                CornerRadius = new CornerRadius(6),
            };
            border.Child = new TextBlock
            {
                Text = message,
                Foreground = Brushes.White,
                FontSize = 13,
            };
            w.Content = border;
            w.Loaded += (_, _) =>
            {
                w.Left = SystemParameters.WorkArea.Right - w.ActualWidth - 20;
                w.Top = SystemParameters.WorkArea.Bottom - w.ActualHeight - 20;
            };
            w.Show();
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            t.Tick += (_, _) => { t.Stop(); w.Close(); };
            t.Start();
        });
    }
}
