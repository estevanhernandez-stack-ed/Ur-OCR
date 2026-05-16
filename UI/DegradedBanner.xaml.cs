using System.Windows;
using System.Windows.Controls;

namespace RoRoRo.UrOcr.UI;

public partial class DegradedBanner : UserControl
{
    public DegradedBanner() { InitializeComponent(); }
    public void Show(string text) { Message.Text = text; Visibility = Visibility.Visible; }
    public void Clear() { Visibility = Visibility.Collapsed; }
}
