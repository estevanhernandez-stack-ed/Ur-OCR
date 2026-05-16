using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

public sealed class CaptureEngine : ICaptureSource
{
    private const int SRCCOPY = 0x00CC0020;

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest, int dx, int dy, int w, int h, IntPtr src, int sx, int sy, int rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);

    public Bitmap Capture(RegionRect region)
    {
        var desktopDc = GetDC(IntPtr.Zero);
        var memDc = CreateCompatibleDC(desktopDc);
        var memBmp = CreateCompatibleBitmap(desktopDc, region.Width, region.Height);
        var oldObj = SelectObject(memDc, memBmp);

        BitBlt(memDc, 0, 0, region.Width, region.Height, desktopDc, region.X, region.Y, SRCCOPY);

#pragma warning disable CA1416
        var bmp = Image.FromHbitmap(memBmp);
#pragma warning restore CA1416

        SelectObject(memDc, oldObj);
        DeleteObject(memBmp);
        DeleteDC(memDc);
        ReleaseDC(IntPtr.Zero, desktopDc);

        return bmp;
    }
}
