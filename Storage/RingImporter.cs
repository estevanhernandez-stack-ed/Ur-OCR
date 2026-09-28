using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RoRoRo.UrOcr.Storage;

public sealed record RingImportResult(RingDefinition Ring, IReadOnlyList<Trigger> Triggers);

/// <summary>
/// Turns a measured ring into its ring definition and ten triggers: eight spots
/// (none-of checks against the ring's current layer, each running its "Mine spot"
/// macro), the rock cap (same layer, runs Go to Top) and the camera rule (no layer,
/// runs Camera top-down). Ids are derived from ring and role, so a re-import
/// replaces the same ten triggers.
/// </summary>
public static class RingImporter
{
    public static RingImportResult Build(MeasuredRing m, IReadOnlyList<UrTaskMacro> macros)
    {
        if (m.Validate() is { } problem) throw new InvalidDataException(problem);

        var ring = new RingDefinition(m.RingId, m.Name,
            m.Layers.Select(l => new LayerDefinition(l.Name, l.Rock.ToList())).ToList(), m.MinLayerSpots,
            m.Finders?.Select(f => f.ToSetup(m.RecordedClientW, m.RecordedClientH)).ToList(), m.LayerMinShare, m.LayerLead);

        // The capture region reaches far enough around the point to hold the whole box.
        var reach = Math.Max(1, new[] { -m.Box.OffsetX, -m.Box.OffsetY, m.Box.OffsetX + m.Box.W, m.Box.OffsetY + m.Box.H }.Max());

        Trigger Base(string role, string label, RegionRect region, string macro, int cooldownMs) => new()
        {
            Id = StableId(m.RingId, role),
            Name = $"{m.Name}: {label}",
            Enabled = true,
            Region = region,
            Mode = TriggerMode.Color,
            AccountAware = true,
            Keybind = new KeyCombo("F13", Array.Empty<string>()),   // never pressed: these run macros
            CoordSpace = Trigger.CoordSpaceClient,
            RecordedClientW = m.RecordedClientW,
            RecordedClientH = m.RecordedClientH,
            Action = TriggerAction.RunMacro,
            MacroId = ResolveMacro(macros, macro),
            CooldownMs = cooldownMs,
            FirstFireConfirmed = true,                              // agent-authored: no first-fire toast
        };

        var triggers = new List<Trigger>();
        foreach (var s in m.Spots.OrderBy(x => x.Order))
        {
            var t = Base("spot-" + s.Name, s.Name,
                new RegionRect(s.X - reach, s.Y - reach, 2 * reach + 1, 2 * reach + 1), s.Macro, m.SpotCooldownMs);
            t.Color = new ColorCriteria(new Rgb(0, 0, 0), m.ToleranceRgb, ColorSamplingMode.SinglePixel,
                Point: new PickPoint(reach, reach), Box: m.Box, NoneOf: m.Ignore.ToList());
            t.Ring = new RingSpot(m.RingId, s.Order);
            triggers.Add(t);
        }

        var whole = new RegionRect(0, 0, m.RecordedClientW, m.RecordedClientH);
        var cap = Base("rock-cap", "rock cap", whole, m.RockCap.Macro, m.RockCap.CooldownMs);
        cap.Mode = TriggerMode.Layer;
        cap.Layer = new LayerCriteria(m.RingId, LayerCondition.SameLayer);
        cap.HoldForMs = m.RockCap.HoldForMs;
        triggers.Add(cap);

        var camera = Base("camera", "camera top-down", whole, m.Camera.Macro, m.Camera.CooldownMs);
        camera.Mode = TriggerMode.Layer;
        camera.Layer = new LayerCriteria(m.RingId, LayerCondition.NoLayer);
        camera.HoldForMs = m.Camera.HoldForMs;
        triggers.Add(camera);

        foreach (var t in triggers)
            if (TriggerValidation.Validate(t, new[] { ring }) is { } p) throw new InvalidDataException($"{t.Name}: {p}");
        return new RingImportResult(ring, triggers);
    }

    /// <summary>Builds first, so a bad file or a missing macro writes nothing.</summary>
    public static RingImportResult Apply(TriggerStore store, MeasuredRing m, IReadOnlyList<UrTaskMacro> macros)
    {
        var result = Build(m, macros);
        store.UpsertRing(result.Ring);
        foreach (var t in result.Triggers) store.Upsert(t);
        return result;
    }

    /// <summary>A macro by name (exactly one), else by id. Ur Task's RunMacro takes the id.</summary>
    public static string ResolveMacro(IReadOnlyList<UrTaskMacro> macros, string nameOrId)
    {
        var byName = macros.Where(x => string.Equals(x.Name, nameOrId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byName.Count > 1)
            throw new InvalidDataException($"{byName.Count} Ur Task macros are named \"{nameOrId}\". Rename or delete the extras.");
        if (byName.Count == 1) return byName[0].Id;
        var byId = macros.FirstOrDefault(x => string.Equals(x.Id, nameOrId, StringComparison.OrdinalIgnoreCase));
        return byId?.Id ?? throw new InvalidDataException(
            $"No Ur Task macro is named \"{nameOrId}\". Generate the ore-stop macros in Ur Task first.");
    }

    /// <summary>The same ring and role always give the same id.</summary>
    public static Guid StableId(string ringId, string role)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(
            $"626labs.ur-ocr/ring/{ringId.ToLowerInvariant()}/{role.ToLowerInvariant()}"));
        return new Guid(bytes);
    }
}
