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

    /// <summary>Every macro the loop runs, by name, exactly one each (RingImporter.ResolveMacro rules).
    /// The camera turn is optional: missing is null, two of that name are refused.</summary>
    public static PulseMacros Resolve(IReadOnlyList<UrTaskMacro> macros) => new(
        RingImporter.ResolveMacro(macros, PulseMacroNames.AutoMineOff),
        RingImporter.ResolveMacro(macros, PulseMacroNames.AutoMineOn),
        RingImporter.ResolveMacro(macros, PulseMacroNames.GoToTop),
        MeasuredRing.RingOrder.Select(n => RingImporter.ResolveMacro(macros, PulseMacroNames.Clear(n))).ToList(),
        macros.Any(x => string.Equals(x.Name, PulseMacroNames.CameraTurnLeft, StringComparison.OrdinalIgnoreCase))
            ? RingImporter.ResolveMacro(macros, PulseMacroNames.CameraTurnLeft)
            : null);
}
