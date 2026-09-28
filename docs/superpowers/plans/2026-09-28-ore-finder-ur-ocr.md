# Ore Finder (Ur OCR half) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** At the target layer, the pulse loop finds ore-coloured patches and a block grid in the calm frame and sends them to Ur Task as one `ClearAt` call, instead of running the eight "Clear spot" macros.

**Architecture:** Four pieces, all additive. (1) `IMacroRunClient.ClearAtAsync`, Ur Task's new `ClearAt` bridge method; `MacroCall.BeginClearAt` follows the playback it starts through the existing `GetPlayback` path. (2) `FinderSetup`, one per layer, stored on the ring (`RingDefinition.Finders`) and imported from a new top-level `finders` list in the measured file; files without it load unchanged and the pulse falls back to the eight Clear spot macros. (3) `TargetFinder`, pure: ore patches then grid points over a `FramePixels` calm frame, ordered, capped at 64, clipped to the client. `ISpotReader.ReadFrame` captures that frame. (4) `PulseLoop` at the aim layer with a finder: one `ClearAt` per pass; finished counts as cleared, skipped rides a burst.

**Tech Stack:** C# / .NET 10 (`net10.0-windows10.0.19041.0`), WPF, System.Drawing, System.Text.Json, xUnit 2.9.2. PowerShell 7 (`pwsh`) for the live task.

**Spec:** `../rororo-ur-task/docs/superpowers/specs/2026-09-28-ore-stop-pulse-design.md` (binding, sibling repo), sections "Reach, measured, and the ore finder" and "The ClearAt bridge call", as of `92ab19a` (controller rulings on `client` and the extra refusals). The Ur Task half is `../rororo-ur-task/docs/superpowers/plans/2026-09-28-ore-finder-ur-task.md`. This plan only states what it expects from it (Global Constraints, "ClearAt wire contract").

## Global Constraints

