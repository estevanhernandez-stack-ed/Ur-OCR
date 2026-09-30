using System.IO;

namespace RoRoRo.UrOcr.Storage;

/// <summary>
/// Turns a pulse file into stored pulses: the Ur Task macro names (PulseMacroNames) resolved to ids,
/// and every entry validated against the rings and spot triggers already imported. Builds
/// everything first, so a bad entry or a missing macro writes nothing.
/// </summary>
public static class PulseImporter
{
    public static IReadOnlyList<PulseConfig> Build(PulseFile file, IReadOnlyList<RingDefinition> rings,
        IReadOnlyList<Trigger> triggers, IReadOnlyList<UrTaskMacro> macros)
    {
        if (file.Schema != PulseFile.CurrentSchema)
            throw new InvalidDataException($"schema must be {PulseFile.CurrentSchema}, not {file.Schema}.");
        if (file.Pulses is null || file.Pulses.Count == 0)
            throw new InvalidDataException("pulses is empty: list one entry per account.");
        if (file.Pulses.Any(p => p is null))
            throw new InvalidDataException("pulses has an empty entry.");
        var twice = file.Pulses.GroupBy(p => p.AccountUserId).FirstOrDefault(g => g.Count() > 1);
        if (twice is not null)
            throw new InvalidDataException($"Account {twice.Key} is listed twice: one pulse per account.");

        var resolved = Resolve(macros);
        var result = new List<PulseConfig>();
        foreach (var entry in file.Pulses)
        {
            var pulse = entry with { Macros = resolved };
            if (PulseValidation.Validate(pulse, rings, triggers) is { } problem)
                throw new InvalidDataException($"Account {entry.AccountUserId}: {problem}");
            if ((MissingUsable(pulse.Usables?.Ride, "ride", macros) ?? MissingUsable(pulse.Usables?.Target, "target", macros)) is { } missing)
                throw new InvalidDataException($"Account {entry.AccountUserId}: {missing}");
            result.Add(pulse);
        }
        return result;
    }

    public static IReadOnlyList<PulseConfig> Apply(TriggerStore store, PulseFile file, IReadOnlyList<UrTaskMacro> macros)
    {
        var pulses = Build(file, store.Rings, store.All, macros);
        foreach (var p in pulses) store.UpsertPulse(p);
        return pulses;
    }

    /// <summary>A usable names its macro by id (the file's own, unlike the looked-up ones): it must be one
    /// Ur Task has, or every fire would be refused and skipped.</summary>
    private static string? MissingUsable(PulseUsable? u, string key, IReadOnlyList<UrTaskMacro> macros) =>
        u is null || macros.Any(m => string.Equals(m.Id, u.Macro, StringComparison.OrdinalIgnoreCase))
            ? null
            : $"usables.{key}.macro {u.Macro} is not one of Ur Task's macros: install it in Ur Task first.";

    /// <summary>Every macro the loop runs, by name, exactly one each (RingImporter.ResolveMacro rules).
    /// The camera turn and the camera top-down are optional: missing is null, two of a name are refused.</summary>
    public static PulseMacros Resolve(IReadOnlyList<UrTaskMacro> macros) => new(
        RingImporter.ResolveMacro(macros, PulseMacroNames.AutoMineOff),
        RingImporter.ResolveMacro(macros, PulseMacroNames.AutoMineOn),
        RingImporter.ResolveMacro(macros, PulseMacroNames.GoToTop),
        MeasuredRing.RingOrder.Select(n => RingImporter.ResolveMacro(macros, PulseMacroNames.Clear(n))).ToList(),
        macros.Any(x => string.Equals(x.Name, PulseMacroNames.CameraTurnLeft, StringComparison.OrdinalIgnoreCase))
            ? RingImporter.ResolveMacro(macros, PulseMacroNames.CameraTurnLeft)
            : null,
        Optional(macros, PulseMacroNames.CameraTopDown));

    private static string? Optional(IReadOnlyList<UrTaskMacro> macros, string name) =>
        macros.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
            ? RingImporter.ResolveMacro(macros, name)
            : null;
}
