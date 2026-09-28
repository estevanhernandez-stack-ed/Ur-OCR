using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Storage;

public class RingImporterTests
{
    private static int Dx(string n) => n.Contains('E') ? 40 : n.Contains('W') ? -40 : 0;
    private static int Dy(string n) => n.Contains('N') ? -40 : n.Contains('S') ? 40 : 0;

    internal static MeasuredRing Measured() => new(
        Schema: 1, RingId: "mine8", Name: "Mine #8",
        RecordedClientW: 800, RecordedClientH: 599, ScalePercent: 100,
        ToleranceRgb: 30, MinLayerSpots: 3, Box: new SampleBox(),
        Spots: MeasuredRing.RingOrder.Select((n, i) => new MeasuredSpot(i, n, 400 + Dx(n), 300 + Dy(n), $"Mine spot {n}")).ToList(),
        Ignore: new[] { new Rgb(135, 206, 235) },
        Layers: new[]
        {
            new MeasuredLayer("navy", new[] { new Rgb(30, 30, 90) }),
            new MeasuredLayer("grey", new[] { new Rgb(120, 120, 120) }),
        },
        RockCap: new MeasuredAction(300_000, "Go to Top"),
        Camera: new MeasuredAction(10_000, "Camera top-down"));

    internal static IReadOnlyList<UrTaskMacro> Macros() =>
        MeasuredRing.RingOrder.Select(n => new UrTaskMacro($"id-mine-{n}", $"Mine spot {n}"))
            .Append(new UrTaskMacro("id-go-to-top", "Go to Top"))
            .Append(new UrTaskMacro("id-camera", "Camera top-down"))
            .ToList();

    internal static MeasuredFinder Finder(string layer = "grey") => new(layer, Pitch: 50, CenterX: 390, CenterY: 340,
        Outline: new OutlineBox(50, 50), Ore: new[] { new OreColour("cyan crystal", new Rgb(60, 220, 230)) },
        OreToleranceRgb: 40);

    [Fact]
    public void A_measured_file_from_before_the_finder_loads_and_imports_with_none()
    {
        var node = JsonSerializer.SerializeToNode(Measured(), TriggerJsonOptions.Default)!.AsObject();
        node.Remove("finders");
        node.Remove("layerMinShare");
        node.Remove("layerLead");
        var path = Path.Combine(Path.GetTempPath(), "urocr-tests", Guid.NewGuid().ToString("N") + ".measured.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, node.ToJsonString());

        var m = MeasuredRing.Load(path);

        Assert.Null(m.Finders);
        Assert.Null(m.LayerMinShare);
        Assert.Null(m.LayerLead);
        Assert.Null(m.Validate());
        Assert.Null(RingImporter.Build(m, Macros()).Ring.Finders);
    }

    [Fact]
    public void A_finder_imports_onto_the_ring_in_the_recorded_client_size()
    {
        var result = RingImporter.Build(Measured() with { Finders = new[] { Finder() } }, Macros());

        var f = Assert.Single(result.Ring.Finders!);
        Assert.Equal("grey", f.Layer);
        Assert.Equal((800, 599), (f.ClientW, f.ClientH));
        Assert.Equal((50, 390, 340), (f.Pitch, f.CenterX, f.CenterY));
        Assert.Equal(FinderSetup.DefaultRadiusBlocks, f.RadiusBlocks);
        Assert.Equal(new OutlineBox(50, 50, 60, 225), f.Outline);
        Assert.Equal(new OreColour("cyan crystal", new Rgb(60, 220, 230)), Assert.Single(f.Ore));
        Assert.Equal(40, f.OreToleranceRgb);
    }

    [Fact]
    public void Layer_share_settings_import_onto_the_ring_and_default_to_none()
    {
        var set = RingImporter.Build(Measured() with { LayerMinShare = 0.06, LayerLead = 1.8 }, Macros()).Ring;
        var unset = RingImporter.Build(Measured(), Macros()).Ring;

        Assert.Equal((0.06, 1.8), (set.LayerMinShare, set.LayerLead));
        Assert.Null(unset.LayerMinShare);
        Assert.Null(unset.LayerLead);
    }

    [Fact]
    public void A_finder_for_a_layer_the_file_does_not_list_fails_the_import()
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            RingImporter.Build(Measured() with { Finders = new[] { Finder("black") } }, Macros()));

