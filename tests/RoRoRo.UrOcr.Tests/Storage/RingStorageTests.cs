using System.IO;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Storage;

public class RingStorageTests
{
    private static string TempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "urocr-tests", Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private static RingDefinition Ring() => new("mine8", "Mine #8", new[]
    {
        new LayerDefinition("navy", new[] { new Rgb(30, 30, 90) }),
        new LayerDefinition("grey", new[] { new Rgb(120, 120, 120), new Rgb(100, 100, 100) }),
    }, MinLayerSpots: 3);

    private static Trigger Spot(int order, Guid? id = null, string name = "spot") => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = name,
        Region = new RegionRect(0, 0, 9, 9),
        Mode = TriggerMode.Color,
        Color = new ColorCriteria(new Rgb(0, 0, 0), 30, ColorSamplingMode.SinglePixel,
            Point: new PickPoint(4, 4), Box: new SampleBox(), NoneOf: new[] { new Rgb(135, 206, 235) }),
        Keybind = new KeyCombo("F13", Array.Empty<string>()),
        Ring = new RingSpot("mine8", order),
    };

    [Fact]
    public void Rings_and_ring_spots_survive_a_reload()
    {
        var path = TempFile();
        var s = new TriggerStore(path);
        s.UpsertRing(Ring());
        s.Add(Spot(2));

        var s2 = new TriggerStore(path);

        var ring = Assert.Single(s2.Rings);
        Assert.Equal("mine8", ring.Id);
        Assert.Equal(3, ring.MinLayerSpots);
        Assert.Equal(2, ring.Layers.Count);
        Assert.Equal(new[] { new Rgb(120, 120, 120), new Rgb(100, 100, 100) }, ring.Layers[1].Rock);
        var t = Assert.Single(s2.All);
        Assert.Equal(new RingSpot("mine8", 2), t.Ring);
        Assert.Equal(new[] { new Rgb(135, 206, 235) }, t.Color!.NoneOf);
    }

    [Fact]
    public void A_ring_with_a_finder_survives_a_reload()
    {
        var path = TempFile();
        new TriggerStore(path).UpsertRing(Ring() with { Finders = new[] { FinderSetupTests.Valid() } });

        var f = Assert.Single(Assert.Single(new TriggerStore(path).Rings).Finders!);

        Assert.Equal("grey", f.Layer);
        Assert.Equal((800, 599, 50, 390, 340, 5), (f.ClientW, f.ClientH, f.Pitch, f.CenterX, f.CenterY, f.RadiusBlocks));
        Assert.Equal(new OutlineBox(50, 50, 60, 225), f.Outline);
        Assert.Equal(new OreColour("cyan crystal", new Rgb(60, 220, 230)), Assert.Single(f.Ore));
        Assert.Equal(40, f.OreToleranceRgb);
    }

    [Fact]
    public void A_finder_s_guard_survives_a_reload()
    {
        var path = TempFile();
        var guard = new GuardBox(55, 289, 3, 3, new Rgb(255, 19, 90), 30);
        new TriggerStore(path).UpsertRing(Ring() with { Finders = new[] { FinderSetupTests.Valid() with { Guard = guard } } });

        var f = Assert.Single(Assert.Single(new TriggerStore(path).Rings).Finders!);

        Assert.Equal(guard, f.Guard);
    }

    [Fact]
    public void A_finder_without_a_guard_writes_no_guard_key()
    {
        var path = TempFile();
        new TriggerStore(path).UpsertRing(Ring() with { Finders = new[] { FinderSetupTests.Valid() } });

        Assert.DoesNotContain("\"guard\"", File.ReadAllText(path));
        Assert.Null(Assert.Single(Assert.Single(new TriggerStore(path).Rings).Finders!).Guard);
    }

    [Fact]
    public void A_ring_without_finders_writes_no_finders_key_and_an_old_file_loads()
    {
        var path = TempFile();
        File.WriteAllText(path, """
            { "schemaVersion": 2, "rings": [ { "id": "mine8", "name": "Mine #8", "minLayerSpots": 3,
              "layers": [ { "name": "grey", "rock": [ { "r": 120, "g": 120, "b": 120 } ] } ] } ],
              "triggers": [], "pulses": [] }
            """);

        var s = new TriggerStore(path);
        var ring = Assert.Single(s.Rings);
        Assert.Null(ring.Finders);
        Assert.Null(ring.LayerMinShare);
        Assert.Null(ring.LayerLead);

        s.UpsertRing(ring);
        Assert.DoesNotContain("finders", File.ReadAllText(path));
        Assert.DoesNotContain("layerMinShare", File.ReadAllText(path));
        Assert.DoesNotContain("layerLead", File.ReadAllText(path));
    }

    [Fact]
    public void Layer_share_settings_survive_a_reload()
    {
        var path = TempFile();
        new TriggerStore(path).UpsertRing(Ring() with { LayerMinShare = 0.06, LayerLead = 1.8 });

        var ring = Assert.Single(new TriggerStore(path).Rings);

        Assert.Equal((0.06, 1.8), (ring.LayerMinShare, ring.LayerLead));
    }

    [Theory]
    [InlineData(0.0, null)]
    [InlineData(1.5, null)]
    [InlineData(null, 0.9)]
    public void Out_of_range_layer_share_settings_are_refused(double? minShare, double? lead)
    {
        Assert.NotNull(TriggerValidation.Validate(Ring() with { LayerMinShare = minShare, LayerLead = lead }));
    }

    [Fact]
    public void Layer_trigger_and_hold_survive_a_reload()
    {
        var path = TempFile();
        var s = new TriggerStore(path);
        s.Add(new Trigger
        {
            Id = Guid.NewGuid(), Name = "rock cap",
            Region = new RegionRect(0, 0, 800, 599), Mode = TriggerMode.Layer,
            Layer = new LayerCriteria("mine8", LayerCondition.SameLayer), HoldForMs = 300_000,
            Keybind = new KeyCombo("F13", Array.Empty<string>()),
        });

        var t = Assert.Single(new TriggerStore(path).All);

        Assert.Equal(TriggerMode.Layer, t.Mode);
        Assert.Equal(new LayerCriteria("mine8", LayerCondition.SameLayer), t.Layer);
        Assert.Equal(300_000, t.HoldForMs);
        Assert.Null(t.Color);
    }

    [Fact]
    public void A_file_without_rings_loads_with_none()
    {
        var path = TempFile();
        File.WriteAllText(path, """
        {
          "schemaVersion": 2,
          "triggers": [
            { "id": "11111111-1111-1111-1111-111111111111", "name": "t",
              "enabled": true, "region": { "x": 10, "y": 20, "width": 30, "height": 40 },
              "mode": "color", "accountAware": true, "coordSpace": "screen",
              "color": { "targetRgb": { "r": 1, "g": 2, "b": 3 }, "toleranceRgb": 10, "samplingMode": "singlePixel" },
              "keybind": { "key": "F", "modifiers": [] } }
          ]
        }
        """);

        var s = new TriggerStore(path);

        Assert.Empty(s.Rings);
        var t = Assert.Single(s.All);
        Assert.Null(t.Ring);
        Assert.Null(t.Layer);
        Assert.Equal(0, t.HoldForMs);
        Assert.Null(t.Color!.NoneOf);
    }

    [Fact]
    public void Zero_hold_and_no_ring_are_not_written()
    {
        var path = TempFile();
        var s = new TriggerStore(path);
        s.Add(new Trigger
        {
            Id = Guid.NewGuid(), Name = "plain",
            Region = new RegionRect(0, 0, 10, 10), Mode = TriggerMode.Color,
            Color = new ColorCriteria(new Rgb(0, 0, 0), 5, ColorSamplingMode.SinglePixel),
            Keybind = new KeyCombo("A", Array.Empty<string>()),
        });

        var json = File.ReadAllText(path);

        Assert.DoesNotContain("holdForMs", json);
        Assert.DoesNotContain("\"ring\"", json);
        Assert.DoesNotContain("\"layer\"", json);
    }

    [Fact]
    public void UpsertRing_replaces_by_id_ignoring_case()
    {
        var s = new TriggerStore(TempFile());
        s.UpsertRing(Ring());
        s.UpsertRing(Ring() with { Id = "MINE8", Name = "renamed" });

        var r = Assert.Single(s.Rings);
        Assert.Equal("renamed", r.Name);
    }

    [Fact]
    public void Upsert_adds_then_replaces_by_id()
    {
        var s = new TriggerStore(TempFile());
        var id = Guid.NewGuid();
        s.Upsert(Spot(0, id, "first"));
        s.Upsert(Spot(0, id, "second"));

        var t = Assert.Single(s.All);
        Assert.Equal("second", t.Name);
    }
}
