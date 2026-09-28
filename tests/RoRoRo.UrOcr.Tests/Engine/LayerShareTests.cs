using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;
using Xunit;
using Xunit.Abstractions;

namespace RoRoRo.UrOcr.Tests.Engine;

/// <summary>
/// The pulse's layer read by colour share (spec "The layer is read by colour share, not 8 spots"):
/// a disc of 3 blocks around the character, HUD and character out, each pixel to its nearest listed
/// colour within tolerance; ore and shared colours count for nobody.
/// </summary>
public class LayerShareTests(ITestOutputHelper output)
{
    private const int W = 800, H = 599, Cx = 400, Cy = 340, Pitch = 50, Tol = 30;

    private static readonly Rgb Base = new(10, 10, 28);        // both layers' dark rock
    private static readonly Rgb Magenta = new(200, 0, 160);    // only the top layer
    private static readonly Rgb Line = new(20, 60, 230);       // only the bottom layer
    private static readonly Rgb Ore = new(205, 10, 170);       // within tolerance of magenta, but ore

    private static readonly LayerDefinition[] Two =
    {
        new("top", new[] { Base, Magenta }),
        new("bottom", new[] { Base, Line }),
    };

    private static FramePixels Paint(Func<int, int, Rgb> colourAt)
    {
        var px = new int[W * H];
        for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
                px[y * W + x] = FramePixels.Pack(colourAt(x, y));
        return new FramePixels(W, H, px);
    }

    /// <summary>Diagonal stripes: <paramref name="aPct"/>% of the pixels in <paramref name="a"/>, the
    /// next <paramref name="bPct"/>% in <paramref name="b"/>, the rest the shared base.</summary>
    private static FramePixels Stripes(Rgb a, int aPct, Rgb b, int bPct) => Paint((x, y) =>
    {
        var k = (x / 2 + y / 2) % 100;
        return k < aPct ? a : k < aPct + bPct ? b : Base;
    });

    private static LayerRead Read(FramePixels frame, IReadOnlyList<Rgb>? ore = null, int cx = Cx, int cy = Cy) =>
        LayerShare.Read(frame, cx, cy, Pitch, Two, ore ?? Array.Empty<Rgb>(), Tol, LayerShare.DefaultMinShare, LayerShare.DefaultLead);

    [Fact]
    public void Two_layers_with_the_same_base_read_by_their_accents()
    {
        Assert.Equal("top", Read(Stripes(Magenta, 20, Line, 0)).Layer);
        Assert.Equal("bottom", Read(Stripes(Line, 20, Magenta, 0)).Layer);
    }

    [Fact]
    public void A_frame_of_only_the_shared_base_is_no_layer()
    {
        var r = Read(Paint((_, _) => Base));

        Assert.Null(r.Layer);
        Assert.Equal(0, r.Shares["top"]);
        Assert.Equal(0, r.Shares["bottom"]);
        Assert.True(r.PixelsRead > 0);
    }

    [Fact]
    public void Ore_pixels_count_for_nobody()
    {
        var r = Read(Stripes(Ore, 30, Line, 0), new[] { Ore });

        Assert.Null(r.Layer);
        Assert.Equal(0, r.Shares["top"]);
    }

    [Fact]
    public void The_leader_needs_one_and_a_half_times_the_runner_up()
    {
        var close = Read(Stripes(Magenta, 10, Line, 8));
        var clear = Read(Stripes(Magenta, 10, Line, 5));

        Assert.Null(close.Layer);
        Assert.InRange(close.Shares["top"], 0.09, 0.11);
        Assert.InRange(close.Shares["bottom"], 0.07, 0.09);
        Assert.Equal("top", clear.Layer);
    }

    [Fact]
    public void A_leader_under_the_minimum_share_is_no_layer()
    {
        Assert.Null(Read(Stripes(Magenta, 3, Line, 0)).Layer);
    }

    [Fact]
    public void Hud_pixels_count_for_nobody()
    {
        // The disc around (250, 340) reaches x = 100, into the icon column (left of 160).
        var r = Read(Paint((x, y) => x < 160 || y > 470 ? Magenta : Base), cx: 250);

        Assert.Null(r.Layer);
        Assert.Equal(0, r.Shares["top"]);
    }

    [Fact]
    public void The_character_counts_for_nobody()
    {
        var r = Read(Paint((x, y) => (x - Cx) * (x - Cx) + (y - Cy) * (y - Cy) < 15 * 15 ? Magenta : Base));

        Assert.Equal(0, r.Shares["top"]);
    }

    [Fact]
    public void The_same_frame_gives_the_same_read()
    {
        var frame = Stripes(Magenta, 12, Line, 3);
        var a = Read(frame);
        var b = Read(frame);

        Assert.Equal(a.Layer, b.Layer);
        Assert.Equal(a.PixelsRead, b.PixelsRead);
        Assert.Equal(a.Shares, b.Shares);
    }

    [Fact]
    public void A_disc_past_the_frame_edge_is_clipped()
    {
        var r = Read(Stripes(Magenta, 20, Line, 0), cx: 780, cy: 100);

        Assert.Equal("top", r.Layer);
    }

    // Real calm frames, cropped to the 800x599 client, nameplates covered; the character at (400, 310).
    // Rock colours from the live measured file, trimmed so no colour is listed for two layers.
    private static readonly LayerDefinition[] Mine8 =
    {
        new("lava", Hex("470092 4A0087 68008B 910095 A200A1 AE009C CC009A FF8B2A")),
        new("blue", Hex("212A78 252D7B 212D80 253391 2D37A6 3C4EB7")),
        new("black", Hex("0F0F93 0C0A89 1010B4 2818F8")),
    };
    private const int RealCy = 310;
    private static readonly IReadOnlyList<Rgb> Mine8Ore = Hex("00F8F6 00C8C3 D546FD 7A8EDA");

    private static Rgb[] Hex(string list) => list.Split(' ').Select(h => new Rgb(
        Convert.ToInt32(h[..2], 16), Convert.ToInt32(h[2..4], 16), Convert.ToInt32(h[4..], 16))).ToArray();

    private static FramePixels Real(string path)
    {
        using var bmp = new System.Drawing.Bitmap(System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures", path + ".png"));
        return FramePixels.FromBitmap(bmp);
    }

    private LayerRead ReadReal(string path)
    {
        var frame = Real(path);
        var pitch = PitchEstimator.Estimate(frame, Cx, RealCy)?.Pitch ?? 50;
        var r = LayerShare.Read(frame, Cx, RealCy, pitch, Mine8, Mine8Ore, Tol, LayerShare.DefaultMinShare, LayerShare.DefaultLead);
        output.WriteLine($"{path}: pitch {pitch}, {r.PixelsRead} px, layer {r.Layer ?? "none"}, " +
                         string.Join(", ", r.Shares.Select(s => $"{s.Key} {s.Value:P1}")));
        return r;
    }

    [Theory]
    [InlineData("layer/lava-tunnel", "lava")]
    [InlineData("layer/blue-tunnel", "blue")]
    [InlineData("pitch/pit", "black")]
    [InlineData("pitch/bottom-grid", "black")]
    public void A_real_frame_reads_its_layer(string path, string layer)
    {
        Assert.Equal(layer, ReadReal(path).Layer);
    }

    [Fact]
    public void A_shaft_in_blue_walls_reads_blue_or_nothing()
    {
        var r = ReadReal("pitch/shaft");

        if (r.Layer is not null) Assert.Equal("blue", r.Layer);
    }
}