        Assert.Contains("Finder layer black is not one of the layers", ex.Message);
    }

    [Fact]
    public void Two_finders_for_one_layer_fail_the_import()
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            RingImporter.Build(Measured() with { Finders = new[] { Finder(), Finder() } }, Macros()));

        Assert.Contains("Two finders are for layer grey", ex.Message);
    }

    private static TriggerStore TempStore() =>
        new(Path.Combine(Path.GetTempPath(), "urocr-tests", Guid.NewGuid().ToString("N") + ".json"));

    [Fact]
    public void Builds_the_ring_eight_spots_the_rock_cap_and_the_camera_rule()
    {
        var result = RingImporter.Build(Measured(), Macros());

        Assert.Equal("mine8", result.Ring.Id);
        Assert.Equal(3, result.Ring.MinLayerSpots);
        Assert.Equal(new[] { "navy", "grey" }, result.Ring.Layers.Select(l => l.Name));
        Assert.Equal(10, result.Triggers.Count);
        Assert.Equal(8, result.Triggers.Count(t => t.Ring is not null));
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, result.Triggers.Where(t => t.Ring is not null).Select(t => t.Ring!.Order));
    }

    [Fact]
    public void A_spot_samples_the_measured_point_with_the_measured_box()
    {
        var e = RingImporter.Build(Measured(), Macros()).Triggers.Single(t => t.Name == "Mine #8: E");

        Assert.Equal(new RegionRect(437, 297, 7, 7), e.Region);   // (440, 300) with a reach of 3
        Assert.Equal(new PickPoint(3, 3), e.Color!.Point);
        Assert.Equal(new SampleBox(), e.Color.Box);
        Assert.Equal(new[] { new Rgb(135, 206, 235) }, e.Color.NoneOf);
        Assert.Equal(30, e.Color.ToleranceRgb);
        Assert.Equal(new RingSpot("mine8", 2), e.Ring);
        Assert.Equal(Trigger.CoordSpaceClient, e.CoordSpace);
        Assert.Equal(800, e.RecordedClientW);
        Assert.Equal(599, e.RecordedClientH);
        Assert.True(e.AccountAware);
        Assert.Equal(TriggerAction.RunMacro, e.Action);
        Assert.Equal("id-mine-E", e.MacroId);
        Assert.Equal(1000, e.CooldownMs);
    }

    [Fact]
    public void Layer_triggers_carry_the_hold_and_the_macro()
    {
        var triggers = RingImporter.Build(Measured(), Macros()).Triggers;
        var cap = triggers.Single(t => t.Layer?.Condition == LayerCondition.SameLayer);
        var camera = triggers.Single(t => t.Layer?.Condition == LayerCondition.NoLayer);

        Assert.Equal(TriggerMode.Layer, cap.Mode);
        Assert.Equal(300_000, cap.HoldForMs);
        Assert.Equal("id-go-to-top", cap.MacroId);
        Assert.Equal(10_000, camera.HoldForMs);
        Assert.Equal("id-camera", camera.MacroId);
        Assert.Equal("mine8", camera.Layer!.RingId);
    }

    [Fact]
    public void Every_built_trigger_passes_validation()
    {
        var result = RingImporter.Build(Measured(), Macros());

        Assert.All(result.Triggers, t => Assert.Null(TriggerValidation.Validate(t, new[] { result.Ring })));
    }

    [Fact]
    public void Ids_are_stable_across_builds()
    {
        var a = RingImporter.Build(Measured(), Macros()).Triggers.Select(t => t.Id);
        var b = RingImporter.Build(Measured(), Macros()).Triggers.Select(t => t.Id);

        Assert.Equal(a, b);
        Assert.Equal(10, a.Distinct().Count());
    }

    [Fact]
    public void A_reimport_replaces_instead_of_adding()
    {
        var store = TempStore();
        var unrelated = new Trigger
        {
            Id = Guid.NewGuid(), Name = "unrelated", Region = new RegionRect(0, 0, 10, 10), Mode = TriggerMode.Color,
            Color = new ColorCriteria(new Rgb(0, 0, 0), 5, ColorSamplingMode.SinglePixel),
            Keybind = new KeyCombo("A", Array.Empty<string>()),
        };
        store.Add(unrelated);

        RingImporter.Apply(store, Measured(), Macros());
        RingImporter.Apply(store, Measured() with { ToleranceRgb = 25 }, Macros());

        Assert.Equal(11, store.All.Count);
        Assert.Single(store.Rings);
        Assert.All(store.All.Where(t => t.Ring is not null), t => Assert.Equal(25, t.Color!.ToleranceRgb));
    }

    [Fact]
    public void A_missing_macro_names_the_macro_and_writes_nothing()
    {
        var store = TempStore();
        var macros = Macros().Where(m => m.Name != "Go to Top").ToList();

        var ex = Assert.Throws<InvalidDataException>(() => RingImporter.Apply(store, Measured(), macros));

        Assert.Contains("Go to Top", ex.Message);
        Assert.Empty(store.All);
        Assert.Empty(store.Rings);
    }

    [Fact]
    public void Two_macros_with_one_name_is_an_error()
    {
        var macros = Macros().Append(new UrTaskMacro("id-other", "Go to Top")).ToList();

        Assert.Throws<InvalidDataException>(() => RingImporter.Build(Measured(), macros));
    }

    [Fact]
    public void A_macro_can_be_named_by_id()
    {
        var m = Measured() with { RockCap = new MeasuredAction(300_000, "id-go-to-top") };

        var cap = RingImporter.Build(m, Macros()).Triggers.Single(t => t.Layer?.Condition == LayerCondition.SameLayer);

        Assert.Equal("id-go-to-top", cap.MacroId);
    }

    public static TheoryData<string, MeasuredRing> Invalid() => new()
    {
        { "7 spots", Measured() with { Spots = Measured().Spots.Take(7).ToList() } },
        { "125%", Measured() with { ScalePercent = 125 } },
        { "outside", Measured() with { Spots = Measured().Spots.Select(s => s.Order == 0 ? s with { Y = 700 } : s).ToList() } },
        { "misnamed", Measured() with { Spots = Measured().Spots.Select(s => s.Order == 1 ? s with { Name = "E" } : s).ToList() } },
        { "no layers", Measured() with { Layers = Array.Empty<MeasuredLayer>() } },
        { "empty rock", Measured() with { Layers = new[] { new MeasuredLayer("navy", Array.Empty<Rgb>()) } } },
        { "schema 2", Measured() with { Schema = 2 } },
        { "no hold", Measured() with { RockCap = new MeasuredAction(0, "Go to Top") } },
        { "layerMinShare 0", Measured() with { LayerMinShare = 0 } },
        { "layerMinShare over 1", Measured() with { LayerMinShare = 1.5 } },
        { "layerLead under 1", Measured() with { LayerLead = 0.9 } },
        { "duplicate macro", Measured() with { Spots = Measured().Spots
            .Select(s => s.Order == 1 ? s with { Macro = Measured().Spots[0].Macro } : s).ToList() } },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void An_invalid_measured_file_is_refused(string why, MeasuredRing m)
    {
        Assert.False(string.IsNullOrEmpty(why));
        Assert.NotNull(m.Validate());
        Assert.Throws<InvalidDataException>(() => RingImporter.Build(m, Macros()));
    }

    [Fact]
    public void Reads_the_documented_shape()
    {
        var path = Path.Combine(Path.GetTempPath(), "urocr-tests", Guid.NewGuid().ToString("N") + ".measured.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
        {
          "schema": 1,
          "ringId": "mine8",
          "name": "Mine #8",
          "recordedClientW": 800,
          "recordedClientH": 599,
          "scalePercent": 100,
          "toleranceRgb": 30,
          "minLayerSpots": 3,
          "box": { "offsetX": -2, "offsetY": -2, "w": 5, "h": 5 },
          "spots": [
            { "order": 0, "name": "N",  "x": 400, "y": 260, "macro": "Mine spot N" },
            { "order": 1, "name": "NE", "x": 440, "y": 260, "macro": "Mine spot NE" },
            { "order": 2, "name": "E",  "x": 440, "y": 300, "macro": "Mine spot E" },
            { "order": 3, "name": "SE", "x": 440, "y": 340, "macro": "Mine spot SE" },
            { "order": 4, "name": "S",  "x": 400, "y": 340, "macro": "Mine spot S" },
            { "order": 5, "name": "SW", "x": 360, "y": 340, "macro": "Mine spot SW" },
            { "order": 6, "name": "W",  "x": 360, "y": 300, "macro": "Mine spot W" },
            { "order": 7, "name": "NW", "x": 360, "y": 260, "macro": "Mine spot NW" }
          ],
          "ignore": [ { "r": 135, "g": 206, "b": 235 } ],
          "layers": [
            { "name": "navy", "rock": [ { "r": 30, "g": 30, "b": 90 } ] },
            { "name": "grey", "rock": [ { "r": 120, "g": 120, "b": 120 } ] }
          ],
          "rockCap": { "holdForMs": 300000, "macro": "Go to Top", "cooldownMs": 5000 },
          "camera": { "holdForMs": 10000, "macro": "Camera top-down", "cooldownMs": 5000 },
          "spotCooldownMs": 1000
        }
        """);

        var m = MeasuredRing.Load(path);

        Assert.Null(m.Validate());
        Assert.Equal("mine8", m.RingId);
        Assert.Equal(new MeasuredSpot(2, "E", 440, 300, "Mine spot E"), m.Spots[2]);
        Assert.Equal(new SampleBox(-2, -2, 5, 5), m.Box);
        Assert.Equal(300_000, m.RockCap.HoldForMs);
        Assert.Equal(5000, m.Camera.CooldownMs);
        Assert.Equal(new Rgb(120, 120, 120), m.Layers[1].Rock[0]);
    }
}
