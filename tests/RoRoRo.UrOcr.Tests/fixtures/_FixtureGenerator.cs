using System.Drawing;
using System.Drawing.Imaging;

namespace RoRoRo.UrOcr.Tests.Fixtures;

internal static class FixtureGenerator
{
    public static Bitmap MakeBanner(string text, int width = 320, int height = 60)
    {
        var bmp = new Bitmap(width, height);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        using var font = new Font("Arial", 24, FontStyle.Bold);
        using var brush = new SolidBrush(Color.Black);
        g.DrawString(text, font, brush, 10, 10);
        return bmp;
    }
}
