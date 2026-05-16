using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace RoRoRo.UrOcr.UI;

public partial class Loupe : UserControl
{
    public Loupe() { InitializeComponent(); }

    public void UpdateAt(int screenX, int screenY, RoRoRo.UrOcr.Engine.CaptureEngine capture)
    {
        var src = capture.Capture(new RoRoRo.UrOcr.Storage.RegionRect(screenX - 10, screenY - 10, 20, 20));
        var hbitmap = src.GetHbitmap();
        try
        {
            ZoomImage.Source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hbitmap, IntPtr.Zero, System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(160, 160));
        }
        finally { DeleteObject(hbitmap); src.Dispose(); }
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
