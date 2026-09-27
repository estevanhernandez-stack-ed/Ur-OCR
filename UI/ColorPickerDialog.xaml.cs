using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.UI;

public partial class ColorPickerDialog : Window
{
    private readonly Bitmap _sourceBitmap;
    public Rgb? SelectedColor { get; private set; }
    public PickPoint? SelectedPoint { get; private set; }
    public SampleBox SelectedBox { get; } = new();
    public int Tolerance { get; private set; } = 15;

    public ColorPickerDialog(Bitmap region)
    {
        InitializeComponent();
        _sourceBitmap = region;
        PreviewImage.Source = ToBitmapSource(region);
    }

    private void OnImageClick(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(PreviewImage);
        var px = (int)pos.X;
        var py = (int)pos.Y;
        if (px < 0 || py < 0 || px >= _sourceBitmap.Width || py >= _sourceBitmap.Height) return;

        // Average the same box the trigger will check, around the clicked pixel.
        SelectedPoint = new PickPoint(px, py);
        var avg = Engine.ColorMatcher.AverageBox(_sourceBitmap, SelectedPoint, SelectedBox);
        SelectedColor = avg;
        SelectedRgbLabel.Text = $"{Engine.ColorNamer.Describe(avg)}  ({SelectedBox.W}x{SelectedBox.H} average at {px}, {py})";
        ColorSwatch.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb((byte)avg.R, (byte)avg.G, (byte)avg.B));
        OkButton.IsEnabled = true;
    }

    private void OnToleranceChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        Tolerance = (int)e.NewValue;
        if (ToleranceValue is not null) ToleranceValue.Text = Tolerance.ToString();
    }

    private void OnOk(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void OnCancel(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

    private static BitmapSource ToBitmapSource(Bitmap bmp)
    {
        var hbitmap = bmp.GetHbitmap();
        try
        {
            return Imaging.CreateBitmapSourceFromHBitmap(
                hbitmap, IntPtr.Zero, System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
        finally { DeleteObject(hbitmap); }
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
