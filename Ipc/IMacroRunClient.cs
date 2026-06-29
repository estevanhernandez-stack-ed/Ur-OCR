// Ipc/IMacroRunClient.cs
namespace RoRoRo.UrOcr.Ipc;

public interface IMacroRunClient
{
    Task<RunMacroResponse> RunAsync(string macroId, IReadOnlyList<string>? targets, CancellationToken ct);
}
