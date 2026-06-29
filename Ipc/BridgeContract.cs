// Ipc/BridgeContract.cs
using System.Text.Json;
using System.Text.Json.Serialization;
namespace RoRoRo.UrOcr.Ipc;

public sealed record RunMacroRequest(string ContractVersion, string Method, string MacroId,
    IReadOnlyList<string>? Targets, int? InterAltDelayMs, string? CallerPluginId);

public sealed record RunMacroResponse(bool Ok, string? PlaybackId, bool Queued, string? Reason, string? Detail);

public static class BridgeContract
{
    public const string PipeName = "626labs-ur-task";
    public const string Method = "RunMacro";
    public const string CallerId = "626labs.ur-ocr";
    public const string ContractVersion = "1.0";

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static RunMacroRequest ForMacro(string macroId, IReadOnlyList<string>? targets)
        => new(ContractVersion, Method, macroId,
               targets is { Count: > 0 } ? targets : new[] { "foreground" }, null, CallerId);
}
