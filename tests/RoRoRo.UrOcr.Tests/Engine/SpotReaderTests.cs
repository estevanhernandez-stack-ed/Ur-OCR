using System.Drawing;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

public class SpotReaderTests
{
    private static readonly Rgb Grey = new(120, 120, 120);
    private static readonly Rgb Orange = new(240, 160, 40);
    private static readonly Rgb Sky = new(135, 206, 235);

    /// <summary>Paints the whole capture one colour, chosen by the region's X.</summary>
    private sealed class PaintedCapture : ICaptureSource
    {
        public Dictionary<int, Rgb> ByX { get; } = new();
        public List<RegionRect> Regions { get; } = new();
        public Bitmap Capture(RegionRect r)
        {
            Regions.Add(r);
            var c = ByX.TryGetValue(r.X, out var v) ? v : Grey;
            var bmp = new Bitmap(Math.Max(1, r.Width), Math.Max(1, r.Height));
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.FromArgb(c.R, c.G, c.B));
            return bmp;
        }
    }

    private sealed class Metrics : IWindowMetrics
    {
        public bool Gone;
        public IntPtr HwndForPid(int pid) => Gone ? IntPtr.Zero : new IntPtr(0x10);
        public (int X, int Y)? ClientOrigin(IntPtr h) => (0, 0);
        public (int W, int H)? ClientSize(IntPtr h) => (800, 599);
    }

    private static int X(int order) => 100 + order * 20;

    private static Trigger Spot(int order) => new()
    {
        Id = Guid.NewGuid(),
        Name = $"Mine #8: {MeasuredRing.RingOrder[order]}",
        Region = new RegionRect(X(order), 100, 9, 9),
        Mode = TriggerMode.Color,
        Color = new ColorCriteria(new Rgb(0, 0, 0), 25, ColorSamplingMode.SinglePixel,
            Point: new PickPoint(4, 4), Box: new SampleBox(), NoneOf: new[] { Sky }),
        Keybind = new KeyCombo("F13", Array.Empty<string>()),
        CoordSpace = Trigger.CoordSpaceClient,
        RecordedClientW = 800,
        RecordedClientH = 599,
        Ring = new RingSpot("mine8", order),
    };

    [Fact]
    public void Reads_each_spots_box_with_its_order_tolerance_and_ignore_colours()
    {
        var paint = new PaintedCapture();
        paint.ByX[X(5)] = Orange;
        var reader = new SpotReader(paint, new Metrics());

        var readings = reader.Read(7, Enumerable.Range(0, 8).Select(Spot).ToList())!;

        Assert.Equal(Enumerable.Range(0, 8), readings.Select(r => r.Order));
        Assert.Equal(Orange, readings[5].Sampled);
        Assert.Equal(Grey, readings[0].Sampled);
        Assert.All(readings, r => Assert.Equal(25, r.ToleranceRgb));
        Assert.All(readings, r => Assert.Equal(new[] { Sky }, r.Ignore));
    }

    [Fact]
    public void A_window_it_cannot_find_reads_nothing()
    {
        var reader = new SpotReader(new PaintedCapture(), new Metrics { Gone = true });

        Assert.Null(reader.Read(7, Enumerable.Range(0, 8).Select(Spot).ToList()));
    }

    [Fact]
    public void A_trigger_that_is_not_a_ring_spot_is_left_out()
    {
        var paint = new PaintedCapture();
        var notSpot = Spot(0);
        notSpot.Ring = null;

        var readings = new SpotReader(paint, new Metrics()).Read(7, new[] { notSpot, Spot(1) })!;

        Assert.Equal(new[] { 1 }, readings.Select(r => r.Order));
        Assert.Single(paint.Regions);
    }
}
