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

        var pixel = _sourceBitmap.GetPixel(px, py);
        SelectedColor = new Rgb(pixel.R, pixel.G, pixel.B);
        SelectedRgbLabel.Text = $"RGB ({pixel.R}, {pixel.G}, {pixel.B})  at ({px}, {py}) within region";
        ColorSwatch.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(pixel.R, pixel.G, pixel.B));
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
