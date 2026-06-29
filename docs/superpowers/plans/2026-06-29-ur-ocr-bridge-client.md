# Ur-OCR Bridge Client — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Let an Ur-OCR trigger **fire a Ur Task macro** (instead of only a keybind) by sending a `RunMacro` request over the named pipe the Ur Task action-bridge server already speaks. This closes the perception→action loop: a color trigger detects a game event → runs a specific Ur Task macro on the foreground alt, natively and account-safe.

**Architecture:** A trigger gains an `Action` discriminator (`KeyChord | RunMacro`). On a match, the coordinator either presses the keybind (today's path, unchanged) or hands the trigger to an injected macro-firer that opens `\\.\pipe\626labs-ur-task`, sends a length-prefixed JSON `RunMacro`, and reads the ack. The wire format mirrors the server (`rororo-ur-task` `src/Ipc`) exactly — JSON, both sides owned.

**Tech Stack:** C#/.NET 10 WPF, `System.IO.Pipes`, `System.Text.Json`, xUnit. Design basis: `rororo-ur-task/docs/v0.3-ur-ocr-bridge.md` (the A3 contract) + `docs/superpowers/specs/2026-06-29-ur-ocr-action-bridge-design.md` §5.

## Design decisions (locked for v1)

- **Discriminator-branch, not a full `IFireAction` refactor.** The bridge spec floated `IFireAction { KeyChord | RunMacro }`. We instead add a `Trigger.Action` enum + an injected `IMacroRunFirer` and branch in the coordinator's fire path. Rationale: lowest blast radius on the *working* keybind fire path + the dry-run branch already there; only two action types, so a polymorphic seam isn't paying for itself yet. If a third action type appears, refactor to `IFireAction` then.
- **Targets = foreground alt** for v1 (`["foreground"]` sentinel — the smart default the server already supports). A multi-alt target picker is a later nicety; the meter/trigger is single-active-window anyway.
- **Capability string is deferred.** `plugins.send-run-requests` needs the ROROROblox host to recognize it (per the merged decision). The named pipe is same-user/same-machine and works WITHOUT a declared capability, so v1 ships functional; the manifest capability lands when the host coordinates it. Do NOT add it to the manifest in this plan.

## Global Constraints

- **Wire format (must match the server byte-for-byte):** 4-byte **big-endian** length prefix, then UTF-8 JSON. 64 KB cap.
- **Request JSON (camelCase, omit nulls):** `{ "contractVersion":"1.0", "method":"RunMacro", "macroId":"<guid>", "targets":["foreground"], "interAltDelayMs":null, "callerPluginId":"626labs.ur-ocr" }`.
- **Response:** `{ ok, playbackId, queued, reason, detail }`. `ok:false` reasons: `busy | unknown-macro | no-targets-resolved | refused | version-mismatch`.
- **Pipe:** `NamedPipeClientStream(".", "626labs-ur-task", PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly)`. Connect with a short timeout (2000ms). Ur Task not running ⇒ connect times out ⇒ treat as a clean "blocked: ur-task-not-running", log it, do NOT throw out of the tick.
- **`callerPluginId` = `"626labs.ur-ocr"`.**
- Namespaces: `RoRoRo.UrOcr.Ipc` (new), `RoRoRo.UrOcr.Storage`, `RoRoRo.UrOcr.Engine`, `RoRoRo.UrOcr.UI`.
- **Build-lock:** redirect test output `-o "C:/Users/estev/AppData/Local/Temp/urocr-sdd/cN"`. Unit test project is standalone (no ROROROblox).
- Tasks 1-4 are TDD + unit-verified. Tasks 5-6 are WPF UI: compile-verified; runtime needs a human run (Ur Task must be running for a true end-to-end).

---

### Task 1: Trigger action discriminator + macro fields

**Files:** Modify `Storage/Trigger.cs`; Test `tests/RoRoRo.UrOcr.Tests/Storage/TriggerActionTests.cs`

**Interfaces — Produces:** `enum TriggerAction { KeyChord, RunMacro }`; `Trigger.Action` (default `KeyChord`), `Trigger.MacroId` (string?), `Trigger.MacroTargets` (IReadOnlyList<string>? — null ⇒ foreground).

- [ ] **Step 1: Failing test**

```csharp
// tests/RoRoRo.UrOcr.Tests/Storage/TriggerActionTests.cs
using System.Text.Json;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Storage;

public class TriggerActionTests
{
    private static Trigger NewColorTrigger() => new()
    {
        Id = Guid.NewGuid(), Name = "t", Region = new RegionRect(0, 0, 4, 4),
        Mode = TriggerMode.Color, Color = new ColorCriteria(new Rgb(1, 2, 3), 10, ColorSamplingMode.SinglePixel),
        Keybind = new KeyCombo("F", Array.Empty<string>()),
    };

    [Fact]
    public void Action_DefaultsToKeyChord()
        => Assert.Equal(TriggerAction.KeyChord, NewColorTrigger().Action);

    [Fact]
    public void RunMacroFields_RoundTrip_CamelCase()
    {
        var t = NewColorTrigger();
        t.Action = TriggerAction.RunMacro;
        t.MacroId = "f4e5d6c7-0000-0000-0000-000000000000";

        var json = JsonSerializer.Serialize(t, TriggerJsonOptions.Default);
        Assert.Contains("\"action\": \"runMacro\"", json);
        Assert.Contains("\"macroId\": \"f4e5d6c7", json);

        var back = JsonSerializer.Deserialize<Trigger>(json, TriggerJsonOptions.Default)!;
        Assert.Equal(TriggerAction.RunMacro, back.Action);
        Assert.Equal(t.MacroId, back.MacroId);
    }

    [Fact]
    public void LegacyTrigger_NoActionField_DeserializesAsKeyChord()
    {
        // A trigger written before this change has no "action" key.
        var legacy = "{\"id\":\"" + Guid.NewGuid() + "\",\"name\":\"t\",\"enabled\":true," +
                     "\"region\":{\"x\":0,\"y\":0,\"width\":4,\"height\":4},\"mode\":\"color\"," +
                     "\"color\":{\"targetRgb\":{\"r\":1,\"g\":2,\"b\":3},\"toleranceRgb\":10,\"samplingMode\":\"singlePixel\"}," +
                     "\"keybind\":{\"key\":\"F\",\"modifiers\":[]},\"cooldownMs\":2000}";
        var t = JsonSerializer.Deserialize<Trigger>(legacy, TriggerJsonOptions.Default)!;
        Assert.Equal(TriggerAction.KeyChord, t.Action);
    }
}
```

- [ ] **Step 2: Run → FAIL** (`--filter "FullyQualifiedName~TriggerActionTests" -o ...c1`)

- [ ] **Step 3: Implement** — in `Storage/Trigger.cs`, add the enum next to the others (`TriggerMode`, etc.) and the three properties to `Trigger` (after `Keybind`, before `CooldownMs`):

```csharp
public enum TriggerAction { KeyChord, RunMacro }
```
```csharp
    // Fire action: press the keybind (default, legacy) or run a Ur Task macro
    // via the action bridge. Additive — legacy triggers with no "action" key
    // deserialize as KeyChord (System.Text.Json leaves the default).
    public TriggerAction Action { get; set; } = TriggerAction.KeyChord;
    public string? MacroId { get; set; }
    public IReadOnlyList<string>? MacroTargets { get; set; }   // null ⇒ foreground alt
```

(The existing `TriggerJsonOptions.Default` already has `JsonStringEnumConverter(camelCase)`, so `RunMacro` ⇒ `"runMacro"`. No migrator needed — `SchemaVersion` stays 1.)

- [ ] **Step 4: Run → PASS** (3/3). - [ ] **Step 5: Commit** `feat(storage): Trigger.Action discriminator + macro fields (additive)`

---

### Task 2: Bridge wire contract + frame codec (client side)

**Files:** Create `Ipc/FrameCodec.cs`, `Ipc/BridgeContract.cs`; Test `tests/RoRoRo.UrOcr.Tests/Ipc/BridgeContractTests.cs`

**Interfaces — Produces:** `RunMacroRequest(string ContractVersion, string Method, string MacroId, IReadOnlyList<string>? Targets, int? InterAltDelayMs, string? CallerPluginId)`; `RunMacroResponse(bool Ok, string? PlaybackId, bool Queued, string? Reason, string? Detail)`; `static BridgeContract { JsonSerializerOptions Json; const string PipeName="626labs-ur-task"; const string Method="RunMacro"; const string CallerId="626labs.ur-ocr"; RunMacroRequest ForMacro(string macroId, IReadOnlyList<string>? targets) }`; `static FrameCodec { Task WriteFrameAsync(Stream, ReadOnlyMemory<byte>, ct); Task<byte[]?> ReadFrameAsync(Stream, ct) }`.

These MIRROR the server's `rororo-ur-task/src/Ipc/FrameCodec.cs` + `BridgeContract.cs`. Copy that shape (4-byte big-endian length prefix, 64 KB cap, camelCase + ignore-null JSON). The implementer should open the server files for reference if available, else use the code below.

- [ ] **Step 1: Failing test** — round-trip a `RunMacroRequest` (assert camelCase keys `contractVersion`/`macroId`/`callerPluginId`), and a frame round-trip via `MemoryStream` (write then read returns the same bytes; empty stream returns null).

```csharp
// tests/RoRoRo.UrOcr.Tests/Ipc/BridgeContractTests.cs
using System.IO;
using System.Text;
using System.Text.Json;
using RoRoRo.UrOcr.Ipc;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Ipc;

public class BridgeContractTests
{
    [Fact]
    public void ForMacro_BuildsValidRequest_CamelCase()
    {
        var req = BridgeContract.ForMacro("f4e5d6c7-0000-0000-0000-000000000000", null);
        Assert.Equal("1.0", req.ContractVersion);
        Assert.Equal("RunMacro", req.Method);
        Assert.Equal("626labs.ur-ocr", req.CallerPluginId);
        Assert.Equal(new[] { "foreground" }, req.Targets);  // null targets ⇒ ["foreground"]
        var json = JsonSerializer.Serialize(req, BridgeContract.Json);
        Assert.Contains("\"contractVersion\":\"1.0\"", json);
        Assert.Contains("\"callerPluginId\":\"626labs.ur-ocr\"", json);
    }

    [Fact]
    public async Task Frame_RoundTrips()
    {
        var payload = Encoding.UTF8.GetBytes("{\"x\":1}");
        using var ms = new MemoryStream();
        await FrameCodec.WriteFrameAsync(ms, payload, default);
        ms.Position = 0;
        Assert.Equal(payload, await FrameCodec.ReadFrameAsync(ms, default));
    }

    [Fact]
    public async Task ReadFrame_EmptyStream_ReturnsNull()
    {
        using var ms = new MemoryStream();
        Assert.Null(await FrameCodec.ReadFrameAsync(ms, default));
    }
}
```

- [ ] **Step 2: Run → FAIL.**
- [ ] **Step 3: Implement** —

```csharp
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
```
```csharp
// Ipc/FrameCodec.cs
using System.Buffers.Binary;
using System.IO;
namespace RoRoRo.UrOcr.Ipc;

internal static class FrameCodec
{
    public const int MaxFrameBytes = 64 * 1024;

    public static async Task WriteFrameAsync(Stream s, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        if (payload.Length > MaxFrameBytes) throw new InvalidDataException($"Frame too large: {payload.Length}.");
        var len = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, payload.Length);
        await s.WriteAsync(len, ct).ConfigureAwait(false);
        await s.WriteAsync(payload, ct).ConfigureAwait(false);
        await s.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async Task<byte[]?> ReadFrameAsync(Stream s, CancellationToken ct)
    {
        var lenBuf = await ReadExactAsync(s, 4, ct).ConfigureAwait(false);
        if (lenBuf is null) return null;
        int len = BinaryPrimitives.ReadInt32BigEndian(lenBuf);
        if (len < 0 || len > MaxFrameBytes) throw new InvalidDataException($"Bad frame length: {len}.");
        var payload = await ReadExactAsync(s, len, ct).ConfigureAwait(false);
        if (payload is null) throw new EndOfStreamException("Truncated frame.");
        return payload;
    }

    private static async Task<byte[]?> ReadExactAsync(Stream s, int count, CancellationToken ct)
    {
        if (count == 0) return Array.Empty<byte>();
        var buf = new byte[count]; int read = 0;
        while (read < count)
        {
            int n = await s.ReadAsync(buf.AsMemory(read, count - read), ct).ConfigureAwait(false);
            if (n == 0) return read == 0 ? null : throw new EndOfStreamException();
            read += n;
        }
        return buf;
    }
}
```

- [ ] **Step 4: Run → PASS** (3/3). - [ ] **Step 5: Commit** `feat(ipc): RunMacro client contract + frame codec (mirrors server)`

---

### Task 3: MacroRunClient — the named-pipe client

**Files:** Create `Ipc/IMacroRunClient.cs`, `Ipc/MacroRunClient.cs`; Test `tests/RoRoRo.UrOcr.Tests/Ipc/MacroRunClientTests.cs`

**Interfaces — Produces:** `interface IMacroRunClient { Task<RunMacroResponse> RunAsync(string macroId, IReadOnlyList<string>? targets, CancellationToken ct); }`; `class MacroRunClient : IMacroRunClient`. On call: connect to the pipe (2s timeout) → write the `RunMacro` frame → read the response frame → return it. On connect timeout / IO failure, return `RunMacroResponse(false, null, false, "ur-task-not-running", "<detail>")` (a synthetic refusal, never throws).

The client opens a fresh connection per call and closes it (matches the server's one-request-per-connection model). For testability, factor the pipe-open behind a `Func<CancellationToken, Task<Stream?>>` so a test injects an in-process pipe; the production path opens the real `NamedPipeClientStream`.

- [ ] **Step 1: Failing test** — drive `RunAsync` over an in-process `NamedPipeServerStream`/`Client` pair where a fake "server" reads the request frame, asserts it deserializes to a `RunMacro` with the right macroId + callerId, and writes back an `Accepted` response; assert the client returns `Ok==true` with the playbackId. Plus: a "no server" case returns `Ok==false, Reason=="ur-task-not-running"`.

```csharp
// tests/RoRoRo.UrOcr.Tests/Ipc/MacroRunClientTests.cs  (sketch — write concretely)
// - Build the client with an injected stream-opener that returns a connected
//   NamedPipeClientStream paired to a NamedPipeServerStream (unique name).
// - On a background task, the fake server: ReadFrameAsync -> deserialize
//   RunMacroRequest -> Assert macroId + Method=="RunMacro" + CallerPluginId
//   -> WriteFrameAsync(serialize Accepted("01OK")).
// - Assert client.RunAsync(macroId, null, ct) returns Ok==true, PlaybackId=="01OK".
// - Second test: opener returns null (connect failed) -> RunAsync returns
//   Ok==false, Reason=="ur-task-not-running", and does NOT throw.
```

- [ ] **Step 2: Run → FAIL.**
- [ ] **Step 3: Implement** —

```csharp
// Ipc/IMacroRunClient.cs
namespace RoRoRo.UrOcr.Ipc;
public interface IMacroRunClient
{
    Task<RunMacroResponse> RunAsync(string macroId, IReadOnlyList<string>? targets, CancellationToken ct);
}
```
```csharp
// Ipc/MacroRunClient.cs
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
namespace RoRoRo.UrOcr.Ipc;

public sealed class MacroRunClient : IMacroRunClient
{
    private const int ConnectTimeoutMs = 2000;
    private readonly Func<CancellationToken, Task<Stream?>> _openPipe;

    public MacroRunClient() : this(DefaultOpenAsync) { }
    internal MacroRunClient(Func<CancellationToken, Task<Stream?>> openPipe) => _openPipe = openPipe;

    public async Task<RunMacroResponse> RunAsync(string macroId, IReadOnlyList<string>? targets, CancellationToken ct)
    {
        Stream? pipe = null;
        try
        {
            pipe = await _openPipe(ct).ConfigureAwait(false);
            if (pipe is null)
                return new RunMacroResponse(false, null, false, "ur-task-not-running", "Ur Task is not running or refused the connection.");

            var reqBytes = JsonSerializer.SerializeToUtf8Bytes(BridgeContract.ForMacro(macroId, targets), BridgeContract.Json);
            await FrameCodec.WriteFrameAsync(pipe, reqBytes, ct).ConfigureAwait(false);
            var respBytes = await FrameCodec.ReadFrameAsync(pipe, ct).ConfigureAwait(false);
            if (respBytes is null)
                return new RunMacroResponse(false, null, false, "refused", "Ur Task closed the connection without a response.");
            return JsonSerializer.Deserialize<RunMacroResponse>(respBytes, BridgeContract.Json)
                   ?? new RunMacroResponse(false, null, false, "refused", "Empty response.");
        }
        catch (Exception ex)
        {
            return new RunMacroResponse(false, null, false, "ur-task-not-running", ex.Message);
        }
        finally { if (pipe is not null) await pipe.DisposeAsync().ConfigureAwait(false); }
    }

    private static async Task<Stream?> DefaultOpenAsync(CancellationToken ct)
    {
        var pipe = new NamedPipeClientStream(".", BridgeContract.PipeName,
            PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try { await pipe.ConnectAsync(ConnectTimeoutMs, ct).ConfigureAwait(false); return pipe; }
        catch { await pipe.DisposeAsync().ConfigureAwait(false); return null; }
    }
}
```

- [ ] **Step 4: Run → PASS.** - [ ] **Step 5: Commit** `feat(ipc): MacroRunClient — named-pipe RunMacro client with graceful not-running`

---

### Task 4: Coordinator fires the macro action

**Files:** Modify `Engine/TriggerCoordinator.cs`; Test `tests/RoRoRo.UrOcr.Tests/Engine/FireMacroTests.cs`

**Interfaces — Consumes:** `IMacroRunClient` (Task 3), `Trigger.Action`/`MacroId`/`MacroTargets` (Task 1). The coordinator gains a constructor param `IMacroRunClient? macroClient = null` (optional/last so existing tests still compile) and fires it for `RunMacro` triggers.

- [ ] **Step 1: Failing tests** — reuse the existing coordinator fakes (read `TriggerCoordinatorTests.cs`/`DryRunTests.cs`). Add a fake `IMacroRunClient` recording calls. Facts:
  - A `RunMacro` trigger that matches (not dry-run) ⇒ `macroClient.RunAsync(macroId, ...)` called once, `keys.Press` NOT called, log `Fired`.
  - A `KeyChord` trigger that matches ⇒ `keys.Press` called, `macroClient.RunAsync` NOT called (unchanged path).
  - Dry-run ⇒ neither client nor keys called, log `WouldFire` (both action types).

- [ ] **Step 2: Run → FAIL.**
- [ ] **Step 3: Implement** — add the optional ctor param `IMacroRunClient? macroClient = null` to the primary constructor's parameter list (after `onFirstFire`). In the fire branch (the `if (matched && !was && cooldownReady)` block), restructure to:

```csharp
            if (matched && !was && cooldownReady)
            {
                if (DryRun)
                {
                    log.Record(trig.Id, trig.Name, ActivityKind.WouldFire,
                        detail.Length > 0 ? $"OCR: {detail}" : null);
                }
                else if (trig.Action == TriggerAction.RunMacro && macroClient is not null && trig.MacroId is not null)
                {
                    var resp = await macroClient.RunAsync(trig.MacroId, trig.MacroTargets, ct).ConfigureAwait(false);
                    store.MarkFired(trig.Id, now);
                    log.Record(trig.Id, trig.Name, ActivityKind.Fired,
                        resp.Ok ? $"macro {trig.MacroId}" : $"macro refused: {resp.Reason}");
                    if (!trig.FirstFireConfirmed) onFirstFire?.Invoke(trig);
                }
                else
                {
                    keys.Press(trig.Keybind);
                    store.MarkFired(trig.Id, now);
                    log.Record(trig.Id, trig.Name, ActivityKind.Fired,
                        detail.Length > 0 ? $"OCR: {detail}" : null);
                    if (!trig.FirstFireConfirmed) onFirstFire?.Invoke(trig);
                }
            }
```

(Note: a `RunMacro` trigger whose `macroClient`/`MacroId` is null falls through to the keybind path — harmless; the UI ensures a macro is picked. The `await` is fine — `TickOnceAsync` is already async.)

- [ ] **Step 4: Run → PASS** (new + `TriggerCoordinatorTests` + `DryRunTests` regression). - [ ] **Step 5: Commit** `feat(engine): coordinator fires a Ur Task macro for RunMacro triggers`

---

### Task 5: Macro discovery + trigger-editor action UI

**Files:** Create `Storage/UrTaskMacros.cs` (reader); Modify `UI/TriggerEditView.xaml` + the edit VM (`UI/TriggerRowViewModel.cs`). No unit test (WPF + filesystem); compile-verify + a small reader test if cheap.

- [ ] **Step 1: Macro reader** — `Storage/UrTaskMacros.cs`: reads `%LOCALAPPDATA%\626Labs\RoRoRoUrTask\macros\*.json`, deserializes each to `{ string id, string name }` (camelCase), returns `IReadOnlyList<UrTaskMacro>` (record `UrTaskMacro(string Id, string Name)`), swallowing unreadable files. Path: `Path.Combine(Environment.GetFolderPath(SpecialFolder.LocalApplicationData), "626Labs", "RoRoRoUrTask", "macros")`. A focused unit test against a temp dir with a sample macro json is cheap — add it.

- [ ] **Step 2: Editor UI** — in `UI/TriggerEditView.xaml`, replace the static `KEYBIND` block (lines 35-39) with an **action selector** (two RadioButtons or a ComboBox: "Press keybind" / "Run Ur Task macro", bound to `Source.Action`). When KeyChord ⇒ show the existing `KeybindCapture`. When RunMacro ⇒ show a **macro picker** `ComboBox` (ItemsSource = the macros from the reader, SelectedValue ⇒ `Source.MacroId`, DisplayMemberPath = Name). Use `Visibility` bound to the action so only the relevant control shows. The edit VM (`TriggerRowViewModel`) exposes the macro list + the selected action; read `TriggerRowViewModel.cs` and follow its INPC pattern. Refreshing the macro list on editor-open is enough (no live watch).

- [ ] **Step 3: Build-verify** `dotnet build rororo-ur-ocr.csproj -o ".../c5"` → 0 errors. - [ ] **Step 4: Commit** `feat(ui): trigger action selector + Ur Task macro picker`. Report: compile-verified; live behavior needs a human run.

---

### Task 6: Wire the client into the runtime

**Files:** Modify `PluginRuntime.cs`

- [ ] **Step 1:** Add `public Ipc.MacroRunClient MacroClient { get; } = new();` and pass it into the `TriggerCoordinator` constructor (line 47-52) as the new `macroClient` argument.
- [ ] **Step 2:** The `onFirstFire` toast at line 49 reads `t.Keybind.Key` — guard it for macro triggers so it doesn't read a keybind a macro trigger may not care about: `onFirstFire: t => Toasts.Show(t.Action == Storage.TriggerAction.RunMacro ? $"✓ \"{t.Name}\" ran a macro" : $"✓ \"{t.Name}\" fired ({t.Keybind.Key})")`.
- [ ] **Step 3: Build-verify** `dotnet build ... -o ".../c6"` → 0 errors. - [ ] **Step 4: Commit** `feat(ipc): wire MacroRunClient into the coordinator`. Report: compile-verified.

---

## Out of scope (own follow-ups)

- **Manifest capability** `plugins.send-run-requests` — host-coordinated; ships when ROROROblox recognizes it. Pipe works without it.
- Multi-alt target picker (v1 fires on the foreground alt).
- The bridge-side decision log items already cover the rororo-ur-task server (merged) + the SequencePlayer guard (PR #7).

## Self-Review

- **Spec coverage:** bridge spec §5 (widen fire seam → Task 4; Trigger discriminator → Task 1; RunMacroFireAction client → Tasks 2-3; macro picker UI → Task 5; manifest capability → deferred, noted). Wire contract matches the merged server (`src/Ipc`).
- **Placeholders:** Tasks 1-4 carry full code; Task 3's test is a directed sketch (the in-process pipe pattern is established in the server's `MacroRunnerServerTests`); Task 5 directs the implementer to read the edit VM rather than guess.
- **Type consistency:** `IMacroRunClient.RunAsync(string, IReadOnlyList<string>?, CancellationToken)` produced in Task 3, consumed in Tasks 4 + 6; `RunMacroRequest`/`Response` from Task 2 used in Task 3; `Trigger.Action`/`MacroId` from Task 1 used in Tasks 4-5.
