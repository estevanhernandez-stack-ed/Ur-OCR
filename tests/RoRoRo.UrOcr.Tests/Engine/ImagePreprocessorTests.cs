using System.Drawing;
using Xunit;
using RoRoRo.UrOcr.Engine;

namespace RoRoRo.UrOcr.Tests.Engine;

public class ImagePreprocessorTests
{
    [Fact]
    public void Preprocess_doubles_dimensions()
    {
        using var src = new Bitmap(50, 20);
        using var g = Graphics.FromImage(src);
        g.Clear(Color.Gray);
        using var dst = ImagePreprocessor.Preprocess(src);
        Assert.Equal(100, dst.Width);
        Assert.Equal(40, dst.Height);
    }

    [Fact]
    public void Preprocess_produces_binarized_output()
    {
        using var src = new Bitmap(10, 10);
        using var g = Graphics.FromImage(src);
        g.Clear(Color.FromArgb(200, 200, 200));
        using var dst = ImagePreprocessor.Preprocess(src);
        var px = dst.GetPixel(5, 5);
        Assert.True(px.R == 0 || px.R == 255);
    }
}
