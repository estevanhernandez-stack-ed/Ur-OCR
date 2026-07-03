// tests/RoRoRo.UrOcr.Tests/Engine/PreviewEvaluatorTests.cs
using System;
using System.Drawing;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

public class PreviewEvaluatorTests
{
    private sealed class FakeCapture : ICaptureSource
    {
        private readonly Color _fill;
        public FakeCapture(int r, int g, int b) => _fill = Color.FromArgb(r, g, b);
        public Bitmap Capture(RegionRect region)
        {
            var bmp = new Bitmap(Math.Max(1, region.Width), Math.Max(1, region.Height));
            using var gfx = Graphics.FromImage(bmp);
            gfx.Clear(_fill);
            return bmp;
        }
    }

    private sealed class FakeMetrics : IWindowMetrics
    {
        public IntPtr Hwnd = new(0x10);
        public (int X, int Y)? Origin = (0, 0);
        public (int W, int H)? Size = (800, 600);
        public IntPtr HwndForPid(int pid) => pid == 0 ? IntPtr.Zero : Hwnd;
        public (int X, int Y)? ClientOrigin(IntPtr h) => h == IntPtr.Zero ? null : Origin;
        public (int W, int H)? ClientSize(IntPtr h) => h == IntPtr.Zero ? null : Size;
    }

    private static Trigger ScreenTrigger(RegionRect region, ColorCriteria color) => new()
    {
        Id = Guid.NewGuid(), Name = "s", Region = region, Mode = TriggerMode.Color, Color = color,
        Keybind = new KeyCombo("F", Array.Empty<string>()), CoordSpace = Trigger.CoordSpaceScreen,
    };

    private static Trigger ClientTrigger(RegionRect region, ColorCriteria color) => new()
    {
        Id = Guid.NewGuid(), Name = "c", Region = region, Mode = TriggerMode.Color, Color = color,
        Keybind = new KeyCombo("F", Array.Empty<string>()),
        CoordSpace = Trigger.CoordSpaceClient, RecordedClientW = 800, RecordedClientH = 600,
    };

    [Fact]
    public void EvaluateTrigger_ScreenTrigger_ReturnsMatchResult_ForCapturedColor()
    {
        var pe = new PreviewEvaluator(new FakeCapture(255, 17, 95), new ColorMatcher(), new FakeMetrics(), new AccountRegistry());
        var trig = ScreenTrigger(new RegionRect(0, 0, 4, 4), new ColorCriteria(new Rgb(255, 17, 95), 10, ColorSamplingMode.SinglePixel));

        var r = pe.EvaluateTrigger(trig);

        Assert.NotNull(r);
        Assert.True(r!.Matched);
        Assert.True(r.Distance < 0.001);
    }

    [Fact]
    public void EvaluateTrigger_DegenerateRegion_ReturnsNull()
    {
        var pe = new PreviewEvaluator(new FakeCapture(0, 0, 0), new ColorMatcher(), new FakeMetrics(), new AccountRegistry());
        var trig = ScreenTrigger(new RegionRect(0, 0, 0, 0), new ColorCriteria(new Rgb(0, 0, 0), 5, ColorSamplingMode.SinglePixel));

        Assert.Null(pe.EvaluateTrigger(trig));
    }

    [Fact]
    public void EvaluateTrigger_ClientTrigger_NoRunningAlts_ReturnsNull()
    {
        // No alts registered -> Pids.FirstOrDefault() == 0 -> resolver returns null.
        var pe = new PreviewEvaluator(new FakeCapture(255, 17, 95), new ColorMatcher(), new FakeMetrics(), new AccountRegistry());
        var trig = ClientTrigger(new RegionRect(50, 60, 30, 40), new ColorCriteria(new Rgb(255, 17, 95), 10, ColorSamplingMode.SinglePixel));

        Assert.Null(pe.EvaluateTrigger(trig));
    }

    [Fact]
    public void EvaluateTrigger_ClientTrigger_AnchorsToFirstRunningAlt()
    {
        var accounts = new AccountRegistry();
        accounts.Add(111, userId: 1);
        var pe = new PreviewEvaluator(new FakeCapture(255, 17, 95), new ColorMatcher(), new FakeMetrics(), accounts);
        var trig = ClientTrigger(new RegionRect(50, 60, 30, 40), new ColorCriteria(new Rgb(255, 17, 95), 10, ColorSamplingMode.SinglePixel));

        var r = pe.EvaluateTrigger(trig);

        Assert.NotNull(r);
        Assert.True(r!.Matched);
    }

    [Fact]
    public void EvaluateTrigger_NonColorTrigger_ReturnsNull()
    {
        var pe = new PreviewEvaluator(new FakeCapture(255, 17, 95), new ColorMatcher(), new FakeMetrics(), new AccountRegistry());
        var trig = ScreenTrigger(new RegionRect(0, 0, 4, 4), new ColorCriteria(new Rgb(255, 17, 95), 10, ColorSamplingMode.SinglePixel));
        trig.Mode = TriggerMode.Text;

        Assert.Null(pe.EvaluateTrigger(trig));
    }
}