- **Paths:** every path in this plan is relative to the Ur-OCR repo root; the sibling repo is `..\rororo-ur-task`. Run every command from the Ur-OCR root and check with `git rev-parse --show-toplevel` after any `cd`. No absolute user-profile path goes into any committed file.
- **Branch:** `feat/ore-stop-pulse`, local only, at `3b9829d` when this plan was written. Never push. Never commit to `feat/ore-stop` or `main`.
- **Build:** `dotnet build rororo-ur-ocr.csproj`. There is no tracked `.sln`; do not create one.
- **Tests:** `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj`. Baseline: **349 passed**. Every task ends with the full suite green. Expected counts: Task 1 **359**, Task 2 **379**, Task 3 **392**, Task 4 **405**. **Known flake:** a test that builds a temp `TriggerStore` can fail once at `File.Move` in `TriggerStore.WriteNow`. Rerun the suite once before debugging; a second failure is real.
- **No new NuGet packages.**
- **Commits:** conventional commits, sentence case after the colon, no emoji. Every message ends with a blank line then a `Co-Authored-By:` trailer naming the model that implemented the task. The commands below use `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`; an implementer on another model writes its own name there.
- **Never `git add`** the untracked `AGENTS.md`, `CLAUDE.md` (GitNexus output) or `.gitnexus/`. Add files by path, never `git add -A` / `git add .`.
- **Fixtures are anonymised.** Account ids in tests are `42`; examples in docs use `123456789`. No real user id, account name or display name in any committed file.
- **Version stays 0.6.0** (unreleased). `rororo-ur-ocr.csproj` and `manifest.json` are not touched. The CHANGELOG entry goes under the existing `## 0.6.0 — unreleased`.
- **ClearAt wire contract** (spec, verbatim; Ur Task bridge 1.x, additive):
  - request `{ "contractVersion": "1.0", "method": "ClearAt", "callerPluginId": "626labs.ur-ocr", "target": "<decimal user id>", "client": { "w": 800, "h": 599 }, "points": [ { "x": 412, "y": 288, "label": "ore 1" }, ... ], "outline": { "w": 50, "h": 50, "minCount": 60, "whiteMin": 225 }, "maxMsPerPoint": null }`. Property order as shown. `maxMsPerPoint` is always written, `null` meaning no time limit (spec decision 5); Ur OCR always sends `null`.
  - `client` is **the client size the points were measured in** (the measured file's `recordedClientW` x `recordedClientH`), never the live size. Ur Task sizes the window to it first, like a recorded macro, then plays points and the box unscaled (ruling 2026-09-28).
  - The outline box is `w` x `h` **centred on the point**: left `x - w / 2`, top `y - h / 2` (integer division), inside the client when `left >= 0 && top >= 0 && left + w <= client.w && top + h <= client.h` (Ur Task's `PointMath.InsideClient` over `CheckBox(-(w / 2), -(h / 2), w, h)`).
  - Ur Task refuses, so Ur OCR validates at import and never sends: 0 or more than **64** points; a point or its box outside `client`; `minCount` below 1 or larger than `w * h`; `whiteMin` outside **1..255** (0 is refused); an outline side over **120**; `maxMsPerPoint` below 1.
  - Response: `RunMacroResponse` (`ok` + `playbackId`, or `ok: false` + `reason` + `detail`). `busy` when another playback runs. An Ur Task without ClearAt answers `{ "ok": false, "reason": "refused", "detail": "Unknown method 'ClearAt'." }`.
  - `GetPlayback` afterwards, unchanged: `finished` (something was pressed), `finished` + reason `skipped` (every point skipped), `stopped`, `failed`. Ur Task logs the playback as `ClearAt (N points)` and each point by its label.
- **Target rules, from the approved spec and the controller's brief:** ore patches sampled every `pitch / 4` px, matched against a named palette within a tolerance, adjacent hits clustered, each cluster's centroid a target; grid points every `pitch` within `radiusBlocks` (default **5**) of the character centre; grid points inside an ore patch dropped; order ore nearest first, then grid nearest first; **cap 64**; clip to the client.
- **Progress rule:** a `ClearAt` that ends `finished` (no `skipped`) counts as cleared progress for the rock cap and the burst decision, exactly as a non-skipped Clear spot does. `finished` + `skipped` means nothing was cleared: ride a burst.
- **Old files still load.** A measured file or `triggers.json` without `finders` loads unchanged and a pulse whose clearing layer has no finder clears with the eight "Clear spot" macros, as in 0.6.0 before this plan.
- **Copy:** sentence case, second person where it addresses the user, no emoji, em-dashes minimal.

## Review Focus

1. **An ore patch or grid point near the window edge** (the character standing near a wall of the view, ore in the corner): its outline box would leave the client and Ur Task would refuse the whole call. Expected: that point is dropped before sending, and the centre itself is checked at import. Pinned by `Points_whose_outline_box_leaves_the_window_are_dropped` (Task 3) and the `centre` case of `A_bad_value_is_named` (Task 2).
2. **More candidates than one call takes** (an ore-rich bowl; radius 5 alone is 81 grid points): expected exactly 64 points, ore first, nearest kept, never a refusal for 65. Pinned by `The_grid_is_capped_at_64_nearest_first` and `Never_more_than_64_points_and_ore_fills_them_first` (Task 3).
3. **A measured file or triggers.json from before the finder, or a finder for a layer the account does not clear on:** loads, and that pulse clears with the eight Clear spot macros. Pinned by `A_measured_file_from_before_the_finder_loads_and_imports_with_none` and `A_ring_without_finders_writes_no_finders_key_and_an_old_file_loads` (Task 2) and `Without_a_finder_for_the_aim_layer_it_clears_the_eight_spots` (Task 4).
4. **An Ur Task that predates ClearAt:** the loop stops with one line quoting `Unknown method 'ClearAt'`, and says Auto Mine is off; no retry loop, no silent switch to Clear spots. Pinned by `An_Ur_Task_without_ClearAt_stops_with_its_reason` (Task 1) and `An_Ur_Task_without_ClearAt_stops_the_loop_and_says_so` (Task 4).
5. **The live window is not the measured size when the calm frame is taken** (125% scale, slack, a resize): the finder samples the frame scaled and still returns points in measured-client pixels, which Ur Task sizes the window to. Pinned by `A_frame_at_another_size_gives_the_same_points` (Task 3).

Also pinned: a lone ore-coloured pixel (a sparkle) is not ore (`A_single_ore_coloured_sample_is_noise`, Task 3); a window that cannot be captured waits and logs once (`A_window_it_cannot_capture_waits_in_Reading_and_logs_once`, Task 4).

## Decisions this plan makes where the spec is open

- **One finder per layer, not one per zone.** The spec says the pitch and box are "measured there, once per zone", but a block is 21 px at the surface and 50 px in the bowl, and accounts clear on different layers (the main on 3, an alt on 1 or 2). So the ring holds a list of finders keyed by layer name, and a pulse uses the one for its aim layer. Most rings will have exactly one.
- **`finders` is a top-level key of the measured file**, not a field inside `layers`: `tools/ring-fit.ps1 -Write` rewrites `layers` wholesale (`Add-Member -Force -NotePropertyName layers`) and would drop anything nested there, but keeps every other key.
- **The measured finder carries no client size;** the importer stamps the file's `recordedClientW` x `recordedClientH` onto the stored `FinderSetup`, which is what `ClearAt` sends as `client`.
- **Finder coordinates are measured-client pixels throughout.** `TargetFinder` maps each sample point into the live frame by scaling, and returns targets in measured pixels.
- **A patch needs at least 2 samples** (`TargetFinder.MinPatchSamples`). At `pitch / 4` a block is about 16 samples, so this only drops single-pixel sparkle. **Clustering is 4-connected.** A vein of adjacent ore blocks is one patch with one centroid; the grid points on its other blocks are dropped (spec rule), so the rest of the vein is found on the next read after a pass that cleared something.
- **The centre grid point is kept.** Top-down it is the block under the character; the outline check decides.
- **An Ur Task without ClearAt stops the loop** with its reason rather than falling back to Clear spots: a silent fallback would hide a version mismatch for the whole session.
- **A window that cannot be captured** keeps the loop in Reading, logged once, like an unreadable ring.
- **The eight Clear spot macros stay required at `--import-pulse`,** finder or not: they are the fallback, and Ur Task 0.11.0 generates them anyway. `PulseImporter` does not change.
- **`PulseRunner` does not change.** It already passes the foreground pid to `TickAsync` and a fresh `inFront` read that `MacroCall` checks right before starting any call, ClearAt included.
- **ClearAt has no `interAltDelayMs`** on the wire (the spec's request has none), so Ur Task's own focus wait applies once per pass. One call replaces eight, so a pass still starts faster than before.

## File map

| File | Status | Responsibility |
| --- | --- | --- |
| `Ipc/BridgeContract.cs` | modify | `ClearAtClient`, `ClearAtPoint`, `ClearAtOutline`, `ClearAtRequest`, `MethodClearAt`, `MaxClearAtPoints`, `ForClearAt` |
| `Ipc/IMacroRunClient.cs` | modify | `ClearAtAsync` (default refusal so existing fakes compile) |
| `Ipc/MacroRunClient.cs` | modify | `ClearAtAsync` over the shared exchange |
| `Engine/MacroCall.cs` | modify | `BeginClearAt`, `ClearAtLabel`; a start delegate replaces the macro id |
| `Storage/FinderSetup.cs` | create | `OreColour`, `OutlineBox`, `FinderSetup` (+ `Validate`, `BoxFits`) |
| `Storage/MeasuredRing.cs` | modify | `MeasuredFinder`, `MeasuredRing.Finders`, their validation |
| `Storage/Ring.cs` | modify | `RingDefinition.Finders` (omitted from JSON when null) |
| `Storage/RingImporter.cs` | modify | copies finders onto the ring with the recorded client size |
| `Storage/PulseValidation.cs` | modify | `FinderFor`; a bad finder on the aim layer fails the pulse |
| `RingImportCommand.cs` | modify | report line naming the finders |
| `Engine/FramePixels.cs` | create | a captured frame as packed RGB, pure |
| `Engine/TargetFinder.cs` | create | `FinderTarget`, `TargetFinder.Find` |
| `Engine/SpotReader.cs` | modify | `ISpotReader.ReadFrame` (default null), `SpotReader.ReadFrame` |
| `Engine/PulseLoop.cs` | modify | the ClearAt clear step |
| `README.md`, `CHANGELOG.md` | modify | ore finder section, 0.6.0 entry |
| `tests/RoRoRo.UrOcr.Tests/Ipc/ClearAtClientTests.cs` | create | wire shape and client behaviour |
| `tests/RoRoRo.UrOcr.Tests/Engine/PulseFakes.cs` | modify | `ScriptedMacros.ClearAtAsync`, `ClearAts`, `ClearAtReplies`, `ClearAtId` |
| `tests/RoRoRo.UrOcr.Tests/Engine/MacroCallTests.cs` | modify | ClearAt calls |
| `tests/RoRoRo.UrOcr.Tests/Storage/FinderSetupTests.cs` | create | every validation rule |
| `tests/RoRoRo.UrOcr.Tests/Storage/RingImporterTests.cs` | modify | old files, finder import, bad finders |
| `tests/RoRoRo.UrOcr.Tests/Storage/RingStorageTests.cs` | modify | round trip, legacy file |
| `tests/RoRoRo.UrOcr.Tests/Storage/PulseValidationTests.cs` | modify | `FinderFor`, bad finders |
| `tests/RoRoRo.UrOcr.Tests/RingImportCommandTests.cs` | modify | the report line |
| `tests/RoRoRo.UrOcr.Tests/Engine/Frames.cs` | create | test frame painting (shared by Tasks 3 and 4) |
| `tests/RoRoRo.UrOcr.Tests/Engine/TargetFinderTests.cs` | create | the finder |
| `tests/RoRoRo.UrOcr.Tests/Engine/FramePixelsTests.cs` | create | bitmap to frame |
| `tests/RoRoRo.UrOcr.Tests/Engine/SpotReaderTests.cs` | modify | `ReadFrame` |
| `tests/RoRoRo.UrOcr.Tests/Engine/PulseLoopFixtures.cs` | modify | `Cyan`, `Finder`, `RingWithFinder`, `Calm`; `ScriptedReader.Frame` |
| `tests/RoRoRo.UrOcr.Tests/Engine/PulseLoopFinderTests.cs` | create | the loop with a finder |

---

### Task 1: ClearAt on the bridge client, followed through GetPlayback

**Files:**
- Modify: `Ipc/BridgeContract.cs`, `Ipc/IMacroRunClient.cs`, `Ipc/MacroRunClient.cs`, `Engine/MacroCall.cs`
- Modify: `tests/RoRoRo.UrOcr.Tests/Engine/PulseFakes.cs`, `tests/RoRoRo.UrOcr.Tests/Engine/MacroCallTests.cs`
- Create: `tests/RoRoRo.UrOcr.Tests/Ipc/ClearAtClientTests.cs`

**Interfaces:**
- Consumes (existing): `RunMacroResponse(bool Ok, string? PlaybackId, bool Queued, string? Reason, string? Detail)`; `BridgeContract.Json`, `ContractVersion`, `CallerId`; `BridgeReasons.Refused`, `Busy`, `NotRunning`, `Skipped`; `MacroCall(IMacroRunClient client, string target, IClock clock, Action<string> log)`, `StepAsync(bool foreground, CancellationToken ct, Func<bool>? inFront = null)`, `CallResult(CallStatus Status, string Label, string? Detail)`, `MacroCall.RetryMs`; test fakes `ScriptedMacros`, `PulseClock`.
- Produces:
  - `public sealed record ClearAtClient(int W, int H);`
  - `public sealed record ClearAtPoint(int X, int Y, string Label);`
  - `public sealed record ClearAtOutline(int W, int H, int MinCount, int WhiteMin);`
  - `public sealed record ClearAtRequest(string ContractVersion, string Method, string CallerPluginId, string Target, ClearAtClient Client, IReadOnlyList<ClearAtPoint> Points, ClearAtOutline Outline, int? MaxMsPerPoint);`
  - `BridgeContract.MethodClearAt = "ClearAt"`, `BridgeContract.MaxClearAtPoints = 64`, `BridgeContract.ForClearAt(string target, ClearAtClient client, IReadOnlyList<ClearAtPoint> points, ClearAtOutline outline, int? maxMsPerPoint = null)`.
  - `IMacroRunClient.ClearAtAsync(ClearAtRequest request, CancellationToken ct) : Task<RunMacroResponse>` (default: refused).
  - `MacroCall.BeginClearAt(ClearAtClient size, IReadOnlyList<ClearAtPoint> points, ClearAtOutline outline)`; `MacroCall.ClearAtLabel(int points) : string` (`"ClearAt (3 points)"`, `"ClearAt (1 point)"`).
  - Test fake: `ScriptedMacros.ClearAtId = "clear-at"`, `ScriptedMacros.ClearAts : List<ClearAtRequest>`, `ScriptedMacros.ClearAtReplies : Queue<RunMacroResponse>`. A ClearAt is listed in `Runs` as `("clear-at", [target], null)` and scripted with `Script("clear-at", ...)`.

- [ ] **Step 1: Write the failing wire and client tests**

Create `tests/RoRoRo.UrOcr.Tests/Ipc/ClearAtClientTests.cs`:

```csharp
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using RoRoRo.UrOcr.Ipc;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Ipc;

/// <summary>
/// ClearAt against a fake Ur Task on a real in-process pipe. The request is read back as raw JSON
/// so the wire names are pinned, not just the C# record.
/// </summary>
public class ClearAtClientTests
{
    private static ClearAtRequest Request() => BridgeContract.ForClearAt("42", new ClearAtClient(800, 599),
        new[] { new ClearAtPoint(412, 288, "ore 1") }, new ClearAtOutline(50, 50, 60, 225));

    private static async Task<(TResponse Response, JsonDocument Request)> RoundTrip<TResponse>(
        Func<MacroRunClient, Task<TResponse>> call, string replyJson)
    {
        var pipeName = "626labs-ur-ocr-test-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        Task<Stream?> Open(CancellationToken ct)
        {
            var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            return client.ConnectAsync(2000, ct).ContinueWith<Stream?>(
                t => t.IsCompletedSuccessfully ? client : null, TaskContinuationOptions.ExecuteSynchronously);
        }

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            var request = await FrameCodec.ReadFrameAsync(server, default);
            await FrameCodec.WriteFrameAsync(server, Encoding.UTF8.GetBytes(replyJson), default);
            return JsonDocument.Parse(request!);
        });

        var response = await call(new MacroRunClient(Open));
        return (response, await serverTask);
    }

    private sealed class OnlyRun : IMacroRunClient
    {
        public Task<RunMacroResponse> RunAsync(string macroId, IReadOnlyList<string>? targets, CancellationToken ct) =>
            Task.FromResult(new RunMacroResponse(true, "pb", false, null, null));
    }

    [Fact]
    public void The_request_has_the_exact_wire_shape()
    {
        var json = JsonSerializer.Serialize(Request(), BridgeContract.Json);

        Assert.Equal(
            "{\"contractVersion\":\"1.0\",\"method\":\"ClearAt\",\"callerPluginId\":\"626labs.ur-ocr\",\"target\":\"42\"," +
            "\"client\":{\"w\":800,\"h\":599},\"points\":[{\"x\":412,\"y\":288,\"label\":\"ore 1\"}]," +
            "\"outline\":{\"w\":50,\"h\":50,\"minCount\":60,\"whiteMin\":225},\"maxMsPerPoint\":null}",
            json);
    }

    [Fact]
    public async Task Sends_ClearAt_and_returns_the_playback_id()
    {
        var (resp, req) = await RoundTrip(
            c => ((IMacroRunClient)c).ClearAtAsync(Request(), default),
            "{\"ok\":true,\"playbackId\":\"pb-7\",\"queued\":false}");

        var root = req.RootElement;
        Assert.Equal("ClearAt", root.GetProperty("method").GetString());
        Assert.Equal("42", root.GetProperty("target").GetString());
        Assert.Equal("ore 1", root.GetProperty("points")[0].GetProperty("label").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("maxMsPerPoint").ValueKind);
        Assert.True(resp.Ok);
        Assert.Equal("pb-7", resp.PlaybackId);
    }

    [Fact]
    public async Task A_refusal_comes_back_with_its_reason_and_detail()
    {
        var (resp, _) = await RoundTrip(
            c => c.ClearAtAsync(Request(), default),
            "{\"ok\":false,\"reason\":\"refused\",\"detail\":\"Unknown method 'ClearAt'.\"}");

        Assert.False(resp.Ok);
        Assert.Equal(BridgeReasons.Refused, resp.Reason);
        Assert.Equal("Unknown method 'ClearAt'.", resp.Detail);
    }

    [Fact]
    public async Task No_Ur_Task_is_not_running_and_never_throws()
    {
        var client = new MacroRunClient(_ => Task.FromResult<Stream?>(null));

        var resp = await client.ClearAtAsync(Request(), default);

        Assert.False(resp.Ok);
        Assert.Equal(BridgeReasons.NotRunning, resp.Reason);
    }

    [Fact]
    public async Task A_client_that_cannot_send_ClearAt_refuses()
    {
        var resp = await ((IMacroRunClient)new OnlyRun()).ClearAtAsync(Request(), default);

        Assert.False(resp.Ok);
        Assert.Equal(BridgeReasons.Refused, resp.Reason);
    }
}
```

- [ ] **Step 2: Write the failing MacroCall tests**

In `tests/RoRoRo.UrOcr.Tests/Engine/PulseFakes.cs`, add these members to `ScriptedMacros`, after the `RunReplies` property:

```csharp
    /// <summary>The id a ClearAt playback is listed under in Runs and scripted with in Script.</summary>
    public const string ClearAtId = "clear-at";
    public List<ClearAtRequest> ClearAts { get; } = new();
    /// <summary>Answers to ClearAt, used before RunReplies and the default accept.</summary>
    public Queue<RunMacroResponse> ClearAtReplies { get; } = new();

    public Task<RunMacroResponse> ClearAtAsync(ClearAtRequest request, CancellationToken ct)
    {
        ClearAts.Add(request);
        if (ClearAtReplies.Count == 0) return RunAsync(ClearAtId, new[] { request.Target }, null, ct);
        Runs.Add((ClearAtId, new[] { request.Target }, null));
        return Task.FromResult(ClearAtReplies.Dequeue());
    }
```

In `tests/RoRoRo.UrOcr.Tests/Engine/MacroCallTests.cs`, add after the `Step` helper:

```csharp
    private static readonly ClearAtClient Size = new(800, 599);
    private static readonly ClearAtOutline Outline = new(50, 50, 60, 225);

    private static Rig BuildClearAt(int points = 3)
    {
        var macros = new ScriptedMacros();
        var clock = new PulseClock();
        var log = new List<string>();
        var call = new MacroCall(macros, "42", clock, log.Add);
        call.BeginClearAt(Size,
            Enumerable.Range(1, points).Select(n => new ClearAtPoint(100 + n, 200, $"stone {n}")).ToList(), Outline);
        return new Rig(call, macros, clock, log);
    }

    [Fact]
    public async Task A_ClearAt_starts_on_the_account_and_is_followed_through_GetPlayback()
    {
        var rig = BuildClearAt();

        Assert.Equal(CallStatus.Waiting, (await Step(rig)).Status);
        var req = Assert.Single(rig.Macros.ClearAts);
        Assert.Equal("ClearAt", req.Method);
        Assert.Equal("42", req.Target);
        Assert.Equal(Size, req.Client);
        Assert.Equal(Outline, req.Outline);
        Assert.Equal(3, req.Points.Count);
        Assert.Null(req.MaxMsPerPoint);

        var end = await Step(rig);
        Assert.Equal(CallStatus.Done, end.Status);
        Assert.Equal("ClearAt (3 points)", end.Label);
        Assert.Equal(new[] { "pb1" }, rig.Macros.Polls);
        Assert.False(rig.Call.Active);
    }

    [Fact]
    public async Task A_ClearAt_that_skipped_every_point_is_Skipped()
    {
        var rig = BuildClearAt();
        rig.Macros.Script(ScriptedMacros.ClearAtId, ScriptedMacros.Skipped);

        await Step(rig);

        Assert.Equal(CallStatus.Skipped, (await Step(rig)).Status);
    }

    [Fact]
    public async Task A_ClearAt_waits_until_the_account_is_in_front_right_before_it_starts()
    {
        var rig = BuildClearAt(points: 1);

        Assert.Equal(CallStatus.Waiting, (await rig.Call.StepAsync(true, CancellationToken.None, () => false)).Status);
        Assert.Empty(rig.Macros.ClearAts);
        Assert.Equal("ClearAt (1 point)", rig.Call.Label);

        await rig.Call.StepAsync(true, CancellationToken.None, () => true);
        Assert.Single(rig.Macros.ClearAts);
    }

    [Fact]
    public async Task An_Ur_Task_without_ClearAt_stops_with_its_reason()
    {
        var rig = BuildClearAt();
        rig.Macros.ClearAtReplies.Enqueue(new RunMacroResponse(false, null, false, "refused", "Unknown method 'ClearAt'."));

        var end = await Step(rig);

        Assert.Equal(CallStatus.Stop, end.Status);
        Assert.Contains("ClearAt (3 points)", end.Detail);
        Assert.Contains("Unknown method 'ClearAt'", end.Detail);
        Assert.False(rig.Call.Active);
    }

    [Fact]
    public async Task A_busy_ClearAt_tries_again_after_a_second_with_the_same_points()
    {
        var rig = BuildClearAt();
        rig.Macros.ClearAtReplies.Enqueue(ScriptedMacros.Refusal("busy"));

        Assert.Equal(CallStatus.Waiting, (await Step(rig)).Status);
        Assert.Equal(CallStatus.Waiting, (await Step(rig)).Status);
        Assert.Single(rig.Macros.ClearAts);

        rig.Clock.Advance(MacroCall.RetryMs);
        Assert.Equal(CallStatus.Waiting, (await Step(rig)).Status);
        Assert.Equal(2, rig.Macros.ClearAts.Count);
        Assert.Same(rig.Macros.ClearAts[0], rig.Macros.ClearAts[1]);
        Assert.Equal(CallStatus.Done, (await Step(rig)).Status);
    }
```

- [ ] **Step 3: Run the new tests to verify they fail**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~ClearAtClientTests|FullyQualifiedName~MacroCallTests"`
Expected: build FAILS with CS0246 (`ClearAtRequest`, `ClearAtClient`, `ClearAtPoint`, `ClearAtOutline` not found) and CS1061 (`BeginClearAt`, `ClearAtAsync`).

- [ ] **Step 4: Add the wire records and ForClearAt**

In `Ipc/BridgeContract.cs`, insert after the `GetPlaybackResponse` record:

```csharp
/// <summary>ClearAt's client size: the client the points were measured in. Ur Task sizes the window
/// to it first, as it does for a recorded macro, then plays the points and the box unscaled.</summary>
public sealed record ClearAtClient(int W, int H);

/// <summary>One ClearAt point in pixels of <see cref="ClearAtClient"/>. Label names it in Ur Task's log.</summary>
public sealed record ClearAtPoint(int X, int Y, string Label);

/// <summary>The outline check at every point: a W x H box centred on the point, passing at MinCount
/// or more pixels whose every channel is at least WhiteMin.</summary>
public sealed record ClearAtOutline(int W, int H, int MinCount, int WhiteMin);

/// <summary>
/// Ur Task's ClearAt (bridge 1.x, additive): every point in order as ONE playback, each a reach hold
/// (hover, outline check, hold-release-look). Answered with a RunMacroResponse and followed with
/// GetPlayback like a macro. MaxMsPerPoint null means no time limit and is always written, as in
/// the spec's example.
/// </summary>
public sealed record ClearAtRequest(string ContractVersion, string Method, string CallerPluginId, string Target,
    ClearAtClient Client, IReadOnlyList<ClearAtPoint> Points, ClearAtOutline Outline,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? MaxMsPerPoint);
```

In the `BridgeContract` class, add after `public const string MethodGetPlayback = "GetPlayback";`:

```csharp
    public const string MethodClearAt = "ClearAt";
    /// <summary>Ur Task refuses a ClearAt with more points than this (or none).</summary>
    public const int MaxClearAtPoints = 64;
```

and after `ForPlayback`:

```csharp
    public static ClearAtRequest ForClearAt(string target, ClearAtClient client, IReadOnlyList<ClearAtPoint> points,
        ClearAtOutline outline, int? maxMsPerPoint = null)
        => new(ContractVersion, MethodClearAt, CallerId, target, client, points, outline, maxMsPerPoint);
```

- [ ] **Step 5: Add ClearAtAsync to the interface and the pipe client**

In `Ipc/IMacroRunClient.cs`, add after the `GetPlaybackAsync` default member:

```csharp

    /// <summary>Ur Task's ClearAt: every point as one playback, followed with GetPlaybackAsync. The
    /// default refuses so a client that cannot send it (the trigger tests' fakes) still compiles;
    /// MacroRunClient sends it.</summary>
    Task<RunMacroResponse> ClearAtAsync(ClearAtRequest request, CancellationToken ct)
        => Task.FromResult(new RunMacroResponse(false, null, false, BridgeReasons.Refused,
            "This client cannot ask Ur Task to clear at points."));
```

In `Ipc/MacroRunClient.cs`, add after `GetPlaybackAsync`:

```csharp
    /// <summary>One ClearAt playback. Ur Task acks with a playback id or a refusal: busy, a bad
    /// point or box, or "Unknown method 'ClearAt'." from an Ur Task without it.</summary>
    public Task<RunMacroResponse> ClearAtAsync(ClearAtRequest request, CancellationToken ct) =>
        ExchangeAsync(request,
            (reason, detail) => new RunMacroResponse(false, null, false, reason, detail),
            "ClearAt sent; Ur Task did not ack within the tick window.",
            ct);
```

- [ ] **Step 6: Let MacroCall start either a macro or a ClearAt**

In `Engine/MacroCall.cs`, replace the summary line `/// Runs one Ur Task macro for one account and follows it to its end, one tick at a time.` with:

```csharp
/// Runs one Ur Task macro (or one ClearAt call) for one account and follows it to its end, one tick at a time.
```

Replace:

```csharp
    private string? _macroId;
    private string _label = "";
```

with:

```csharp
    private Func<CancellationToken, Task<RunMacroResponse>>? _start;
    private string _label = "";
```

Replace the whole block from `public bool Active => _macroId is not null;` through the end of `Begin` (the closing brace after `_sawBehind = false;`) with:

```csharp
    public bool Active => _start is not null;
    public string Label => _label;

    public void Begin(string macroId, string label) =>
        Arm(label, ct => client.RunAsync(macroId, new[] { target }, InterAltDelayMs, ct));

    /// <summary>One ClearAt for this account: every point in order as one playback, followed through
    /// GetPlayback like a macro. The request is built once, so a busy retry or an interrupted rerun
    /// sends the same points.</summary>
    public void BeginClearAt(ClearAtClient size, IReadOnlyList<ClearAtPoint> points, ClearAtOutline outline)
    {
        var request = BridgeContract.ForClearAt(target, size, points, outline);
        Arm(ClearAtLabel(points.Count), ct => client.ClearAtAsync(request, ct));
    }

    /// <summary>The name Ur Task's log gives a ClearAt playback.</summary>
    public static string ClearAtLabel(int points) => $"ClearAt ({points} {(points == 1 ? "point" : "points")})";

    private void Arm(string label, Func<CancellationToken, Task<RunMacroResponse>> start)
    {
        _start = start;
        _label = label;
        _playbackId = null;
        _retryAt = DateTimeOffset.MinValue;
        _loggedRefusal = null;
        _interruptions = 0;
        _sawBehind = false;
    }
```

In `StepAsync`, replace:

```csharp
        if (_macroId is null) throw new InvalidOperationException("No macro to step: call Begin first.");
```

with:

```csharp
        if (_start is null) throw new InvalidOperationException("No macro to step: call Begin first.");
```

In `StartAsync`, replace:

```csharp
        var resp = await client.RunAsync(_macroId!, new[] { target }, InterAltDelayMs, ct).ConfigureAwait(false);
```

with:

```csharp
        var resp = await _start!(ct).ConfigureAwait(false);
```

In `End`, replace `_macroId = null;` with `_start = null;`.

- [ ] **Step 7: Run the new tests to verify they pass**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~ClearAtClientTests|FullyQualifiedName~MacroCallTests"`
Expected: PASS, including every existing MacroCall test.

- [ ] **Step 8: Run the full suite**

Run: `dotnet build rororo-ur-ocr.csproj` then `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj`
Expected: build clean; **359 passed** (349 + 10).

- [ ] **Step 9: Commit**

```bash
git add Ipc/BridgeContract.cs Ipc/IMacroRunClient.cs Ipc/MacroRunClient.cs Engine/MacroCall.cs tests/RoRoRo.UrOcr.Tests/Ipc/ClearAtClientTests.cs tests/RoRoRo.UrOcr.Tests/Engine/PulseFakes.cs tests/RoRoRo.UrOcr.Tests/Engine/MacroCallTests.cs
git commit -m "feat(ipc): ClearAt bridge call, followed through GetPlayback" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Finder data in the measured ring, stored on the ring

**Files:**
- Create: `Storage/FinderSetup.cs`, `tests/RoRoRo.UrOcr.Tests/Storage/FinderSetupTests.cs`
- Modify: `Storage/MeasuredRing.cs`, `Storage/Ring.cs`, `Storage/RingImporter.cs`, `Storage/PulseValidation.cs`, `RingImportCommand.cs`
- Modify: `tests/RoRoRo.UrOcr.Tests/Storage/RingImporterTests.cs`, `tests/RoRoRo.UrOcr.Tests/Storage/RingStorageTests.cs`, `tests/RoRoRo.UrOcr.Tests/Storage/PulseValidationTests.cs`, `tests/RoRoRo.UrOcr.Tests/RingImportCommandTests.cs`

**Interfaces:**
- Consumes (existing): `Rgb(int R, int G, int B)`; `ColorCriteria.InRange(Rgb)`, `ColorCriteria.MaxTolerance` (442); `MeasuredRing` (positional, last parameter `int SpotCooldownMs = 1000`), `MeasuredRing.Load`, `Validate`; `RingDefinition(string Id, string Name, IReadOnlyList<LayerDefinition> Layers, int MinLayerSpots = 3)`; `RingImporter.Build`; `PulseValidation.Validate(PulseConfig, IReadOnlyList<RingDefinition>, IReadOnlyList<Trigger>)`; `PulseConfig.AimLayer`; `TriggerJsonOptions.Default` (internal, visible to tests); test helpers `RingImporterTests.Measured()` (layers `navy`, `grey`, client 800x599) and `RingImporterTests.Macros()`.
- Produces:
  - `public sealed record OreColour(string Name, Rgb Rgb);`
  - `public sealed record OutlineBox(int W, int H, int MinCount = 60, int WhiteMin = 225);`
  - `public sealed record FinderSetup(string Layer, int ClientW, int ClientH, int Pitch, int CenterX, int CenterY, int RadiusBlocks, OutlineBox Outline, IReadOnlyList<OreColour> Ore, int OreToleranceRgb)` with `DefaultRadiusBlocks` (5), `MinPitch` (4), `MaxRadiusBlocks` (20), `MaxOutlineSide` (120), `string? Validate()`, `bool BoxFits(int x, int y)`.
  - `public sealed record MeasuredFinder(string Layer, int Pitch, int CenterX, int CenterY, OutlineBox Outline, IReadOnlyList<OreColour> Ore, int OreToleranceRgb, int RadiusBlocks = FinderSetup.DefaultRadiusBlocks)` with `FinderSetup ToSetup(int clientW, int clientH)`.
  - `MeasuredRing.Finders : IReadOnlyList<MeasuredFinder>?` (last positional parameter, default null).
  - `RingDefinition.Finders : IReadOnlyList<FinderSetup>?` (last positional parameter, default null, left out of JSON when null).
  - `PulseValidation.FinderFor(RingDefinition ring, int aimLayer) : FinderSetup?`.
  - Test helpers: `FinderSetupTests.Valid()` (internal static; layer `grey`, client 800x599, pitch 50, centre 390,340, radius 5, outline 50x50/60/225, ore `cyan crystal` (60,220,230), tolerance 40); `RingImporterTests.Finder(string layer = "grey")` (internal static, the same values as a `MeasuredFinder`).

- [ ] **Step 1: Write the failing FinderSetup tests**

Create `tests/RoRoRo.UrOcr.Tests/Storage/FinderSetupTests.cs`:

```csharp
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Storage;

public class FinderSetupTests
{
    internal static FinderSetup Valid() => new("grey", 800, 599, Pitch: 50, CenterX: 390, CenterY: 340,
        RadiusBlocks: 5, Outline: new OutlineBox(50, 50),
        Ore: new[] { new OreColour("cyan crystal", new Rgb(60, 220, 230)) }, OreToleranceRgb: 40);

    [Fact]
    public void A_measured_finder_is_valid() => Assert.Null(Valid().Validate());

    [Theory]
    [InlineData("pitch", "pitch must be 4 to 599")]
    [InlineData("radius", "radiusBlocks must be 1 to 20")]
    [InlineData("bigBox", "outline must be 1 to 120 pixels a side")]
    [InlineData("minCount", "outline.minCount must be 1 to 2500")]
    [InlineData("whiteMin", "outline.whiteMin must be 1 to 255")]
    [InlineData("centre", "must sit inside the 800x599 game area")]
    [InlineData("oreColour", "Ore colour cyan crystal has a channel outside 0 to 255")]
    [InlineData("oreMissing", "ore is missing")]
    [InlineData("tolerance", "oreToleranceRgb must be 1 to 442")]
    public void A_bad_value_is_named(string what, string expected)
    {
        var v = Valid();
        var bad = what switch
        {
            "pitch" => v with { Pitch = 3 },
            "radius" => v with { RadiusBlocks = 0 },
            "bigBox" => v with { Outline = v.Outline with { W = 121 } },
            "minCount" => v with { Outline = v.Outline with { MinCount = 2501 } },
            "whiteMin" => v with { Outline = v.Outline with { WhiteMin = 0 } },
            "centre" => v with { CenterX = 10 },
            "oreColour" => v with { Ore = new[] { new OreColour("cyan crystal", new Rgb(60, 300, 230)) } },
            "oreMissing" => v with { Ore = null! },
            "tolerance" => v with { OreToleranceRgb = 0 },
            _ => throw new ArgumentOutOfRangeException(nameof(what)),
        };

        Assert.Contains(expected, bad.Validate());
    }
}
```

- [ ] **Step 2: Write the failing import, storage, validation and command tests**

In `tests/RoRoRo.UrOcr.Tests/Storage/RingImporterTests.cs`, add `using System.Text.Json.Nodes;` to the usings, then add after `Macros()`:

```csharp
    internal static MeasuredFinder Finder(string layer = "grey") => new(layer, Pitch: 50, CenterX: 390, CenterY: 340,
        Outline: new OutlineBox(50, 50), Ore: new[] { new OreColour("cyan crystal", new Rgb(60, 220, 230)) },
        OreToleranceRgb: 40);

    [Fact]
    public void A_measured_file_from_before_the_finder_loads_and_imports_with_none()
    {
        var node = JsonSerializer.SerializeToNode(Measured(), TriggerJsonOptions.Default)!.AsObject();
        node.Remove("finders");
        var path = Path.Combine(Path.GetTempPath(), "urocr-tests", Guid.NewGuid().ToString("N") + ".measured.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, node.ToJsonString());

        var m = MeasuredRing.Load(path);

        Assert.Null(m.Finders);
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
```

In `tests/RoRoRo.UrOcr.Tests/Storage/RingStorageTests.cs`, add after `Rings_and_ring_spots_survive_a_reload`:

```csharp
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

        s.UpsertRing(ring);
        Assert.DoesNotContain("finders", File.ReadAllText(path));
    }
```

In `tests/RoRoRo.UrOcr.Tests/Storage/PulseValidationTests.cs`, add after `A_complete_pulse_is_valid`:

```csharp
    private static RingDefinition RingWith(FinderSetup finder) => Ring() with { Finders = new[] { finder } };

    [Fact]
    public void FinderFor_picks_the_finder_of_the_aim_layer()
    {
        var ring = RingWith(FinderSetupTests.Valid());      // a finder for grey, layer 3

        Assert.Equal("grey", PulseValidation.FinderFor(ring, 3)!.Layer);
        Assert.Null(PulseValidation.FinderFor(ring, 2));
        Assert.Null(PulseValidation.FinderFor(ring, 4));
        Assert.Null(PulseValidation.FinderFor(Ring(), 3));
    }

    [Fact]
    public void A_bad_finder_on_the_aim_layer_fails_the_pulse() =>
        Assert.Contains("Ring mine8's ore finder for grey: pitch must be 4 to 599",
            Check(Pulse(), null, RingWith(FinderSetupTests.Valid() with { Pitch = 3 })));

    [Fact]
    public void A_bad_finder_on_another_layer_leaves_the_pulse_valid() =>
        Assert.Null(Check(Pulse(), null, RingWith(FinderSetupTests.Valid() with { Layer = "navy", Pitch = 3 })));
```

In `tests/RoRoRo.UrOcr.Tests/RingImportCommandTests.cs`, replace:

```csharp
    private string WriteMeasured()
    {
        var path = Path.Combine(_dir, "mine8.measured.json");
        File.WriteAllText(path, JsonSerializer.Serialize(RingImporterTests.Measured(), TriggerJsonOptions.Default));
```

with:

```csharp
    private string WriteMeasured(MeasuredRing? measured = null)
    {
        var path = Path.Combine(_dir, "mine8.measured.json");
        File.WriteAllText(path, JsonSerializer.Serialize(measured ?? RingImporterTests.Measured(), TriggerJsonOptions.Default));
```

and add after `Imports_the_ring_and_reports_it`:

```csharp
    [Fact]
    public void Reports_the_ore_finder_it_imported()
    {
        WriteMacros();

        var code = Run(() => false,
            WriteMeasured(RingImporterTests.Measured() with { Finders = new[] { RingImporterTests.Finder() } }));

        Assert.Equal(0, code);
        Assert.Contains("ore finder on grey (50 px blocks, 1 ore colour)", File.ReadAllText(Report));
        Assert.NotNull(Assert.Single(new TriggerStore(Triggers).Rings).Finders);
    }
```

- [ ] **Step 3: Run the new tests to verify they fail**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~FinderSetupTests|FullyQualifiedName~RingImporterTests|FullyQualifiedName~RingStorageTests|FullyQualifiedName~PulseValidationTests|FullyQualifiedName~RingImportCommandTests"`
Expected: build FAILS with CS0246 (`FinderSetup`, `OutlineBox`, `OreColour`, `MeasuredFinder`) and CS0117 (`Finders`, `FinderFor`).

- [ ] **Step 4: Create the finder records**

Create `Storage/FinderSetup.cs`:

```csharp
namespace RoRoRo.UrOcr.Storage;

/// <summary>An ore colour the finder looks for, named for the measured file and the log.</summary>
public sealed record OreColour(string Name, Rgb Rgb);

/// <summary>The outline check Ur Task runs at each point: a W x H box centred on the point, passing
/// at MinCount or more pixels whose every channel is at least WhiteMin. Spec values: 60 and 225.</summary>
public sealed record OutlineBox(int W, int H, int MinCount = 60, int WhiteMin = 225);

/// <summary>
/// The ore finder for one layer of a ring (spec "Reach, measured, and the ore finder"), in pixels of
/// the client it was measured in (ClientW x ClientH, the measured file's recorded size, which is what
/// ClearAt sends as its client). Pitch is one block at that layer; the grid reaches RadiusBlocks
/// blocks around the character's centre. Stored in triggers.json on the ring; imported from the
/// measured file's "finders".
/// </summary>
public sealed record FinderSetup(string Layer, int ClientW, int ClientH, int Pitch, int CenterX, int CenterY,
    int RadiusBlocks, OutlineBox Outline, IReadOnlyList<OreColour> Ore, int OreToleranceRgb)
{
    public const int DefaultRadiusBlocks = 5;
    /// <summary>Samples are taken every Pitch / 4 pixels: 4 is the smallest pitch with a 1 px step.</summary>
    public const int MinPitch = 4;
    public const int MaxRadiusBlocks = 20;
    /// <summary>Ur Task refuses a larger outline box (its OutlineCheck.MaxSide).</summary>
    public const int MaxOutlineSide = 120;

    /// <summary>Null when ClearAt can use it, else one sentence naming the first problem. Covers every
    /// refusal Ur Task makes on the outline, so a stored finder never sends one.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Layer)) return "layer is missing.";
        if (ClientW < 1 || ClientH < 1) return "the client size must be positive.";
        var maxPitch = Math.Min(ClientW, ClientH);
        if (Pitch < MinPitch || Pitch > maxPitch) return $"pitch must be {MinPitch} to {maxPitch} pixels, not {Pitch}.";
        if (RadiusBlocks < 1 || RadiusBlocks > MaxRadiusBlocks)
            return $"radiusBlocks must be 1 to {MaxRadiusBlocks}, not {RadiusBlocks}.";
        if (Outline is null) return "outline is missing.";
        if (Outline.W < 1 || Outline.H < 1 || Outline.W > MaxOutlineSide || Outline.H > MaxOutlineSide)
            return $"outline must be 1 to {MaxOutlineSide} pixels a side, not {Outline.W}x{Outline.H}.";
        if (Outline.MinCount < 1 || Outline.MinCount > Outline.W * Outline.H)
            return $"outline.minCount must be 1 to {Outline.W * Outline.H} (the box's pixels), not {Outline.MinCount}.";
        if (Outline.WhiteMin < 1 || Outline.WhiteMin > 255)
            return $"outline.whiteMin must be 1 to 255, not {Outline.WhiteMin}.";
        if (!BoxFits(CenterX, CenterY))
            return $"The centre ({CenterX}, {CenterY}) and its {Outline.W}x{Outline.H} outline box must sit inside the {ClientW}x{ClientH} game area.";
        if (Ore is null) return "ore is missing (use [] for none).";
        foreach (var c in Ore)
        {
            if (c is null || string.IsNullOrWhiteSpace(c.Name)) return "Every ore colour needs a name.";
            if (c.Rgb is null || !ColorCriteria.InRange(c.Rgb)) return $"Ore colour {c.Name} has a channel outside 0 to 255.";
        }
        if (OreToleranceRgb < 1 || OreToleranceRgb > ColorCriteria.MaxTolerance)
            return $"oreToleranceRgb must be 1 to {ColorCriteria.MaxTolerance}, not {OreToleranceRgb}.";
        return null;
    }

    /// <summary>True when the outline box centred on (x, y) lies wholly inside the client. Ur Task
    /// refuses the whole ClearAt otherwise (its PointMath.InsideClient over CheckBox(-(W / 2), -(H / 2), W, H)).</summary>
    public bool BoxFits(int x, int y)
    {
        var left = x - Outline.W / 2;
        var top = y - Outline.H / 2;
        return left >= 0 && top >= 0 && left + Outline.W <= ClientW && top + Outline.H <= ClientH;
    }
}
```

- [ ] **Step 5: Add finders to the measured ring and the ring definition**

In `Storage/MeasuredRing.cs`, add after `public sealed record MeasuredAction(...)`:

```csharp

/// <summary>
/// One layer's ore finder as the measured file holds it, in pixels of the file's recorded client
/// size (spec "Reach, measured, and the ore finder"). A top-level "finders" list, not a field of
/// "layers": ring-fit.ps1 -Write rewrites "layers" and keeps every other key.
/// </summary>
public sealed record MeasuredFinder(string Layer, int Pitch, int CenterX, int CenterY, OutlineBox Outline,
    IReadOnlyList<OreColour> Ore, int OreToleranceRgb, int RadiusBlocks = FinderSetup.DefaultRadiusBlocks)
{
    public FinderSetup ToSetup(int clientW, int clientH) =>
        new(Layer, clientW, clientH, Pitch, CenterX, CenterY, RadiusBlocks, Outline, Ore, OreToleranceRgb);
}
```

Replace:

```csharp
    MeasuredAction RockCap, MeasuredAction Camera, int SpotCooldownMs = 1000)
```

with:

```csharp
    MeasuredAction RockCap, MeasuredAction Camera, int SpotCooldownMs = 1000,
    IReadOnlyList<MeasuredFinder>? Finders = null)
```

Replace (at the end of `Validate`):

```csharp
        if (SpotCooldownMs < 0) return "spotCooldownMs cannot be negative.";
        return null;
```

with:

```csharp
        if (SpotCooldownMs < 0) return "spotCooldownMs cannot be negative.";
        if (Finders is not null)
        {
            var finderLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in Finders)
            {
                if (f is null) return "finders has an empty entry.";
                if (!Layers.Any(l => l is not null && string.Equals(l.Name, f.Layer, StringComparison.OrdinalIgnoreCase)))
                    return $"Finder layer {f.Layer} is not one of the layers.";
                if (!finderLayers.Add(f.Layer)) return $"Two finders are for layer {f.Layer}.";
                if (f.ToSetup(RecordedClientW, RecordedClientH).Validate() is { } finderProblem)
                    return $"Finder {f.Layer}: {finderProblem}";
            }
        }
        return null;
```

In `Storage/Ring.cs`, add `using System.Text.Json.Serialization;` above `namespace RoRoRo.UrOcr.Storage;`, and replace:

```csharp
public sealed record RingDefinition(string Id, string Name, IReadOnlyList<LayerDefinition> Layers, int MinLayerSpots = 3);
```

with:

```csharp
/// <remarks>Finders: the ore finder per layer (spec "Reach, measured, and the ore finder"), null on a
/// ring imported without one; left out of triggers.json when null, so older files round-trip unchanged.</remarks>
public sealed record RingDefinition(string Id, string Name, IReadOnlyList<LayerDefinition> Layers, int MinLayerSpots = 3,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<FinderSetup>? Finders = null);
```

- [ ] **Step 6: Import the finders, validate the aim layer's, and report them**

In `Storage/RingImporter.cs`, replace:

```csharp
        var ring = new RingDefinition(m.RingId, m.Name,
            m.Layers.Select(l => new LayerDefinition(l.Name, l.Rock.ToList())).ToList(), m.MinLayerSpots);
```

with:

```csharp
        var ring = new RingDefinition(m.RingId, m.Name,
            m.Layers.Select(l => new LayerDefinition(l.Name, l.Rock.ToList())).ToList(), m.MinLayerSpots,
            m.Finders?.Select(f => f.ToSetup(m.RecordedClientW, m.RecordedClientH)).ToList());
```

In `Storage/PulseValidation.cs`, add after `SpotsOf`:

```csharp
    /// <summary>The ring's ore finder for the 1-based aim layer, or null: no finders, none for that
    /// layer, or a layer out of range. Null means the pulse clears with the eight Clear spot macros.</summary>
    public static FinderSetup? FinderFor(RingDefinition ring, int aimLayer)
    {
        if (ring.Finders is null || aimLayer < 1 || aimLayer > ring.Layers.Count) return null;
        var name = ring.Layers[aimLayer - 1].Name;
        return ring.Finders.FirstOrDefault(f => f is not null && string.Equals(f.Layer, name, StringComparison.OrdinalIgnoreCase));
    }
```

and in `Validate`, replace:

```csharp
        if (p.RockCapMinutes < 1 || p.RockCapMinutes > MaxRockCapMinutes)
            return $"rockCapMinutes must be 1 to {MaxRockCapMinutes}, not {p.RockCapMinutes}.";
```

with:

```csharp
        if (p.RockCapMinutes < 1 || p.RockCapMinutes > MaxRockCapMinutes)
            return $"rockCapMinutes must be 1 to {MaxRockCapMinutes}, not {p.RockCapMinutes}.";
        if (FinderFor(ring, p.AimLayer) is { } finder && finder.Validate() is { } finderProblem)
            return $"Ring {p.RingId}'s ore finder for {finder.Layer}: {finderProblem}";
```

In `RingImportCommand.cs`, replace the summary's first sentence `/// RoRoRo.UrOcr.exe --import-ring &lt;measured.json&gt; writes a measured ring (its layers,` with `/// RoRoRo.UrOcr.exe --import-ring &lt;measured.json&gt; writes a measured ring (its layers, its ore finders,` and replace:

```csharp
                foreach (var t in result.Triggers) lines.Add($"  {t.Name} -> macro {t.MacroId}");
```

with:

```csharp
                foreach (var t in result.Triggers) lines.Add($"  {t.Name} -> macro {t.MacroId}");
                lines.Add(result.Ring.Finders is { Count: > 0 } finders
                    ? "  ore finder on " + string.Join(", ", finders.Select(f =>
                        $"{f.Layer} ({f.Pitch} px blocks, {f.Ore.Count} ore {(f.Ore.Count == 1 ? "colour" : "colours")})"))
                    : "  no ore finder: pulses on this ring clear with the 8 Clear spot macros");
```

- [ ] **Step 7: Run the new tests to verify they pass**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~FinderSetupTests|FullyQualifiedName~RingImporterTests|FullyQualifiedName~RingStorageTests|FullyQualifiedName~PulseValidationTests|FullyQualifiedName~RingImportCommandTests"`
Expected: PASS, including every existing test in those classes.

- [ ] **Step 8: Run the full suite**

Run: `dotnet build rororo-ur-ocr.csproj` then `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj`
Expected: build clean; **379 passed** (359 + 10 FinderSetup cases + 4 importer + 2 storage + 3 validation + 1 command).

- [ ] **Step 9: Commit**

```bash
git add Storage/FinderSetup.cs Storage/MeasuredRing.cs Storage/Ring.cs Storage/RingImporter.cs Storage/PulseValidation.cs RingImportCommand.cs tests/RoRoRo.UrOcr.Tests/Storage/FinderSetupTests.cs tests/RoRoRo.UrOcr.Tests/Storage/RingImporterTests.cs tests/RoRoRo.UrOcr.Tests/Storage/RingStorageTests.cs tests/RoRoRo.UrOcr.Tests/Storage/PulseValidationTests.cs tests/RoRoRo.UrOcr.Tests/RingImportCommandTests.cs
git commit -m "feat(storage): ore finder per layer in the measured ring, stored on the ring" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: The target finder over a calm frame

**Files:**
- Create: `Engine/FramePixels.cs`, `Engine/TargetFinder.cs`
- Modify: `Engine/SpotReader.cs`
- Create: `tests/RoRoRo.UrOcr.Tests/Engine/Frames.cs`, `tests/RoRoRo.UrOcr.Tests/Engine/TargetFinderTests.cs`, `tests/RoRoRo.UrOcr.Tests/Engine/FramePixelsTests.cs`
- Modify: `tests/RoRoRo.UrOcr.Tests/Engine/SpotReaderTests.cs`

**Interfaces:**
- Consumes: `FinderSetup` (with `BoxFits`), `OreColour`, `OutlineBox` (Task 2); `BridgeContract.MaxClearAtPoints` (Task 1); existing `ColorMatcher.Distance(Rgb, Rgb)`, `ICaptureSource.Capture(RegionRect) : Bitmap`, `IWindowMetrics.HwndForPid/ClientOrigin/ClientSize`, `RegionRect(int X, int Y, int Width, int Height)`.
- Produces:
  - `public sealed class FramePixels(int width, int height, int[] rgb)` with `Width`, `Height`, `Rgb At(int x, int y)`, `static int Pack(Rgb)`, `static FramePixels FromBitmap(Bitmap)`.
  - `public sealed record FinderTarget(int X, int Y, bool Ore, string Label);` labels `ore 1..`, `stone 1..`.
  - `TargetFinder.Find(FramePixels frame, FinderSetup setup) : IReadOnlyList<FinderTarget>`; `TargetFinder.MaxPoints` (64), `TargetFinder.MinPatchSamples` (2).
  - `ISpotReader.ReadFrame(int pid) : FramePixels?` (default null); `SpotReader.ReadFrame(int pid)`.
  - Test helper `Frames.Solid(int w, int h, Rgb c) : int[]`, `Frames.Fill(int[] px, int w, int x, int y, int bw, int bh, Rgb c)`.

**The algorithm, exactly** (every test below follows from it):
1. `step = max(1, pitch / 4)`; `cols = clientW / step`; `rows = clientH / step`. Sample cell `(gx, gy)` sits at measured pixel `(gx * step + step / 2, gy * step + step / 2)`, read from the frame at `(x * frame.Width / clientW, y * frame.Height / clientH)` (clamped).
2. A cell is a hit when its colour is within `oreToleranceRgb` (Euclidean, `<=`) of any palette colour.
3. Hits cluster 4-connected. A patch of fewer than `MinPatchSamples` cells is dropped. A kept patch's target is the mean of its cells' sample points, rounded half away from zero.
4. Grid: every `(i, j)` in `-r..r` with `i*i + j*j <= r*r`, at `(centerX + i * pitch, centerY + j * pitch)`. A grid point whose cell `(x / step, y / step)` belongs to a kept patch is dropped.
5. Every point whose outline box does not fit (`BoxFits`) is dropped.
6. Ore by squared distance from the centre, ties by Y then X; then grid the same way. Take the first 64. Label `ore N` / `stone N` by position within each group.

- [ ] **Step 1: Write the test frame helper and the failing finder tests**

Create `tests/RoRoRo.UrOcr.Tests/Engine/Frames.cs`:

```csharp
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Tests.Engine;

/// <summary>Hand-painted frames for the ore finder: a solid colour with rectangles over it.</summary>
internal static class Frames
{
    public static int[] Solid(int w, int h, Rgb c) => Enumerable.Repeat(FramePixels.Pack(c), w * h).ToArray();

    public static void Fill(int[] px, int w, int x, int y, int bw, int bh, Rgb c)
    {
        var p = FramePixels.Pack(c);
        for (var yy = y; yy < y + bh; yy++)
            for (var xx = x; xx < x + bw; xx++)
                px[yy * w + xx] = p;
    }
}
```

Create `tests/RoRoRo.UrOcr.Tests/Engine/TargetFinderTests.cs`:

```csharp
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

/// <summary>
/// The finder on painted frames. Pitch 40, so samples every 10 px at 5, 15, 25 ... and a 40 px block
/// painted on a grid point holds 4 x 4 samples whose mean is that grid point.
/// </summary>
public class TargetFinderTests
{
    private static readonly Rgb Grey = new(120, 120, 120);
    private static readonly Rgb Cyan = new(60, 220, 230);

    private static FinderSetup Setup(int w = 400, int h = 300, int cx = 200, int cy = 150, int radius = 2,
        IReadOnlyList<OreColour>? ore = null) =>
        new("grey", w, h, Pitch: 40, CenterX: cx, CenterY: cy, RadiusBlocks: radius, Outline: new OutlineBox(40, 40),
            Ore: ore ?? new[] { new OreColour("cyan crystal", Cyan) }, OreToleranceRgb: 40);

    private static FramePixels Frame(int w, int h, params (int X, int Y, int W, int H, Rgb C)[] paint)
    {
        var px = Frames.Solid(w, h, Grey);
        foreach (var (x, y, bw, bh, c) in paint) Frames.Fill(px, w, x, y, bw, bh, c);
        return new FramePixels(w, h, px);
    }

    private static (int, int) At(FinderTarget t) => (t.X, t.Y);

    [Fact]
    public void A_plain_frame_gives_the_grid_nearest_first()
    {
        var targets = TargetFinder.Find(Frame(400, 300), Setup());

        Assert.Equal(13, targets.Count);                            // radius 2: 13 lattice points
        Assert.All(targets, t => Assert.False(t.Ore));
        Assert.Equal(new[] { (200, 150), (200, 110), (160, 150), (240, 150), (200, 190) }, targets.Take(5).Select(At));
        Assert.Equal(Enumerable.Range(1, 13).Select(n => $"stone {n}"), targets.Select(t => t.Label));
    }

    [Fact]
    public void An_ore_block_is_one_target_at_its_centre_ahead_of_the_grid()
    {
        var targets = TargetFinder.Find(Frame(400, 300, (260, 130, 40, 40, Cyan)), Setup());

        Assert.Equal(new FinderTarget(280, 150, true, "ore 1"), targets[0]);
        Assert.Equal(13, targets.Count);                            // 12 grid points and the ore
        Assert.DoesNotContain(targets, t => !t.Ore && At(t) == (280, 150));   // its grid point is dropped
        Assert.Equal(new FinderTarget(200, 150, false, "stone 1"), targets[1]);
    }

    [Fact]
    public void Ore_patches_go_nearest_first()
    {
        var targets = TargetFinder.Find(Frame(400, 300, (340, 130, 40, 40, Cyan), (180, 210, 40, 40, Cyan)), Setup());

        Assert.Equal(
            new[] { new FinderTarget(200, 230, true, "ore 1"), new FinderTarget(360, 150, true, "ore 2") },
            targets.Where(t => t.Ore));
    }

    [Fact]
    public void A_single_ore_coloured_sample_is_noise()
    {
        var targets = TargetFinder.Find(Frame(400, 300, (265, 135, 1, 1, Cyan)), Setup());

        Assert.DoesNotContain(targets, t => t.Ore);
        Assert.Equal(13, targets.Count);
        Assert.Contains(targets, t => At(t) == (280, 150));
    }

    [Fact]
    public void A_colour_just_outside_the_tolerance_is_not_ore()
    {
        var outside = new Rgb(60, 220, 189);   // 41 from cyan
        var inside = new Rgb(60, 220, 190);    // 40 from cyan

        Assert.DoesNotContain(TargetFinder.Find(Frame(400, 300, (260, 130, 40, 40, outside)), Setup()), t => t.Ore);
        Assert.Contains(TargetFinder.Find(Frame(400, 300, (260, 130, 40, 40, inside)), Setup()), t => t.Ore);
    }

    [Fact]
    public void An_empty_palette_gives_only_the_grid()
    {
        var targets = TargetFinder.Find(Frame(400, 300, (260, 130, 40, 40, Cyan)), Setup(ore: Array.Empty<OreColour>()));

        Assert.Equal(13, targets.Count);
        Assert.DoesNotContain(targets, t => t.Ore);
        Assert.Contains(targets, t => At(t) == (280, 150));
    }

    [Fact]
    public void Points_whose_outline_box_leaves_the_window_are_dropped()
    {
        var setup = Setup(cx: 30);

        var targets = TargetFinder.Find(Frame(400, 300, (0, 130, 20, 40, Cyan)), setup);

        Assert.Equal(9, targets.Count);                             // the 4 grid points left of x = 20 are gone
        Assert.All(targets, t => Assert.True(setup.BoxFits(t.X, t.Y)));
        Assert.DoesNotContain(targets, t => t.Ore);                 // the patch's centre (10, 150) is too near the edge
    }

    [Fact]
    public void The_grid_is_capped_at_64_nearest_first()
    {
        var targets = TargetFinder.Find(Frame(800, 600), Setup(w: 800, h: 600, cx: 400, cy: 300, radius: 5));

        Assert.Equal(TargetFinder.MaxPoints, targets.Count);        // radius 5 has 81 lattice points
        var d2 = targets.Select(t => (t.X - 400) * (t.X - 400) + (t.Y - 300) * (t.Y - 300)).ToList();
        Assert.Equal(d2.OrderBy(d => d), d2);
        Assert.Equal(20 * 40 * 40, d2.Max());   // 49 within 4 blocks, 12 at 17 and 18, then 3 of the 8 at 20
    }

    [Fact]
    public void Never_more_than_64_points_and_ore_fills_them_first()
    {
        var paint = new List<(int, int, int, int, Rgb)>();
        for (var py = 0; py < 10; py++)
            for (var px = 0; px < 13; px++)
                paint.Add((px * 30, py * 30, 20, 10, Cyan));        // 130 two-sample patches, 108 of them placeable

        var targets = TargetFinder.Find(Frame(400, 300, paint.ToArray()), Setup());

        Assert.Equal(TargetFinder.MaxPoints, targets.Count);
        Assert.All(targets, t => Assert.True(t.Ore));
        var d2 = targets.Select(t => (t.X - 200) * (t.X - 200) + (t.Y - 150) * (t.Y - 150)).ToList();
        Assert.Equal(d2.OrderBy(d => d), d2);
    }

    [Fact]
    public void A_frame_at_another_size_gives_the_same_points()
    {
        var measured = TargetFinder.Find(Frame(400, 300, (260, 130, 40, 40, Cyan)), Setup());
        var doubled = TargetFinder.Find(Frame(800, 600, (520, 260, 80, 80, Cyan)), Setup());

        Assert.Equal(measured, doubled);
    }
}
```

Create `tests/RoRoRo.UrOcr.Tests/Engine/FramePixelsTests.cs`:

```csharp
using System.Drawing;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

public class FramePixelsTests
{
    [Fact]
    public void FromBitmap_reads_every_pixel_as_rgb()
    {
        using var bmp = new Bitmap(3, 2);
        bmp.SetPixel(2, 1, Color.FromArgb(255, 200, 100, 50));

        var frame = FramePixels.FromBitmap(bmp);

        Assert.Equal((3, 2), (frame.Width, frame.Height));
        Assert.Equal(new Rgb(200, 100, 50), frame.At(2, 1));
        Assert.Equal(new Rgb(0, 0, 0), frame.At(0, 0));
    }
}
```

In `tests/RoRoRo.UrOcr.Tests/Engine/SpotReaderTests.cs`, add at the end of the class:

```csharp
    [Fact]
    public void ReadFrame_captures_the_whole_client_area()
    {
        var paint = new PaintedCapture();

        var frame = new SpotReader(paint, new Metrics()).ReadFrame(7)!;

        Assert.Equal(new RegionRect(0, 0, 800, 599), Assert.Single(paint.Regions));
        Assert.Equal((800, 599), (frame.Width, frame.Height));
        Assert.Equal(Grey, frame.At(400, 300));
    }

    [Fact]
    public void ReadFrame_of_a_window_it_cannot_find_is_null() =>
        Assert.Null(new SpotReader(new PaintedCapture(), new Metrics { Gone = true }).ReadFrame(7));
```

- [ ] **Step 2: Run the new tests to verify they fail**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~TargetFinderTests|FullyQualifiedName~FramePixelsTests|FullyQualifiedName~SpotReaderTests"`
Expected: build FAILS with CS0246 (`FramePixels`, `TargetFinder`, `FinderTarget`) and CS1061 (`ReadFrame`).

- [ ] **Step 3: Create FramePixels**

Create `Engine/FramePixels.cs`:

```csharp
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

/// <summary>A captured frame as packed 0xRRGGBB pixels, row by row. Pure, so the ore finder runs on
/// a test's hand-painted frame exactly as on a capture.</summary>
public sealed class FramePixels
{
    private readonly int[] _rgb;

    public FramePixels(int width, int height, int[] rgb)
    {
        if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width), "A frame needs at least one pixel.");
        if (rgb.Length != width * height)
            throw new ArgumentException($"Expected {width * height} pixels, got {rgb.Length}.", nameof(rgb));
        Width = width;
        Height = height;
        _rgb = rgb;
    }

    public int Width { get; }
    public int Height { get; }

    public Rgb At(int x, int y)
    {
        var p = _rgb[y * Width + x];
        return new Rgb((p >> 16) & 0xFF, (p >> 8) & 0xFF, p & 0xFF);
    }

    public static int Pack(Rgb c) => (c.R << 16) | (c.G << 8) | c.B;

    public static FramePixels FromBitmap(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[data.Stride * bmp.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var rgb = new int[bmp.Width * bmp.Height];
            for (var y = 0; y < bmp.Height; y++)
                for (var x = 0; x < bmp.Width; x++)
                {
                    var i = y * data.Stride + x * 4;   // B, G, R, A
                    rgb[y * bmp.Width + x] = (bytes[i + 2] << 16) | (bytes[i + 1] << 8) | bytes[i];
                }
            return new FramePixels(bmp.Width, bmp.Height, rgb);
        }
        finally { bmp.UnlockBits(data); }
    }
}
```

- [ ] **Step 4: Create TargetFinder**

Create `Engine/TargetFinder.cs`:

```csharp
using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

/// <summary>One ClearAt point in measured-client pixels. Ore marks an ore patch's centre.</summary>
public sealed record FinderTarget(int X, int Y, bool Ore, string Label);

/// <summary>
/// Where to clear on a calm frame (spec "Reach, measured, and the ore finder"). Ore first: samples
/// every Pitch / 4 pixels, keeps those within the tolerance of a palette colour, clusters adjacent
/// hits (4-connected; a patch needs MinPatchSamples) and takes each patch's centre. Then stone: a grid
/// every Pitch out to RadiusBlocks blocks around the character, less the points inside an ore patch.
/// Points whose outline box would leave the client are dropped. Ore nearest first, then stone nearest
/// first, at most MaxPoints. Every coordinate is in the finder's measured client pixels; the frame
/// may be another size and is sampled scaled.
/// </summary>
public static class TargetFinder
{
    public const int MaxPoints = BridgeContract.MaxClearAtPoints;
    /// <summary>A block at Pitch / 4 is about 16 samples; one lone hit is a sparkle, not ore.</summary>
    public const int MinPatchSamples = 2;

    public static IReadOnlyList<FinderTarget> Find(FramePixels frame, FinderSetup f)
    {
        var step = Math.Max(1, f.Pitch / 4);
        var cols = f.ClientW / step;
        var rows = f.ClientH / step;
        int Centre(int cell) => cell * step + step / 2;

        var hit = new bool[cols, rows];
        for (var gy = 0; gy < rows; gy++)
            for (var gx = 0; gx < cols; gx++)
                hit[gx, gy] = IsOre(Sample(frame, f, Centre(gx), Centre(gy)), f);

        var inOre = new bool[cols, rows];
        var seen = new bool[cols, rows];
        var ore = new List<(int X, int Y)>();
        var stack = new Stack<(int X, int Y)>();
        for (var gy = 0; gy < rows; gy++)
            for (var gx = 0; gx < cols; gx++)
            {
                if (!hit[gx, gy] || seen[gx, gy]) continue;
                var patch = new List<(int X, int Y)>();
                seen[gx, gy] = true;
                stack.Push((gx, gy));
                while (stack.Count > 0)
                {
                    var (x, y) = stack.Pop();
                    patch.Add((x, y));
                    foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                    {
                        if (nx < 0 || ny < 0 || nx >= cols || ny >= rows || seen[nx, ny] || !hit[nx, ny]) continue;
                        seen[nx, ny] = true;
                        stack.Push((nx, ny));
                    }
                }
                if (patch.Count < MinPatchSamples) continue;
                foreach (var (x, y) in patch) inOre[x, y] = true;
                ore.Add((Round(patch.Average(p => Centre(p.X))), Round(patch.Average(p => Centre(p.Y)))));
            }

        var stone = new List<(int X, int Y)>();
        var r = f.RadiusBlocks;
        for (var j = -r; j <= r; j++)
            for (var i = -r; i <= r; i++)
            {
                if (i * i + j * j > r * r) continue;
                var x = f.CenterX + i * f.Pitch;
                var y = f.CenterY + j * f.Pitch;
                if (!f.BoxFits(x, y)) continue;
                int cx = x / step, cy = y / step;
                if (cx < cols && cy < rows && inOre[cx, cy]) continue;
                stone.Add((x, y));
            }

        return Nearest(ore.Where(p => f.BoxFits(p.X, p.Y)), f)
            .Select((p, n) => new FinderTarget(p.X, p.Y, true, $"ore {n + 1}"))
            .Concat(Nearest(stone, f).Select((p, n) => new FinderTarget(p.X, p.Y, false, $"stone {n + 1}")))
            .Take(MaxPoints)
            .ToList();
    }

    private static IEnumerable<(int X, int Y)> Nearest(IEnumerable<(int X, int Y)> points, FinderSetup f) =>
        points
            .OrderBy(p => (long)(p.X - f.CenterX) * (p.X - f.CenterX) + (long)(p.Y - f.CenterY) * (p.Y - f.CenterY))
            .ThenBy(p => p.Y)
            .ThenBy(p => p.X);

    private static int Round(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);

    private static Rgb Sample(FramePixels frame, FinderSetup f, int x, int y)
    {
        var fx = Math.Clamp((int)((long)x * frame.Width / f.ClientW), 0, frame.Width - 1);
        var fy = Math.Clamp((int)((long)y * frame.Height / f.ClientH), 0, frame.Height - 1);
        return frame.At(fx, fy);
    }

    private static bool IsOre(Rgb c, FinderSetup f)
    {
        foreach (var o in f.Ore)
            if (ColorMatcher.Distance(c, o.Rgb) <= f.OreToleranceRgb) return true;
        return false;
    }
}
```

- [ ] **Step 5: Let the reader capture a whole calm frame**

In `Engine/SpotReader.cs`, replace the interface:

```csharp
public interface ISpotReader
{
    /// <summary>Samples each ring spot in <paramref name="pid"/>'s window. Null when the window
    /// cannot be resolved (hidden, closed, mid-resize).</summary>
    IReadOnlyList<SpotReading>? Read(int pid, IReadOnlyList<Trigger> spots);
}
```

with:

```csharp
public interface ISpotReader
{
    /// <summary>Samples each ring spot in <paramref name="pid"/>'s window. Null when the window
    /// cannot be resolved (hidden, closed, mid-resize).</summary>
    IReadOnlyList<SpotReading>? Read(int pid, IReadOnlyList<Trigger> spots);

    /// <summary>The whole client area of <paramref name="pid"/>'s window, for the ore finder. Null
    /// when it cannot be captured. The default is null so a reader without frames (the runner
    /// tests' fakes) still compiles; SpotReader captures one.</summary>
    FramePixels? ReadFrame(int pid) => null;
}
```

and add to `SpotReader`, after `Read`:

```csharp

    public FramePixels? ReadFrame(int pid)
    {
        if (pid == 0) return null;
        var hwnd = metrics.HwndForPid(pid);
        if (hwnd == IntPtr.Zero) return null;
        if (metrics.ClientOrigin(hwnd) is not { } origin || metrics.ClientSize(hwnd) is not { } size
            || size.W < 1 || size.H < 1) return null;
        using var bmp = capture.Capture(new RegionRect(origin.X, origin.Y, size.W, size.H));
        return FramePixels.FromBitmap(bmp);
    }
```

- [ ] **Step 6: Run the new tests to verify they pass**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~TargetFinderTests|FullyQualifiedName~FramePixelsTests|FullyQualifiedName~SpotReaderTests"`
Expected: PASS (10 finder, 1 frame, 5 spot reader).

- [ ] **Step 7: Run the full suite**

Run: `dotnet build rororo-ur-ocr.csproj` then `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj`
Expected: build clean; **392 passed** (379 + 13).

- [ ] **Step 8: Commit**

```bash
git add Engine/FramePixels.cs Engine/TargetFinder.cs Engine/SpotReader.cs tests/RoRoRo.UrOcr.Tests/Engine/Frames.cs tests/RoRoRo.UrOcr.Tests/Engine/TargetFinderTests.cs tests/RoRoRo.UrOcr.Tests/Engine/FramePixelsTests.cs tests/RoRoRo.UrOcr.Tests/Engine/SpotReaderTests.cs
git commit -m "feat(engine): target finder, ore patches then a block grid on a calm frame" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: One ClearAt replaces the eight Clear spots at the target layer

**Files:**
- Modify: `Engine/PulseLoop.cs`
- Modify: `tests/RoRoRo.UrOcr.Tests/Engine/PulseLoopFixtures.cs`
- Create: `tests/RoRoRo.UrOcr.Tests/Engine/PulseLoopFinderTests.cs`
- Modify: `README.md`, `CHANGELOG.md`

**Interfaces:**
- Consumes: `MacroCall.BeginClearAt(ClearAtClient, IReadOnlyList<ClearAtPoint>, ClearAtOutline)`, `ClearAtClient`, `ClearAtPoint`, `ClearAtOutline`, test fake `ScriptedMacros.ClearAtId/ClearAts/ClearAtReplies` (Task 1); `FinderSetup`, `OutlineBox`, `OreColour`, `RingDefinition.Finders`, `PulseValidation.FinderFor(RingDefinition, int)` (Task 2); `FramePixels`, `TargetFinder.Find`, `FinderTarget`, `ISpotReader.ReadFrame(int)`, test helper `Frames` (Task 3); existing `PulseLoop(PulseConfig, IReadOnlyList<RingDefinition>, IReadOnlyList<Trigger>, ISpotReader, IMacroRunClient, IClock, Action<string>)`, `TickAsync(bool foreground, int pid, CancellationToken ct, Func<bool>? inFront = null)`, `PulseFixtures.Ring/Spots/Config/Grey`, `ScriptedReader.All`, `ScriptedMacros.Finished/Skipped/CheckFailed/Stopped/Unknown`.
- Produces: `PulseLoop` behaviour only (no new public API). Test fixtures: `PulseFixtures.Cyan`, `PulseFixtures.Finder(string layer = "grey")` (client 800x599, pitch 50, centre 390,340, radius 2, outline 50x50, cyan crystal, tolerance 40: 13 grid points on a plain frame), `PulseFixtures.RingWithFinder(string layer = "grey")`, `PulseFixtures.Calm(params (int X, int Y, int W, int H, Rgb Colour)[] paint)`; `ScriptedReader.Frame`, `ScriptedReader.FramePids`.

- [ ] **Step 1: Extend the loop fixtures**

In `tests/RoRoRo.UrOcr.Tests/Engine/PulseLoopFixtures.cs`, add to `PulseFixtures` after `Sky`:

```csharp
    public static readonly Rgb Cyan = new(60, 220, 230);

    /// <summary>A finder for one layer on the 800x599 client: 50 px blocks, the character at 390,340,
    /// radius 2 (13 grid points on a plain frame), cyan crystal within 40.</summary>
    public static FinderSetup Finder(string layer = "grey") => new(layer, 800, 599, Pitch: 50, CenterX: 390, CenterY: 340,
        RadiusBlocks: 2, Outline: new OutlineBox(50, 50), Ore: new[] { new OreColour("cyan crystal", Cyan) }, OreToleranceRgb: 40);

    public static RingDefinition RingWithFinder(string layer = "grey") => Ring() with { Finders = new[] { Finder(layer) } };

    /// <summary>An 800x599 calm frame of grey rock with the given rectangles painted over it.</summary>
    public static FramePixels Calm(params (int X, int Y, int W, int H, Rgb Colour)[] paint)
    {
        var px = Frames.Solid(800, 599, Grey);
        foreach (var (x, y, w, h, c) in paint) Frames.Fill(px, 800, x, y, w, h, c);
        return new FramePixels(800, 599, px);
    }
```

and add to `ScriptedReader`, after `Reads`:

```csharp
    public FramePixels? Frame { get; set; }
    public List<int> FramePids { get; } = new();

    public FramePixels? ReadFrame(int pid)
    {
        FramePids.Add(pid);
        return Frame;
    }
```

- [ ] **Step 2: Write the failing loop tests**

Create `tests/RoRoRo.UrOcr.Tests/Engine/PulseLoopFinderTests.cs`:

```csharp
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

/// <summary>
/// The pulse loop with an ore finder on its aim layer (grey, layer 3): one ClearAt per pass instead
/// of the eight Clear spots. Every unscripted playback finishes on its first poll.
/// </summary>
public class PulseLoopFinderTests
{
    private sealed record Rig(PulseLoop Loop, ScriptedMacros Macros, ScriptedReader Reader, PulseClock Clock, List<string> Log);

    private static Rig Build(RingDefinition? ring = null, FramePixels? frame = null)
    {
        var macros = new ScriptedMacros();
        var reader = new ScriptedReader { Next = ScriptedReader.All(PulseFixtures.Grey), Frame = frame ?? PulseFixtures.Calm() };
        var clock = new PulseClock();
        var log = new List<string>();
        var loop = new PulseLoop(PulseFixtures.Config(), new[] { ring ?? PulseFixtures.RingWithFinder() },
            PulseFixtures.Spots(), reader, macros, clock, log.Add);
        return new Rig(loop, macros, reader, clock, log);
    }

    private static Task Tick(Rig r, bool front = true) => r.Loop.TickAsync(front, 7, CancellationToken.None);

    private static async Task Ticks(Rig r, int n)
    {
        for (var i = 0; i < n; i++) await Tick(r);
    }

    /// <summary>Auto Mine on, ride 2 s, Auto Mine off, settle 1 s, read: t = 3 s, ClearAt started.</summary>
    private static async Task FirstRead(Rig r)
    {
        await Tick(r);
        await Tick(r);
        r.Clock.Advance(2000);
        await Tick(r);
        await Tick(r);
        r.Clock.Advance(1000);
        await Tick(r);
    }

    [Fact]
    public async Task Starts_saying_it_clears_with_the_ore_finder()
    {
        var rig = Build();

        await Tick(rig);

        Assert.Contains(rig.Log, l => l.StartsWith("started") && l.Contains("with the ore finder (50 px blocks, radius 2)"));
    }

    [Fact]
    public async Task At_the_target_one_ClearAt_replaces_the_eight_Clear_spots()
    {
        var rig = Build();

        await FirstRead(rig);

        Assert.Equal(PulseState.Clearing, rig.Loop.State);
        Assert.Equal(new[] { "id-on", "id-off", ScriptedMacros.ClearAtId }, rig.Macros.RunIds);
        var req = Assert.Single(rig.Macros.ClearAts);
        Assert.Equal("42", req.Target);
        Assert.Equal(new ClearAtClient(800, 599), req.Client);          // the measured client, not the live one
        Assert.Equal(new ClearAtOutline(50, 50, 60, 225), req.Outline);
        Assert.Equal(13, req.Points.Count);
        Assert.Equal(new ClearAtPoint(390, 340, "stone 1"), req.Points[0]);
        Assert.Equal(new[] { 7 }, rig.Reader.FramePids);
    }

    [Fact]
    public async Task Ore_goes_first_in_the_points()
    {
        var rig = Build(frame: PulseFixtures.Calm((465, 315, 50, 50, PulseFixtures.Cyan)));

        await FirstRead(rig);

        var req = Assert.Single(rig.Macros.ClearAts);
        Assert.Equal(new ClearAtPoint(492, 336, "ore 1"), req.Points[0]);
        Assert.Equal(13, req.Points.Count);
        Assert.DoesNotContain(req.Points, p => (p.X, p.Y) == (490, 340));    // the grid point inside the ore
        Assert.Contains(rig.Log, l => l.EndsWith("is the target: clearing at 13 points (1 ore, 12 stone)"));
    }

    [Fact]
    public async Task A_finished_ClearAt_reads_again_without_riding()
    {
        var rig = Build();
        await FirstRead(rig);

        await Tick(rig);                  // ClearAt finished: settle, Auto Mine stays off
        Assert.Equal(PulseState.Pausing, rig.Loop.State);
        Assert.Contains(rig.Log, l => l == "cleared at the ore finder's points (13 tried): reading again");

        rig.Clock.Advance(1000);
        await Tick(rig);
        Assert.Equal(2, rig.Reader.Reads);
        Assert.Equal(new[] { "id-on", "id-off", ScriptedMacros.ClearAtId, ScriptedMacros.ClearAtId }, rig.Macros.RunIds);
    }

    [Fact]
    public async Task A_skipped_ClearAt_rides_a_burst()
    {
        var rig = Build();
        rig.Macros.Script(ScriptedMacros.ClearAtId, ScriptedMacros.Skipped);
        await FirstRead(rig);

        await Tick(rig);

        Assert.Equal(PulseState.Bursting, rig.Loop.State);
        Assert.Equal("id-on", rig.Macros.RunIds.Last());
        Assert.Contains(rig.Log, l => l == "nothing in reach to clear (all 13 points skipped): riding a burst");
        Assert.DoesNotContain(rig.Macros.RunIds, id => id.StartsWith("id-clear-"));
    }

    [Fact]
    public async Task A_finished_ClearAt_restarts_the_rock_cap()
    {
        var rig = Build();
        rig.Macros.Script(ScriptedMacros.ClearAtId, ScriptedMacros.Skipped);
        await FirstRead(rig);             // t = 3 s: the grey layer starts, ClearAt started
        await Tick(rig);                  // skipped: Auto Mine on started
        await Tick(rig);                  // on finished: ride timer
        rig.Clock.Advance(240_000);
        await Tick(rig);                  // Auto Mine off started
        await Tick(rig);                  // off finished: settle
        rig.Clock.Advance(1000);
        await Tick(rig);                  // t = 244 s: read, ClearAt started
        rig.Macros.Script(ScriptedMacros.ClearAtId, ScriptedMacros.Finished);
        await Tick(rig);                  // it cleared something at 244 s: settle

        rig.Clock.Advance(60_000);
        await Tick(rig);                  // t = 304 s: 60 s since the clear, not 301 s since the layer

        Assert.Equal(PulseState.Clearing, rig.Loop.State);
        Assert.DoesNotContain("id-top", rig.Macros.RunIds);
        Assert.Equal(3, rig.Macros.ClearAts.Count);
    }

    [Fact]
    public async Task Skipped_ClearAts_still_trip_the_rock_cap()
    {
        var rig = Build();
        rig.Macros.Script(ScriptedMacros.ClearAtId, ScriptedMacros.Skipped);
        await FirstRead(rig);             // t = 3 s
        await Tick(rig);                  // skipped: Auto Mine on started
        await Tick(rig);                  // on finished: ride timer

        rig.Clock.Advance(300_000);
        await Tick(rig);                  // Auto Mine off
        await Tick(rig);                  // settle
        rig.Clock.Advance(1000);
        await Tick(rig);                  // t = 304 s: 5 minutes on grey without clearing

        Assert.Equal(PulseState.GoingToTop, rig.Loop.State);
        Assert.Equal("id-top", rig.Macros.RunIds.Last());
        Assert.Contains(rig.Log, l => l == "Went to top: 5 minutes on the grey layer");
    }

    [Fact]
    public async Task Without_a_finder_for_the_aim_layer_it_clears_the_eight_spots()
    {
        var rig = Build(PulseFixtures.RingWithFinder("navy"));

        await FirstRead(rig);

        Assert.Equal("id-clear-N", rig.Macros.RunIds.Last());
        Assert.Empty(rig.Macros.ClearAts);
        Assert.Empty(rig.Reader.FramePids);
        Assert.Contains(rig.Log, l => l.StartsWith("started") && l.Contains("with the 8 Clear spot macros"));
    }

    [Fact]
    public async Task An_Ur_Task_without_ClearAt_stops_the_loop_and_says_so()
    {
        var rig = Build();
        rig.Macros.ClearAtReplies.Enqueue(new RunMacroResponse(false, null, false, "refused", "Unknown method 'ClearAt'."));

        await FirstRead(rig);

        Assert.Equal(PulseState.Stopped, rig.Loop.State);
        Assert.Contains("Unknown method 'ClearAt'", rig.Loop.StopReason);
        Assert.Contains("Auto Mine is off", rig.Loop.StopReason);
        await Ticks(rig, 3);
        Assert.Single(rig.Macros.ClearAts);
    }

    [Fact]
    public async Task A_ClearAt_whose_check_could_not_run_stops_the_loop()
    {
        var rig = Build();
        rig.Macros.Script(ScriptedMacros.ClearAtId, ScriptedMacros.CheckFailed);

        await FirstRead(rig);
        await Tick(rig);

        Assert.Equal(PulseState.Stopped, rig.Loop.State);
        Assert.Contains("could not check", rig.Loop.StopReason);
        Assert.Contains("Auto Mine is off", rig.Loop.StopReason);
    }

    [Fact]
    public async Task A_ClearAt_stopped_in_Ur_Task_stops_the_loop()
    {
        var rig = Build();
        rig.Macros.Script(ScriptedMacros.ClearAtId, ScriptedMacros.Stopped);

        await FirstRead(rig);
        await Tick(rig);

        Assert.Equal(PulseState.Stopped, rig.Loop.State);
        Assert.Contains("stopped in Ur Task", rig.Loop.StopReason);
    }

    [Fact]
    public async Task A_window_it_cannot_capture_waits_in_Reading_and_logs_once()
    {
        var rig = Build();
        rig.Reader.Frame = null;

        await FirstRead(rig);
        await Tick(rig);

        Assert.Equal(PulseState.Reading, rig.Loop.State);
        Assert.Equal(2, rig.Reader.FramePids.Count);
        Assert.Equal(2, rig.Macros.Runs.Count);
        Assert.Single(rig.Log, l => l.Contains("could not capture the window"));
    }

    [Fact]
    public async Task A_lost_ClearAt_stops_without_running_it_again()
    {
        var rig = Build();
        rig.Macros.Script(ScriptedMacros.ClearAtId, ScriptedMacros.Unknown);

        await FirstRead(rig);
        await Tick(rig);

        Assert.Equal(PulseState.Stopped, rig.Loop.State);
        Assert.Contains("no longer knows", rig.Loop.StopReason);
        await Ticks(rig, 3);
        Assert.Equal(new[] { "id-on", "id-off", ScriptedMacros.ClearAtId }, rig.Macros.RunIds);
    }
}
```

- [ ] **Step 3: Run the new tests to verify they fail**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~PulseLoopFinderTests"`
Expected: FAIL. It builds (the fixtures compile against Tasks 1 to 3), then most tests fail because the loop still runs `id-clear-N`, e.g. `At_the_target_one_ClearAt_replaces_the_eight_Clear_spots` with `Assert.Equal() Failure` on `RunIds`. `Without_a_finder_for_the_aim_layer_it_clears_the_eight_spots` and `A_lost_ClearAt...` may fail on the start line or pass by accident; that is fine.

- [ ] **Step 4: Give the loop its finder and a start line that says which clear it uses**

In `Engine/PulseLoop.cs`, replace the class summary's sentence `At it: Clearing, one Clear spot per ring` through `spot, ore first; Ur Task skips a spot with no outline.` with:

```csharp
/// At it: Clearing. With an ore finder for the aim layer, one ClearAt call carries the finder's points
/// (ore patches, then a block grid) from a calm frame; without one, one Clear spot per ring spot,
/// ore first. Ur Task skips a point or spot with no outline.
```

Add these fields after `private readonly MacroCall _call;`:

```csharp
    private readonly FinderSetup? _finder;            // the aim layer's ore finder; null clears the 8 spots
    private IReadOnlyList<FinderTarget>? _targets;     // this pass's ClearAt points; null on a Clear spot pass
    private bool _clearAtEnded;                        // this pass's ClearAt has ended
    private bool _frameLogged;
```

Replace:

```csharp
        _spots = PulseValidation.SpotsOf(config.RingId, triggers);
        _call = new MacroCall(macros, config.AccountUserId.ToString(CultureInfo.InvariantCulture), clock, log);
```

with:

```csharp
        _spots = PulseValidation.SpotsOf(config.RingId, triggers);
        _finder = _ring is null ? null : PulseValidation.FinderFor(_ring, config.AimLayer);
        _call = new MacroCall(macros, config.AccountUserId.ToString(CultureInfo.InvariantCulture), clock, log);
```

Replace:

```csharp
        var mode = config.Mode == PulseMode.OneAbove ? "one above" : "top";
        _log($"started: ring {config.RingId}, target layer {config.TargetLayer} ({mode}), clearing on {_ring!.Layers[config.AimLayer - 1].Name}");
```

with:

```csharp
        var mode = config.Mode == PulseMode.OneAbove ? "one above" : "top";
        var how = _finder is null
            ? "the 8 Clear spot macros"
            : $"the ore finder ({_finder.Pitch} px blocks, radius {_finder.RadiusBlocks})";
        _log($"started: ring {config.RingId}, target layer {config.TargetLayer} ({mode}), clearing on {_ring!.Layers[config.AimLayer - 1].Name} with {how}");
```

- [ ] **Step 5: Read a calm frame and plan one ClearAt at the target**

In `Read`, replace:

```csharp
        var order = PulseOrder.Rank(samples, ring.Layers[number - 1].Rock);
        _queue.Clear();
        foreach (var o in order) _queue.Enqueue(o);
        _spot = -1;
        _cleared.Clear();
        _skipped.Clear();
        _log($"{seen} is the target: clearing {string.Join(" ", order.Select(SpotName))}");
        return Enter(PulseState.Clearing);
```

with:

```csharp
        _queue.Clear();
        _spot = -1;
        _cleared.Clear();
        _skipped.Clear();
        _clearAtEnded = false;

        if (_finder is { } finder)
        {
            var frame = _reader.ReadFrame(pid);
            if (frame is null)
            {
                if (!_frameLogged) _log("could not capture the window for the ore finder (hidden or gone); waiting");
                _frameLogged = true;
                return false;
            }
            _frameLogged = false;
            var targets = TargetFinder.Find(frame, finder);
            if (targets.Count == 0)
            {
                _log($"{seen} is the target, but the ore finder has no point inside the window: riding a burst");
                return Enter(PulseState.Bursting);
            }
            _targets = targets;
            var ore = targets.Count(t => t.Ore);
            _log($"{seen} is the target: clearing at {targets.Count} points ({ore} ore, {targets.Count - ore} stone)");
            return Enter(PulseState.Clearing);
        }

        _targets = null;
        var order = PulseOrder.Rank(samples, ring.Layers[number - 1].Rock);
        foreach (var o in order) _queue.Enqueue(o);
        _log($"{seen} is the target: clearing {string.Join(" ", order.Select(SpotName))}");
        return Enter(PulseState.Clearing);
```

- [ ] **Step 6: Run the ClearAt, count finished as cleared, skipped as nothing**

Replace `ClearNext`:

```csharp
    private bool ClearNext()
    {
        if (_spot < 0)
        {
            if (_queue.Count == 0) return EndPass();
            _spot = _queue.Dequeue();
        }
        return Begin(M.Clear[_spot], PulseMacroNames.Clear(SpotName(_spot)));
    }
```

with:

```csharp
    private bool ClearNext()
    {
        if (_targets is { } targets)
        {
            if (_clearAtEnded) return EndPass();
            var f = _finder!;
            _call.BeginClearAt(new ClearAtClient(f.ClientW, f.ClientH),
                targets.Select(t => new ClearAtPoint(t.X, t.Y, t.Label)).ToList(),
                new ClearAtOutline(f.Outline.W, f.Outline.H, f.Outline.MinCount, f.Outline.WhiteMin));
            return true;
        }
        if (_spot < 0)
        {
            if (_queue.Count == 0) return EndPass();
            _spot = _queue.Dequeue();
        }
        return Begin(M.Clear[_spot], PulseMacroNames.Clear(SpotName(_spot)));
    }
```

At the top of `EndPass`, before `if (_cleared.Count > 0)`, insert:

```csharp
        if (_targets is { } targets)
        {
            if (_cleared.Count > 0)
            {
                _log($"cleared at the ore finder's points ({targets.Count} tried): reading again");
                return Enter(PulseState.Pausing, macroDone: true);   // Auto Mine is still off: just settle
            }
            _log($"nothing in reach to clear (all {targets.Count} points skipped): riding a burst");
            return Enter(PulseState.Bursting);
        }
```

In `OnCallEnded`, replace:

```csharp
        if (State == PulseState.Clearing)
        {
            var name = SpotName(_spot);
```

with:

```csharp
        if (State == PulseState.Clearing)
        {
            // A ClearAt is one playback for the whole pass: finished counts as cleared progress (rock
            // cap, no burst) exactly as a Clear spot does; finished + skipped means nothing cleared.
            var name = _targets is null ? SpotName(_spot) : r.Label;
```

and replace:

```csharp
            _spot = -1;
            return;
        }
```

with:

```csharp
            _spot = -1;
            _clearAtEnded = _targets is not null;
            return;
        }
```

- [ ] **Step 7: Run the loop tests to verify they pass**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~PulseLoop"`
Expected: PASS, all 13 `PulseLoopFinderTests` and every existing `PulseLoopTests` (their ring has no finders, so they still clear the eight spots).

- [ ] **Step 8: Document the finder in the README**

In `README.md`, insert after the paragraph that starts `If Ur Task restarts mid-macro` (end of the "Ore stop pulse" section, before `## Capabilities`):

````markdown
### Ore finder

The eight ring spots touch about 8 of the 70 blocks in view. With an ore finder measured for the layer an account clears on, the pulse looks at the whole calm frame instead: every patch in an ore colour becomes a target, nearest first, then a grid of points one block apart out to 5 blocks around your character, nearest first, 64 points at most. It sends them to Ur Task as one ClearAt call. Ur Task sizes the window to the measured client, hovers each point, checks the white outline, and clears what your pickaxe can reach. A pass that cleared anything reads again; a pass where every point was skipped rides another burst.

The finder goes in the measured file, one entry per layer you clear on, in pixels of the file's `recordedClientW` x `recordedClientH` (the values below show the shape; measure your own):

    "finders": [
      { "layer": "bottom", "pitch": 50, "centerX": 390, "centerY": 340, "radiusBlocks": 5,
        "outline": { "w": 50, "h": 50, "minCount": 60, "whiteMin": 225 },
        "ore": [ { "name": "cyan crystal", "rgb": { "r": 60, "g": 220, "b": 230 } } ],
        "oreToleranceRgb": 40 }
    ]

`layer` names one of the file's `layers`. `pitch` is one block in pixels at that layer, `centerX` and `centerY` the middle of your character, `radiusBlocks` 5 when left out. `outline` is Ur Task's outline check: at most 120 pixels a side, `whiteMin` 1 to 255, `minCount` no more than the box's pixels. A patch within `oreToleranceRgb` of an `ore` colour is ore. `ring-fit.ps1 -Write` keeps `finders` when it rewrites `layers`. Import the ring again after adding one; `ring-import.log` names each finder, or says there is none.

A measured file without `finders`, or an account whose clearing layer has none, clears with the eight "Clear spot" macros as before. The ore finder needs an Ur Task with the ClearAt call; an older one makes the pulse stop with `Unknown method 'ClearAt'`.
````

- [ ] **Step 9: Add the CHANGELOG entry**

In `CHANGELOG.md`, under `## 0.6.0 — unreleased` / `### Added`, add after the `**Ore stop pulse.**` bullet:

```markdown
- **Ore finder.** With a finder measured for the layer an account clears on, the pulse no longer clears only the eight ring spots. On the calm frame it finds ore-coloured patches anywhere in view (nearest first), then adds a grid of points one block apart out to 5 blocks around the character (nearest first), up to 64 points, and sends them to Ur Task as one `ClearAt` call. A finished call counts as cleared, like a Clear spot; one where every point was skipped rides a burst. The finder (block size, character centre, radius, outline box, ore colours and tolerance) goes in the measured file under `finders`, one per layer, and `--import-ring` stores it with the ring and names it in `ring-import.log`.
```

and under `### Notes`, add at the end:

```markdown
- A measured file or triggers.json from before the ore finder loads unchanged. Without a finder for its clearing layer, a pulse clears with the eight Clear spot macros.
- The ore finder needs an Ur Task with the `ClearAt` bridge call (the 0.11.0 ore finder work). An older Ur Task makes the pulse stop with `Unknown method 'ClearAt'`.
```

- [ ] **Step 10: Run the full suite**

Run: `dotnet build rororo-ur-ocr.csproj` then `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj`
Expected: build clean; **405 passed** (392 + 13).

- [ ] **Step 11: Commit**

```bash
git add Engine/PulseLoop.cs tests/RoRoRo.UrOcr.Tests/Engine/PulseLoopFixtures.cs tests/RoRoRo.UrOcr.Tests/Engine/PulseLoopFinderTests.cs README.md CHANGELOG.md
git commit -m "feat(pulse): one ClearAt from the ore finder replaces the eight Clear spots" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Live measure with Este at the Mine #8 bottom bowl

This is a checklist, not code. Este plays; the implementer reads numbers off frames, writes the finder, imports, and watches the logs.

**Files:**
- Working folder, never committed: the sweep folder named in `%LOCALAPPDATA%\626Labs\ore-stop-sweep\current.txt` (its `mine8.measured.json`, and a new `finder\` subfolder for the calm frames).
- Committed only if it is already tracked: `docs/reference/ore-stop/mine8.measured.json` (the earlier pulse plan's Task 9 creates it). Check with `git ls-files docs/reference/ore-stop/mine8.measured.json`.

**Interfaces:**
- Consumes: the `finders` schema and `--import-ring` report from Task 2; the pulse from Task 4; `tools/ring-sweep.ps1 -OutDir <dir> -Seconds <s> -Minutes <m>`; an installed Ur Task build that has `ClearAt` (`..\rororo-ur-task\docs\superpowers\plans\2026-09-28-ore-finder-ur-task.md` implemented).
- Produces: one measured finder for the Mine #8 bottom layer (`pitch`, `centerX`, `centerY`, `ore` palette, `oreToleranceRgb`, `outline`), imported and seen working on the main.

**Before Este plays**
- [ ] Tasks 1 to 4 committed on `feat/ore-stop-pulse`; the full suite is at 405.
- [ ] Ur Task with ClearAt built and installed; Ur OCR built from this branch and installed.
- [ ] On the rig, read the live display scale (Settings, Display) and say it out loud. The measured file must be at 100% (`scalePercent` 100). Not 100%: stop and ask Este.
- [ ] `$sweep = (Get-Content (Join-Path $env:LOCALAPPDATA "626Labs\ore-stop-sweep\current.txt") -Raw).Trim()`, and `Test-Path (Join-Path $sweep "mine8.measured.json")` is `True`. Note its `recordedClientW` x `recordedClientH` (expected 800 x 599) and the name of its bottom layer in `layers` (the third, top first).

**Este, in game**
- [ ] Settings: **Hide My Pets on**.
- [ ] The main at the Mine #8 bottom bowl, Auto Mine off, camera top-down, standing still, no card or popup on screen.
- [ ] Ore in view: at least one cyan crystal, one purple or magenta amethyst and one white quartz block if the bowl has them.

**Capture one calm frame**
- [ ] With Roblox in front: `pwsh -File tools\ring-sweep.ps1 -OutDir (Join-Path $sweep "finder") -Seconds 2 -Minutes 0.2`.
- [ ] Pick one frame with no effects (no white flash, cyan burst, swing, "New Item!" card). Check its size equals `recordedClientW` x `recordedClientH`. Different: stop, the window is not at the measured size.

**Read the numbers off that frame**
- [ ] **Pitch:** measure, in pixels, a straight run of N whole floor blocks edge to edge (N at least 4); `pitch = round(px / N)`. Expected about 50. Write down N and px.
- [ ] **Centre:** the pixel at the middle of the block the character stands on: `centerX`, `centerY`. Expected near 390, 340.
- [ ] **Palette:** for each ore type in view, read 3 pixels from the middle of one block's face, away from edges and highlights, and take the middle value per channel. Name each (`cyan crystal`, `amethyst`, `white quartz`).
- [ ] **Tolerance:** for each ore colour, the largest distance from its 3 pixels to the chosen value, plus 10. Take the largest over all ore colours as `oreToleranceRgb`. Then check every ore colour is **more than** that tolerance from every rock colour of all three layers in the measured file (Euclidean RGB). Any closer: say which pair and ask Este before going on; that ore type may have to come out of the palette.
- [ ] **Outline:** `w` and `h` = the pitch (at most 120), `minCount` 60, `whiteMin` 225 (spec, measured 2026-09-28: 385 to 460 near-white pixels on an outlined edge, 0 to 7 on plain rock).
- [ ] **White quartz risk (ruled 2026-09-28):** Ur Task measures the outline as the rise over a baseline count taken with the pointer away, so a white quartz face alone should not light. This step verifies that live. In the live run below, watch the first quartz point in Ur Task's log: if it holds beat after beat after the block is gone, or lights without the pointer on it, press Esc and take white quartz out of the palette; the outline check is Ur Task's, so report it to its half rather than patching here.

**Write, import, run**
- [ ] Add to the sweep folder's `mine8.measured.json`, top level, with the numbers above and the bottom layer's name:
  `"finders": [ { "layer": "<bottom layer name>", "pitch": P, "centerX": X, "centerY": Y, "radiusBlocks": 5, "outline": { "w": P, "h": P, "minCount": 60, "whiteMin": 225 }, "ore": [ { "name": "cyan crystal", "rgb": { "r": R, "g": G, "b": B } }, ... ], "oreToleranceRgb": T } ]`.
- [ ] Close Ur OCR. `RoRoRo.UrOcr.exe --import-ring <sweep>\mine8.measured.json`. `ring-import.log` has `ore finder on <bottom layer name> (P px blocks, K ore colours)`. A failure names the problem: fix the number, import again.
- [ ] The main's pulse targets layer 3, `top` (re-import the pulse file if it changed). Start Ur OCR. `ur-ocr.log` shows `started: ... with the ore finder (P px blocks, radius 5)`.
- [ ] Let it ride to the bowl. At the first read on the bottom layer, `ur-ocr.log` shows `is the target: clearing at N points (k ore, m stone)` with k at least 1 when ore is in view. Ur Task's log shows `ClearAt (N points)` and each point by label.
- [ ] Watch the first `ore 1`: the pointer lands on an ore block, the outline appears, the block breaks in beats. Then stone points in widening rings around the character. Out-of-reach wall blocks are skipped in about 300 ms each.
- [ ] After a pass that cleared something: `cleared at the ore finder's points (N tried): reading again`, no Auto Mine toggle. After a pass that skipped everything: `nothing in reach to clear (all N points skipped): riding a burst`.
- [ ] Press Esc during a ClearAt once: the pulse stops with `stopped in Ur Task`, and says Auto Mine is off.
- [ ] An account with no finder for its clearing layer (an alt on target 1 or 2) still starts `with the 8 Clear spot macros`.

**Record**
- [ ] Write the measured numbers (N and px for the pitch, centre, each ore colour's three pixels and chosen value, tolerance, the closest ore-to-rock distance) into a `finder-notes.txt` in the sweep folder. No account names or user ids.
- [ ] If `git ls-files docs/reference/ore-stop/mine8.measured.json` prints the path: copy the `finders` block into it, check it has no user ids, and commit:

```bash
git add docs/reference/ore-stop/mine8.measured.json
git commit -m "docs(reference): measured ore finder for the Mine #8 bottom layer" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

  If it prints nothing, commit nothing: the measured file lives in the sweep folder until the earlier plan's Task 9 commits it.
- [ ] Hand Este the summary: points per pass, how many were ore, what was cleared, anything that was skipped that should not have been, and the white quartz finding.
