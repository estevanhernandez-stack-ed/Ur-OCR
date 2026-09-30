# Ore Sweep (Ur OCR half) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** At the target layer, a pulse pass clears ore point by point (ClearAt, as now) and then sweeps the stone around the character in one held-button `SweepPath` call to Ur Task, then reads again; a sweep that changed nothing counts as an empty pass.

**Architecture:** Five additive pieces. (1) The bridge client learns `SweepPath` (`SweepPathRequest`, `IMacroRunClient.SweepPathAsync`, `MacroCall.BeginSweep`), followed through the existing `GetPlayback` path. (2) `SweepPath.Build`, pure: square rings 1 to 4 around the character in spiral order from the block east of it, one block per step, never on or through the centre block, HUD points skipped, a margin inside the client, near-side rows toward the bottom edge when the account's toggle is on, closed on the start block. (3) Two per-account settings on `PulseConfig`: `SweepDwellMs` (400) and `SweepNearSide` (true). (4) `SweepChange`, pure: how many swept blocks changed between the calm frame a pass was planned on and the next calm frame. (5) `PulseLoop`: with a read block size and a finder guard, a pass sends only the ore points through ClearAt, then the sweep, then settles and reads; the next read judges the sweep before planning the next pass. Without either, stone goes through ClearAt exactly as before.

**Tech Stack:** C# / .NET 10 (`net10.0-windows10.0.19041.0`), WPF, System.Drawing, System.Text.Json, xUnit 2.9. PowerShell 7 (`pwsh`) for the live task.

**Spec:** `..\rororo-ur-task\docs\superpowers\specs\2026-09-29-ore-stop-sweep-design.md` (binding, sibling repo), including "Later inputs" 5 (the power-ball confound) and 6 (the near-side rows), building on `..\rororo-ur-task\docs\superpowers\specs\2026-09-28-ore-stop-pulse-design.md`. The Ur Task half is `..\rororo-ur-task\docs\superpowers\plans\2026-09-29-ore-sweep-ur-task.md`; this plan only states what it expects from it (Global Constraints, "SweepPath wire contract").

## Global Constraints

- **Paths:** every path here is relative to the Ur-OCR repo root; the sibling repos are `..\rororo-ur-task` and `..\ROROROblox`. Run every command from the Ur-OCR root and check with `git rev-parse --show-toplevel` after any `cd`. No absolute user-profile path goes into any committed file.
- **Branch:** `feat/ore-stop-pulse`, **local only**, at `d655396` when this plan was written. Never push. Never commit to `main`.
- **Build:** `dotnet build rororo-ur-ocr.csproj`. There is no tracked `.sln`; do not create one.
- **Tests:** `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj` (the command CI runs; the untracked `CLAUDE.md` is a GitNexus stub with no test command). Task 0 records the baseline as **B**. Expected after this plan: **B + 48** (Task 1 +6, Task 2 +18, Task 3 +4, Task 4 +5, Task 5 +15). **Known flake:** a test that builds a temp `TriggerStore` can fail once at `File.Move` in `TriggerStore.WriteNow`. Rerun the suite once before debugging; a second failure is real.
- **No new NuGet packages.**
- **Commits:** conventional commits, sentence case after the colon, no emoji. Every message ends with a blank line then `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` (an implementer on another model writes its own name). Never bypass the pre-commit hooks.
- **Never `git add`** the untracked `AGENTS.md`, `CLAUDE.md` or `.gitnexus/`. Add files by path, never `git add -A` / `git add .`.
- **Fixtures are anonymised.** Account ids in tests are `42`; docs use `123456789`. No real user id, account name or display name in any committed file.
- **Version stays 0.6.0** (unreleased). `rororo-ur-ocr.csproj` and `manifest.json` are not touched. The CHANGELOG entry goes under the existing `## 0.6.0 — unreleased`.
- **Copy:** sentence case, second person where it addresses the user, no emoji, em-dashes minimal.
- **SweepPath wire contract** (Ur Task bridge 1.x, additive, Ur Task 0.12.0), pinned byte for byte in both repos:
  - request `{"contractVersion":"1.0","method":"SweepPath","callerPluginId":"626labs.ur-ocr","target":"42","client":{"w":800,"h":599},"path":[{"x":450,"y":300},{"x":450,"y":250},{"x":400,"y":250},{"x":450,"y":300}],"step":50,"dwellMs":400,"guard":{"x":55,"y":289,"w":3,"h":3,"expect":{"r":255,"g":19,"b":90},"tolerance":30}}`. Property order as shown.
  - `client` is the finder's measured client (never the live size); `step` is the pass's integer block size in measured pixels; `dwellMs` the account's `SweepDwellMs`; `guard` the finder's guard.
  - Ur Task refuses, so Ur OCR never sends: fewer than **3** or more than **256** points; a `step` outside **8..240**; a `dwellMs` outside **50..5000**; no guard; a point outside `client`; a point that is not a whole number of `step` px from `path[0]` on both axes; a point equal to the one before it; a path whose last point is not `path[0]`.
  - Ur Task holds the left button from `path[0]` to the end, one real mouse move per point, dwelling `dwellMs` at every point but the last, and releases only on `path[0]`. It samples the guard every 3 points while held: a change goes back to the start block, releases there, and ends the playback `failed` / `check-failed`.
  - Response: `RunMacroResponse`. Then `GetPlayback`: `finished` (a sweep always presses), `stopped`, `failed`. An Ur Task without it answers `{ "ok": false, "reason": "refused", "detail": "Unknown method 'SweepPath'." }`. Ur Task logs the playback as `SweepPath (N points)` and `swept N points in S s`.
- **The path** (spec "The path" and owner decisions 3 and 6): square rings around the finder's centre (the spec's 400,310), **rings 1 to 4** (`SweepPath.Rings`), one block per step, spiral order, starting and ending on the **east first-ring block** `(centre.x + pitch, centre.y)`. The centre block is never a point, and no straight move between two points passes through its inside (`SweepPath.CrossesCentre`); the ring-to-ring step goes outward. HUD points (`HudMask`, as `TargetFinder` uses it) are skipped and the path jumps over them. Every point stays **12 px** (`SweepPath.EdgeMarginPx`) inside the client. With the account's near-side toggle on, rows continue below ring 4 (screen-down, the camera side) across the ring's width, down to the last row whose y is at most `clientH - 1 - 12`; the other three sides stay at ring 4. At most **256** points.
- **Per account** (owner decision 2 and input 6): `PulseConfig.SweepDwellMs`, default **400**, valid **50..5000**; `PulseConfig.SweepNearSide`, default **true**. Stored where every other per-account pulse setting lives: `triggers.json` `pulses`, imported from the pulse file.
- **When a pass sweeps:** at the aim layer (Clearing is only ever entered there, so never on the ride down, decision 4), with a finder for that layer, a block size **read** off the calm frame, a **guard** in the finder, and a path of at least 3 points. Otherwise the pass clears stone through ClearAt exactly as before (an unread block size would make the step a guess; a held button needs the guard).
- **A sweep pass:** ClearAt with the ore points only (skipped when there are none), then the SweepPath, then settle (Auto Mine stays off) and read. Ore cleared (ClearAt `finished`, not `skipped`) counts as progress whatever the sweep did. Otherwise the next calm read judges the sweep: **one or more** swept blocks changed (`SweepChange.Count`, the start block excluded) is progress (rock cap, burst and turns reset); **none** is an empty pass: turn the camera, then ride a burst (the existing `NothingInReach`).
- **Owner input 5 (the power-ball confound):** the main's pickaxe shoots power balls that break blocks on their own, so the drag run did not isolate the pointer. The design stands; Task 6 measures the pointer's share on its own before the sweep is relied on.

## Review Focus

1. **Power balls, not the pointer, may be what broke the blocks** (owner input 5). If the pointer mines less than the drag run suggested, the sweep costs time for little, and a power ball that lands on a swept block reads as progress, so "the sweep broke nothing" can be missed. Expected: the pointer's share measured on its own before the sweep is trusted. No unit test can pin this; it is pinned by Task 6's "The pointer's share" steps (swept points against control points in the same run, on the main and on an account without the power-ball pickaxe).
2. **The near-side rows against the HUD band.** `HudMask` masks everything below y 470 of the 599 px client, across the full width. Expected: the generator reaches within one block of the bottom margin wherever the mask allows it, and never sweeps a HUD point; with today's mask that is at most one extra row, which the live task measures against the hotbar's real extent. Pinned in Task 2 by `The_near_side_extension_reaches_within_a_block_of_the_bottom_margin` (no mask) and `With_the_real_HUD_mask_the_hotbar_line_stops_the_extension`.
3. **Shaft-sized blocks (150 to 240 px)**, where the client and the HUD cut most rings: the path must still never cross the centre block, which a plain "skip the masked point" would do. Expected: a one-block detour, or the point dropped. Pinned in Task 2 by `The_path_never_stands_on_or_passes_through_the_centre_block` (pitches 16 to 240, near side on and off) and `When_clipping_leaves_a_hop_through_the_centre_it_goes_round`.
4. **A calm frame whose block size could not be read, or a finder measured before the guard:** never a sweep; stone through ClearAt as before. Pinned in Task 5 by `An_unread_block_size_never_sweeps` and `Without_a_guard_stone_is_cleared_point_by_point_as_before`.
5. **An Ur Task older than 0.12.0:** the loop stops with one line quoting `Unknown method 'SweepPath'`, and says Auto Mine is off; no retry loop, no silent fallback. Pinned in Task 5 by `An_Ur_Task_without_SweepPath_stops_the_loop_and_says_so`.

Also pinned: the ride down and a layer past the target never sweep (`Never_sweeps_on_the_ride_down`, `Never_sweeps_past_the_target`, Task 5); a sweep stopped at its guard stops the pulse (`A_sweep_stopped_at_its_guard_stops_the_loop_with_Auto_Mine_off`, Task 5).

## Decisions this plan makes where the spec is open

- **The start block is east of the character** `(1, 0)`, not the camera-side block below it: at shaft block sizes (160 px and up) the block below falls under the HUD line (y 470 of 599), while the east block stays inside the game area at every block size up to 240.
- **Spiral order:** right 1, up 1, left 2, down 2, right 3, ... from the centre, which starts ring k at `(k, k - 1)` and ends it at `(k, k)`, so each ring-to-ring step is one block right, outward. Ring 4 ends at its bottom-right corner, so the near-side rows begin one block straight down from it and go back and forth.
- **Detours round the centre:** when the client or the HUD removes points so that the straight move from one point to the next would pass through the centre block's inside, one valid cell is inserted that clears it on both legs (the shortest such detour); if none exists, that point is dropped. The closing move to the start block gets the same treatment.
- **"The frame barely changed"** is read per swept block: a 9 x 9 box (a quarter block wide at pitch 32, `pitch / 8` either side) averaged on the planning frame and the next calm frame, changed when the three channel differences sum to more than **30** (0 to 765, the scale of the 2026-09-29 drag run: broken blocks 45 to 101, noise and undragged blocks 0). The start block is left out (the pointer rests there after the release and its outline shows).
- **The planning frame is the "before":** ore cleared in the same pass already counts as progress, so the comparison only matters when ClearAt pressed nothing.
- **ClearAt with no ore points is not sent.** An all-stone pass goes straight to the sweep.
- **The sweep's label** is `SweepPath (N points)`, the name Ur Task logs.

## File map

| File | Status | Responsibility |
| --- | --- | --- |
| `tests/RoRoRo.UrOcr.IntegrationTests/PluginClientIntegrationTests.cs`, `.../RoRoRo.UrOcr.IntegrationTests.csproj` | modify (Task 0) | the host #223 launch stub; the host path |
| `Ipc/BridgeContract.cs` | modify | `SweepPoint`, `SweepPathRequest`, `MethodSweepPath`, `MaxSweepPoints`, `ForSweepPath` |
| `Ipc/IMacroRunClient.cs`, `Ipc/MacroRunClient.cs` | modify | `SweepPathAsync` |
| `Engine/MacroCall.cs` | modify | `BeginSweep`, `SweepLabel` |
| `Engine/SweepPath.cs` | create | the path generator, `CrossesCentre` |
| `Engine/SweepChange.cs` | create | how many swept blocks changed |
| `Engine/PulseLoop.cs` | modify | the sweep pass and its judgement |
| `Storage/Pulse.cs`, `Storage/PulseValidation.cs` | modify | `SweepDwellMs`, `SweepNearSide` and their range |
| `README.md`, `CHANGELOG.md` | modify | pulse file keys; "Stone is swept"; 0.6.0 entry |
| `tests/RoRoRo.UrOcr.Tests/Ipc/SweepPathClientTests.cs` | create | wire shape and client behaviour |
| `tests/RoRoRo.UrOcr.Tests/Engine/PulseFakes.cs` | modify | `ScriptedMacros.SweepPathAsync`, `Sweeps`, `SweepReplies`, `SweepId` |
| `tests/RoRoRo.UrOcr.Tests/Engine/MacroCallTests.cs` | modify | `BeginSweep` |
| `tests/RoRoRo.UrOcr.Tests/Engine/SweepPathTests.cs`, `.../Engine/SweepChangeTests.cs`, `.../Engine/PulseLoopSweepTests.cs` | create | generator, change, loop |
| `tests/RoRoRo.UrOcr.Tests/Engine/PulseLoopFixtures.cs` | modify | `PulseFixtures.Dot`, `RingWithSweep` |
| `tests/RoRoRo.UrOcr.Tests/Storage/PulseValidationTests.cs`, `.../Storage/PulseStorageTests.cs` | modify | the two settings |

---

### Task 0: The host integration stand-in follows RoRoRo #223

**Files:**
- Modify: `tests/RoRoRo.UrOcr.IntegrationTests/PluginClientIntegrationTests.cs` (`NoOpLauncher`)
- Modify: `tests/RoRoRo.UrOcr.IntegrationTests/RoRoRo.UrOcr.IntegrationTests.csproj` (the host project references)

**Interfaces:**
- Consumes: the host's `IPluginLaunchInvoker` on `ROROROblox` main (`5b5c9e9`, RoRoRo #223): `RequestLaunchAsync(string accountId)` and `RequestLaunchTargetAsync(string accountId, string? shareUrl, long? followUserId)` return `Task<(bool ok, string? failureReason, int processId, string? reasonCode)>`; `GetCurrentServerAsync()` returns `Task<CurrentServerInfo?>`.
- Produces: an integration project that compiles against that host, and the unit-suite baseline **B**.

The stand-in has only `RequestLaunchAsync`, with a 3-tuple: it has been missing the two members the host grew in RoRoRo 1.7 as well. And its host references use `..\..\..\..\ROROROblox`, which resolves one directory above the projects folder, where no host is; the comment beside them says "4 levels up lands at ...\Projects\", but it takes 3. CI never builds this project, so nothing caught either.

- [ ] **Step 1: Record the unit-suite baseline**

Run: `git rev-parse --show-toplevel` (the Ur-OCR root), `git rev-parse --abbrev-ref HEAD` (`feat/ore-stop-pulse`), `git status --short` (only `?? AGENTS.md`, `?? CLAUDE.md`, and possibly `?? .gitnexus/`).
Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj`
Expected: 0 failed. Write the passed count down as **B**.

- [ ] **Step 2: Replace the launcher stand-in**

In `tests/RoRoRo.UrOcr.IntegrationTests/PluginClientIntegrationTests.cs`, replace

```csharp
    private sealed class NoOpLauncher : IPluginLaunchInvoker
    {
        public Task<(bool ok, string? failureReason, int processId)> RequestLaunchAsync(string accountId)
            => Task.FromResult<(bool, string?, int)>((false, "test stub", 0));
    }
```

with

```csharp
    private sealed class NoOpLauncher : IPluginLaunchInvoker
    {
        // RoRoRo v1.32 (host PR #223) added a machine-readable reasonCode to both launch results.
        public Task<(bool ok, string? failureReason, int processId, string? reasonCode)> RequestLaunchAsync(string accountId)
            => Task.FromResult<(bool, string?, int, string?)>((false, "test stub", 0, null));

        // The host's IPluginLaunchInvoker grew launch-to-target and current-server queries in
        // RoRoRo v1.7.0.0; this stand-in never had them. Mirrors Ur Task's stub.
        public Task<(bool ok, string? failureReason, int processId, string? reasonCode)> RequestLaunchTargetAsync(
            string accountId, string? shareUrl, long? followUserId)
            => Task.FromResult<(bool, string?, int, string?)>((false, "test stub", 0, null));

        public Task<CurrentServerInfo?> GetCurrentServerAsync()
            => Task.FromResult<CurrentServerInfo?>(null);
    }
```

- [ ] **Step 3: Point the host references at the sibling checkout**

In `tests/RoRoRo.UrOcr.IntegrationTests/RoRoRo.UrOcr.IntegrationTests.csproj`, replace the comment that starts `<!-- Host app` (it names an absolute path, which is not repeated here) and the two `ProjectReference` lines under it, whose `Include` values start `..\..\..\..\ROROROblox\src\`, with

```xml
    <!-- Host app — gives us PluginHostService, PluginHostStartupService, InProcessPluginEventBus, etc.
         The host checkout sits beside this repo (<parent>\Ur-OCR, <parent>\ROROROblox): three levels
         up from this folder. Build against another checkout (a worktree at a given host commit) with
         -p:RoRoRoHostDir=<absolute path>. -->
    <ProjectReference Include="$(RoRoRoHostDir)\src\ROROROblox.App\ROROROblox.App.csproj" />
    <ProjectReference Include="$(RoRoRoHostDir)\src\ROROROblox.PluginContract\ROROROblox.PluginContract.csproj" />
```

and add, as the second `PropertyGroup` right after the first one closes:

```xml
  <PropertyGroup>
    <RoRoRoHostDir Condition="'$(RoRoRoHostDir)' == ''">$(MSBuildThisFileDirectory)..\..\..\ROROROblox</RoRoRoHostDir>
  </PropertyGroup>
```

The old comment carried an absolute user path; the new one carries none, which the local-path guard checks.

- [ ] **Step 4: Build it against a host with #223**

The sibling `..\ROROROblox` checkout may be on another branch (another session may own it): do not switch it. Build against a detached worktree of its `origin/main` instead, then remove the worktree.

```powershell
git -C ..\ROROROblox fetch origin
git -C ..\ROROROblox worktree add --detach ..\_scratch\rororo-host-223 origin/main
dotnet build tests\RoRoRo.UrOcr.IntegrationTests\RoRoRo.UrOcr.IntegrationTests.csproj "-p:RoRoRoHostDir=$((Resolve-Path ..\_scratch\rororo-host-223).Path)"
git -C ..\ROROROblox worktree remove ..\_scratch\rororo-host-223
```

Expected: `git -C ..\ROROROblox log -1 --format=%h origin/main` is `5b5c9e9` or later, and the build succeeds with 0 errors. If it fails on anything other than `NoOpLauncher` (other host drift in this file), stop and report the errors to the controller; do not fix them in this task.

- [ ] **Step 5: Run the unit suite**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj`
Expected: **B passed**, 0 failed (the unit suite does not reference the integration project).

- [ ] **Step 6: Commit**

```bash
git add tests/RoRoRo.UrOcr.IntegrationTests/PluginClientIntegrationTests.cs tests/RoRoRo.UrOcr.IntegrationTests/RoRoRo.UrOcr.IntegrationTests.csproj
git commit -m "test(host): follow the host's 4-part launch result (RoRoRo #223)" -m "The stand-in also gains the two members the host added in 1.7, and the host references now resolve to the sibling checkout, overridable with RoRoRoHostDir." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 1: SweepPath on the bridge client

**Files:**
- Modify: `Ipc/BridgeContract.cs`
- Modify: `Ipc/IMacroRunClient.cs`, `Ipc/MacroRunClient.cs`
- Modify: `Engine/MacroCall.cs`
- Modify: `tests/RoRoRo.UrOcr.Tests/Engine/PulseFakes.cs`
- Create: `tests/RoRoRo.UrOcr.Tests/Ipc/SweepPathClientTests.cs`
- Modify: `tests/RoRoRo.UrOcr.Tests/Engine/MacroCallTests.cs` (append)

**Interfaces:**
- Consumes (existing): `ClearAtClient`, `ClearAtGuard(int X, int Y, int W, int H, Rgb Expect, int Tolerance)`, `RunMacroResponse`, `BridgeReasons`, `MacroCall.Arm`, `ScriptedMacros.RunAsync`.
- Produces:
  - `public sealed record SweepPoint(int X, int Y);`
  - `public sealed record SweepPathRequest(string ContractVersion, string Method, string CallerPluginId, string Target, ClearAtClient Client, IReadOnlyList<SweepPoint> Path, int Step, int DwellMs, ClearAtGuard Guard);`
  - `BridgeContract.MethodSweepPath = "SweepPath"`, `BridgeContract.MaxSweepPoints = 256`, `BridgeContract.ForSweepPath(string target, ClearAtClient client, IReadOnlyList<SweepPoint> path, int step, int dwellMs, ClearAtGuard guard)`
  - `Task<RunMacroResponse> IMacroRunClient.SweepPathAsync(SweepPathRequest request, CancellationToken ct)` (default refusal)
  - `void MacroCall.BeginSweep(ClearAtClient size, IReadOnlyList<SweepPoint> path, int step, int dwellMs, ClearAtGuard guard)`, `static string MacroCall.SweepLabel(int points)`
  - In the tests: `ScriptedMacros.SweepId = "sweep-path"`, `ScriptedMacros.Sweeps` (`List<SweepPathRequest>`), `ScriptedMacros.SweepReplies` (`Queue<RunMacroResponse>`).

- [ ] **Step 1: Write the failing tests**

Create `tests/RoRoRo.UrOcr.Tests/Ipc/SweepPathClientTests.cs`:

```csharp
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Ipc;

/// <summary>SweepPath against a fake Ur Task on a real in-process pipe. The request is read back as
/// raw JSON so the wire names are pinned; Ur Task's SweepPathMacroTests pins the same shape.</summary>
public class SweepPathClientTests
{
    private static SweepPathRequest Request() => BridgeContract.ForSweepPath("42", new ClearAtClient(800, 599),
        new[] { new SweepPoint(450, 300), new SweepPoint(450, 250), new SweepPoint(400, 250), new SweepPoint(450, 300) },
        50, 400, new ClearAtGuard(55, 289, 3, 3, new Rgb(255, 19, 90), 30));

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
            "{\"contractVersion\":\"1.0\",\"method\":\"SweepPath\",\"callerPluginId\":\"626labs.ur-ocr\",\"target\":\"42\"," +
            "\"client\":{\"w\":800,\"h\":599}," +
            "\"path\":[{\"x\":450,\"y\":300},{\"x\":450,\"y\":250},{\"x\":400,\"y\":250},{\"x\":450,\"y\":300}]," +
            "\"step\":50,\"dwellMs\":400," +
            "\"guard\":{\"x\":55,\"y\":289,\"w\":3,\"h\":3,\"expect\":{\"r\":255,\"g\":19,\"b\":90},\"tolerance\":30}}",
            json);
    }

    [Fact]
    public async Task Sends_SweepPath_and_returns_the_playback_id()
    {
        var (resp, req) = await RoundTrip(
            c => ((IMacroRunClient)c).SweepPathAsync(Request(), default),
            "{\"ok\":true,\"playbackId\":\"pb-9\",\"queued\":false}");

        var root = req.RootElement;
        Assert.Equal("SweepPath", root.GetProperty("method").GetString());
        Assert.Equal("42", root.GetProperty("target").GetString());
        Assert.Equal(4, root.GetProperty("path").GetArrayLength());
        Assert.True(resp.Ok);
        Assert.Equal("pb-9", resp.PlaybackId);
    }

    [Fact]
    public async Task A_refusal_comes_back_with_its_reason_and_detail()
    {
        var (resp, _) = await RoundTrip(
            c => c.SweepPathAsync(Request(), default),
            "{\"ok\":false,\"reason\":\"refused\",\"detail\":\"Unknown method 'SweepPath'.\"}");

        Assert.False(resp.Ok);
        Assert.Equal(BridgeReasons.Refused, resp.Reason);
        Assert.Equal("Unknown method 'SweepPath'.", resp.Detail);
    }

    [Fact]
    public async Task No_Ur_Task_is_not_running_and_never_throws()
    {
        var client = new MacroRunClient(_ => Task.FromResult<Stream?>(null));

        var resp = await client.SweepPathAsync(Request(), default);

        Assert.False(resp.Ok);
        Assert.Equal(BridgeReasons.NotRunning, resp.Reason);
    }

    [Fact]
    public async Task A_client_that_cannot_send_SweepPath_refuses()
    {
        var resp = await ((IMacroRunClient)new OnlyRun()).SweepPathAsync(Request(), default);

        Assert.False(resp.Ok);
        Assert.Equal(BridgeReasons.Refused, resp.Reason);
    }
}
```

In `tests/RoRoRo.UrOcr.Tests/Engine/MacroCallTests.cs`, append inside the class, before its final closing brace:

```csharp
    [Fact]
    public async Task A_sweep_starts_on_the_account_and_is_followed_through_GetPlayback()
    {
        var macros = new ScriptedMacros();
        var call = new MacroCall(macros, "42", new PulseClock(), _ => { });
        var path = new[] { new SweepPoint(450, 300), new SweepPoint(450, 250), new SweepPoint(450, 300) };
        var guard = new ClearAtGuard(55, 289, 3, 3, new Rgb(255, 19, 90), 30);
        call.BeginSweep(Size, path, 50, 400, guard);

        Assert.Equal(CallStatus.Waiting, (await call.StepAsync(true, CancellationToken.None)).Status);
        var req = Assert.Single(macros.Sweeps);
        Assert.Equal(("SweepPath", "42", Size, 50, 400, guard), (req.Method, req.Target, req.Client, req.Step, req.DwellMs, req.Guard));
        Assert.Equal(path, req.Path);

        var end = await call.StepAsync(true, CancellationToken.None);
        Assert.Equal((CallStatus.Done, "SweepPath (3 points)"), (end.Status, end.Label));
        Assert.Equal(new[] { ScriptedMacros.SweepId }, macros.RunIds);
    }
```

In `tests/RoRoRo.UrOcr.Tests/Engine/PulseFakes.cs`, inside `ScriptedMacros`, add after `ClearAtAsync`:

```csharp
    /// <summary>The id a SweepPath playback is listed under in Runs and scripted with in Script.</summary>
    public const string SweepId = "sweep-path";
    public List<SweepPathRequest> Sweeps { get; } = new();
    /// <summary>Answers to SweepPath, used before RunReplies and the default accept.</summary>
    public Queue<RunMacroResponse> SweepReplies { get; } = new();

    public Task<RunMacroResponse> SweepPathAsync(SweepPathRequest request, CancellationToken ct)
    {
        Sweeps.Add(request);
        if (SweepReplies.Count == 0) return RunAsync(SweepId, new[] { request.Target }, null, ct);
        Runs.Add((SweepId, new[] { request.Target }, null));
        return Task.FromResult(SweepReplies.Dequeue());
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~SweepPathClientTests|FullyQualifiedName~MacroCallTests"`
Expected: build FAILS with `CS0246: The type or namespace name 'SweepPathRequest' could not be found` (and `SweepPoint`).

- [ ] **Step 3: Add the request to the contract**

In `Ipc/BridgeContract.cs`, insert after the `ClearAtRequest` record:

```csharp
/// <summary>One SweepPath point in pixels of <see cref="ClearAtClient"/>.</summary>
public sealed record SweepPoint(int X, int Y);

/// <summary>
/// Ur Task's SweepPath (bridge 1.x, additive, Ur Task 0.12.0): one continuous left-button hold along
/// Path, which starts and ends on the start block beside the character and moves a whole number of
/// Step px at a time. Ur Task dwells DwellMs at every point but the last, samples Guard every few
/// points while held, and lets go only on Path[0]. Answered with a RunMacroResponse and followed
/// with GetPlayback like a macro.
/// </summary>
public sealed record SweepPathRequest(string ContractVersion, string Method, string CallerPluginId, string Target,
    ClearAtClient Client, IReadOnlyList<SweepPoint> Path, int Step, int DwellMs, ClearAtGuard Guard);
```

In `public static class BridgeContract`, add below `MaxClearAtPoints`:

```csharp
    public const string MethodSweepPath = "SweepPath";
    /// <summary>Ur Task refuses a SweepPath with more points than this (or fewer than 3).</summary>
    public const int MaxSweepPoints = 256;
```

and below `ForClearAt`:

```csharp
    public static SweepPathRequest ForSweepPath(string target, ClearAtClient client, IReadOnlyList<SweepPoint> path,
        int step, int dwellMs, ClearAtGuard guard)
        => new(ContractVersion, MethodSweepPath, CallerId, target, client, path, step, dwellMs, guard);
```

- [ ] **Step 4: Send it**

In `Ipc/IMacroRunClient.cs`, add below `ClearAtAsync`:

```csharp
    /// <summary>Ur Task's SweepPath: one held-button walk as one playback, followed with
    /// GetPlaybackAsync. The default refuses so a client that cannot send it (the trigger tests'
    /// fakes) still compiles; MacroRunClient sends it.</summary>
    Task<RunMacroResponse> SweepPathAsync(SweepPathRequest request, CancellationToken ct)
        => Task.FromResult(new RunMacroResponse(false, null, false, BridgeReasons.Refused,
            "This client cannot ask Ur Task to sweep."));
```

In `Ipc/MacroRunClient.cs`, add below `ClearAtAsync`:

```csharp
    /// <summary>One SweepPath playback. Ur Task acks with a playback id or a refusal: busy, a bad
    /// path, or "Unknown method 'SweepPath'." from an Ur Task older than 0.12.0.</summary>
    public Task<RunMacroResponse> SweepPathAsync(SweepPathRequest request, CancellationToken ct) =>
        ExchangeAsync(request,
            (reason, detail) => new RunMacroResponse(false, null, false, reason, detail),
            "SweepPath sent; Ur Task did not ack within the tick window.",
            ct);
```

- [ ] **Step 5: Begin and follow it in MacroCall**

In `Engine/MacroCall.cs`, add after `ClearAtLabel`:

```csharp
    /// <summary>One SweepPath for this account, followed through GetPlayback like a macro. The request
    /// is built once, so a busy retry or an interrupted rerun sends the same path.</summary>
    public void BeginSweep(ClearAtClient size, IReadOnlyList<SweepPoint> path, int step, int dwellMs, ClearAtGuard guard)
    {
        var request = BridgeContract.ForSweepPath(target, size, path, step, dwellMs, guard);
        Arm(SweepLabel(path.Count), ct => client.SweepPathAsync(request, ct));
    }

    /// <summary>The name Ur Task's log gives a SweepPath playback.</summary>
    public static string SweepLabel(int points) => $"SweepPath ({points} {(points == 1 ? "point" : "points")})";
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~SweepPathClientTests|FullyQualifiedName~MacroCallTests"`
Expected: PASS, the 5 new client tests, the new MacroCall test and every existing MacroCall test.

- [ ] **Step 7: Run the full suite**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj`
Expected: **B + 6 passed**, 0 failed.

- [ ] **Step 8: Commit**

```bash
git add Ipc/BridgeContract.cs Ipc/IMacroRunClient.cs Ipc/MacroRunClient.cs Engine/MacroCall.cs tests/RoRoRo.UrOcr.Tests/Ipc/SweepPathClientTests.cs tests/RoRoRo.UrOcr.Tests/Engine/MacroCallTests.cs tests/RoRoRo.UrOcr.Tests/Engine/PulseFakes.cs
git commit -m "feat(ipc): send Ur Task a SweepPath and follow it" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: The sweep path

**Files:**
- Create: `Engine/SweepPath.cs`
- Create: `tests/RoRoRo.UrOcr.Tests/Engine/SweepPathTests.cs`

**Interfaces:**
- Consumes: `SweepPoint`, `BridgeContract.MaxSweepPoints` (Task 1); `HudMask.Contains(int x, int y, int width, int height)`; `FinderSetup` (`CenterX`, `CenterY`, `Pitch`, `ClientW`, `ClientH`).
- Produces: `public static class SweepPath` with `Rings = 4`, `EdgeMarginPx = 12`, `MaxPoints = 256`, `MinPoints = 3`, `static IReadOnlyList<SweepPoint> Build(FinderSetup pass, bool nearSide)`, `static IReadOnlyList<SweepPoint> Build(int centerX, int centerY, int pitch, int clientW, int clientH, bool nearSide, Func<int, int, bool> masked)`, `internal static bool CrossesCentre((int I, int J) a, (int I, int J) b)`. A path is empty, or has at least 3 points and starts and ends on `(centerX + pitch, centerY)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/RoRoRo.UrOcr.Tests/Engine/SweepPathTests.cs`:

```csharp
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Ipc;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

public class SweepPathTests
{
    private static readonly Func<int, int, bool> NoHud = (_, _) => false;
    private static bool Hud(int x, int y) => HudMask.Contains(x, y, 800, 599);

    /// <summary>Open ground: a 2000x2000 client with the character in the middle and no HUD.</summary>
    private static IReadOnlyList<SweepPoint> Open(bool nearSide = false) => SweepPath.Build(1000, 1000, 50, 2000, 2000, nearSide, NoHud);

    private static List<(int I, int J)> Cells(IReadOnlyList<SweepPoint> path, int cx, int cy, int pitch) =>
        path.Select(p => ((p.X - cx) / pitch, (p.Y - cy) / pitch)).ToList();

    private static int Ring((int I, int J) c) => Math.Max(Math.Abs(c.I), Math.Abs(c.J));
    private static int Steps((int I, int J) a, (int I, int J) b) => Math.Max(Math.Abs(a.I - b.I), Math.Abs(a.J - b.J));

    [Fact]
    public void On_open_ground_the_path_walks_rings_1_to_4_in_spiral_order_from_the_east_block()
    {
        var cells = Cells(Open(), 1000, 1000, 50);

        Assert.Equal(81, cells.Count);   // 8 + 16 + 24 + 32 blocks, then back to the start
        Assert.Equal(new (int, int)[] { (1, 0), (1, -1), (0, -1), (-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (2, 1), (2, 0) },
            cells.Take(10));
        Assert.Equal((1, 0), cells[^1]);
        Assert.Equal(80, cells.Take(80).Distinct().Count());
    }

    [Fact]
    public void Every_hop_on_open_ground_is_one_block_except_the_closing_return()
    {
        var cells = Cells(Open(), 1000, 1000, 50);

        for (var k = 1; k < cells.Count - 1; k++) Assert.Equal(1, Steps(cells[k - 1], cells[k]));
        Assert.Equal(((4, 4), (1, 0)), (cells[^2], cells[^1]));
    }

    [Fact]
    public void Each_ring_starts_one_block_outward_from_where_the_last_ended()
    {
        var cells = Cells(Open(), 1000, 1000, 50);

        for (var k = 1; k <= 3; k++)
        {
            var end = cells.IndexOf((k, k));
            Assert.Equal((k + 1, k), cells[end + 1]);
        }
        for (var k = 1; k < cells.Count - 1; k++) Assert.True(Ring(cells[k]) >= Ring(cells[k - 1]));
    }

    [Fact]
    public void The_path_never_stands_on_or_passes_through_the_centre_block()
    {
        foreach (var pitch in new[] { 16, 22, 32, 50, 80, 120, 150, 160, 170, 180, 240 })
            foreach (var nearSide in new[] { true, false })
            {
                var path = SweepPath.Build(400, 310, pitch, 800, 599, nearSide, Hud);
                var cells = Cells(path, 400, 310, pitch);

                Assert.True(path.Count >= SweepPath.MinPoints, $"pitch {pitch}: only {path.Count} points");
                Assert.True(path.Count <= SweepPath.MaxPoints);
                Assert.Equal((1, 0), cells[0]);
                Assert.Equal((1, 0), cells[^1]);
                Assert.DoesNotContain((0, 0), cells);
                for (var k = 1; k < cells.Count; k++)
                {
                    Assert.NotEqual(cells[k - 1], cells[k]);
                    Assert.False(SweepPath.CrossesCentre(cells[k - 1], cells[k]),
                        $"pitch {pitch}, near side {nearSide}: {cells[k - 1]} to {cells[k]} crosses the centre");
                }
            }
    }

    [Fact]
    public void HUD_points_are_skipped_and_the_path_jumps_over_them()
    {
        var path = SweepPath.Build(400, 310, 50, 800, 599, nearSide: false, Hud);
        var cells = Cells(path, 400, 310, 50);

        Assert.DoesNotContain(path, p => Hud(p.X, p.Y));
        Assert.True(path.Count < 81);                                    // ring 4's bottom row is under the hotbar line
        Assert.Contains(Enumerable.Range(1, cells.Count - 1), k => Steps(cells[k - 1], cells[k]) > 1);
    }

    [Fact]
    public void Every_point_stays_12_px_inside_the_client()
    {
        var path = SweepPath.Build(400, 310, 100, 800, 599, nearSide: true, NoHud);

        Assert.All(path, p => Assert.True(p.X >= 12 && p.Y >= 12 && p.X <= 787 && p.Y <= 586, $"{p} is too near the edge"));
        Assert.True(path.Count < 81);                                    // ring 4 at 100 px leaves the client
    }

    [Fact]
    public void The_near_side_extension_reaches_within_a_block_of_the_bottom_margin()
    {
        var path = SweepPath.Build(400, 310, 50, 800, 599, nearSide: true, NoHud);
        var bottom = 599 - SweepPath.EdgeMarginPx;

        Assert.Contains(path, p => p.Y >= bottom - 50 && p.Y <= bottom);
        Assert.DoesNotContain(path, p => p.Y > bottom);
    }

    [Fact]
    public void The_other_three_sides_stay_at_ring_4()
    {
        var cells = Cells(SweepPath.Build(400, 310, 50, 800, 599, nearSide: true, NoHud), 400, 310, 50);

        Assert.All(cells, c => Assert.True(Math.Abs(c.I) <= 4 && c.J >= -4, $"{c} is past ring 4 on a far side"));
        Assert.Contains(cells, c => c.J > 4);
    }

    [Fact]
    public void Without_the_near_side_extension_nothing_passes_ring_4()
    {
        var cells = Cells(SweepPath.Build(400, 310, 50, 800, 599, nearSide: false, NoHud), 400, 310, 50);

        Assert.All(cells, c => Assert.True(Ring(c) <= 4));
    }

    [Fact]
    public void The_extension_continues_one_block_down_from_the_end_of_ring_4()
    {
        var cells = Cells(SweepPath.Build(400, 310, 50, 800, 599, nearSide: true, NoHud), 400, 310, 50);

        var end = cells.IndexOf((4, 4));
        Assert.Equal(new (int, int)[] { (4, 5), (3, 5) }, cells.Skip(end + 1).Take(2));
    }

    [Fact]
    public void With_the_real_HUD_mask_the_hotbar_line_stops_the_extension()
    {
        // HudMask masks everything below y 470 of 599, the full width: the near-side rows stop there.
        var path = SweepPath.Build(390, 340, 32, 800, 599, nearSide: true, Hud);

        Assert.True(path.Max(p => p.Y) <= 470);
    }

    [Fact]
    public void A_start_block_outside_the_game_area_gives_no_path()
        => Assert.Empty(SweepPath.Build(780, 300, 50, 800, 599, nearSide: true, NoHud));

    [Fact]
    public void A_long_path_is_capped_at_256_points_and_still_closes()
    {
        var path = SweepPath.Build(400, 310, 16, 800, 3000, nearSide: true, NoHud);

        Assert.InRange(path.Count, 250, SweepPath.MaxPoints);
        Assert.Equal(new SweepPoint(416, 310), path[0]);
        Assert.Equal(path[0], path[^1]);
    }

    [Fact]
    public void When_clipping_leaves_a_hop_through_the_centre_it_goes_round()
    {
        // 180 px blocks: ring 1's bottom row is under the hotbar line, ring 2 keeps only (2,0) and (2,-1).
        // The move from (-1,0) to (2,0) would cross the character's block, so it goes by (0,-1).
        var cells = Cells(SweepPath.Build(400, 310, 180, 800, 599, nearSide: true, Hud), 400, 310, 180);

        Assert.Equal(new (int, int)[] { (1, 0), (1, -1), (0, -1), (-1, -1), (-1, 0), (0, -1), (2, 0), (2, -1), (1, 0) }, cells);
    }

    [Theory]
    [InlineData(-1, 0, 1, 0, true)]      // straight through the middle
    [InlineData(-1, 0, 2, -1, true)]     // a shallow slant through it
    [InlineData(-1, 0, 0, -1, false)]    // touches the corner only
    [InlineData(1, 0, 1, 1, false)]      // beside it
    public void A_move_crosses_the_centre_only_through_its_inside(int ai, int aj, int bi, int bj, bool crosses)
        => Assert.Equal(crosses, SweepPath.CrossesCentre((ai, aj), (bi, bj)));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~SweepPathTests"`
Expected: build FAILS with `CS0103: The name 'SweepPath' does not exist in the current context`.

- [ ] **Step 3: Write the generator**

Create `Engine/SweepPath.cs`:

```csharp
using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

/// <summary>
/// The sweep's path (ore-stop sweep spec, "The path"): square rings around the character, one block
/// apart, from ring 1 out to <see cref="Rings"/>, in spiral order, starting and ending on the start
/// block east of the character (beside it, never under it). Cells are block units around the centre,
/// j growing down the screen. The spiral goes right 1, up 1, left 2, down 2, right 3, ... so ring k
/// starts at (k, k - 1), ends at (k, k), and the next ring starts one block right, outward.
/// <para>A cell is swept when it is not the centre, its point lies <see cref="EdgeMarginPx"/> inside
/// the client, and it is not in the HUD; the path jumps over the rest with the button held. With
/// nearSide, rows continue below ring 4 (the camera side) across the ring's width, back and forth,
/// down to the last row inside the bottom margin; the other three sides stay at ring 4 (owner input
/// 6). When skipped cells leave a straight move that would pass through the centre block, one detour
/// cell goes in first (the shortest that clears it both ways), or the point is dropped. At most
/// <see cref="MaxPoints"/>, the closing return to the start block included. Every point is in the
/// measured client's pixels, like the finder's. Pure.</para>
/// </summary>
public static class SweepPath
{
    /// <summary>Owner decision 3: out to 4 blocks, slightly tighter than reach (about 5 to 6).</summary>
    public const int Rings = 4;
    /// <summary>How far every point stays inside the client (owner input 6: the near-side rows stop
    /// this far short of the bottom edge).</summary>
    public const int EdgeMarginPx = 12;
    public const int MaxPoints = BridgeContract.MaxSweepPoints;
    /// <summary>The start block, one other block, and the start block again.</summary>
    public const int MinPoints = 3;

    /// <summary>A pass's path: its block size and centre, the finder's client, the game's HUD skipped.</summary>
    public static IReadOnlyList<SweepPoint> Build(FinderSetup pass, bool nearSide) =>
        Build(pass.CenterX, pass.CenterY, pass.Pitch, pass.ClientW, pass.ClientH, nearSide,
            (x, y) => HudMask.Contains(x, y, pass.ClientW, pass.ClientH));

    /// <summary>The path, or empty when the start block is outside the game area or fewer than
    /// <see cref="MinPoints"/> points fit.</summary>
    public static IReadOnlyList<SweepPoint> Build(int centerX, int centerY, int pitch, int clientW, int clientH,
        bool nearSide, Func<int, int, bool> masked)
    {
        if (pitch < 1) return Array.Empty<SweepPoint>();
        (int X, int Y) At((int I, int J) c) => (centerX + c.I * pitch, centerY + c.J * pitch);
        bool Valid((int I, int J) c)
        {
            if (c == (0, 0)) return false;
            var (x, y) = At(c);
            return x >= EdgeMarginPx && y >= EdgeMarginPx && x <= clientW - 1 - EdgeMarginPx && y <= clientH - 1 - EdgeMarginPx
                   && !masked(x, y);
        }

        (int I, int J) start = (1, 0);
        if (!Valid(start)) return Array.Empty<SweepPoint>();

        var order = Spiral(Rings);
        if (nearSide) order.AddRange(NearSideRows(Rings, (clientH - 1 - EdgeMarginPx - centerY) / pitch));
        var cells = order.Where(Valid).ToList();     // cells[0] is the start: the spiral begins on it

        var path = new List<(int I, int J)> { start };
        foreach (var c in cells.Skip(1))
        {
            if (path.Count > MaxPoints - 4) break;    // room for a detour here and one on the way home
            Append(path, c, cells);
        }
        while (path.Count > 1 && !Append(path, start, cells)) path.RemoveAt(path.Count - 1);
        if (path.Count < MinPoints) return Array.Empty<SweepPoint>();

        return path.Select(c => { var (x, y) = At(c); return new SweepPoint(x, y); }).ToList();
    }

    /// <summary>True when the straight move from a to b passes through the centre block's inside (the
    /// square of half a block around (0, 0)). Touching a corner is not passing through. Liang-Barsky
    /// clipping of the segment against that square.</summary>
    internal static bool CrossesCentre((int I, int J) a, (int I, int J) b)
    {
        double t0 = 0, t1 = 1;
        double dx = b.I - a.I, dy = b.J - a.J;
        foreach (var (p, q) in new[] { (-dx, a.I + 0.5), (dx, 0.5 - a.I), (-dy, a.J + 0.5), (dy, 0.5 - a.J) })
        {
            if (p == 0)
            {
                if (q < 0) return false;
                continue;
            }
            var t = q / p;
            if (p < 0) t0 = Math.Max(t0, t); else t1 = Math.Min(t1, t);
            if (t0 > t1) return false;
        }
        return t1 - t0 > 1e-9;
    }

    /// <summary>Rings 1 to <paramref name="rings"/> in spiral order from (1, 0).</summary>
    private static List<(int I, int J)> Spiral(int rings)
    {
        var cells = new List<(int I, int J)>();
        var total = (2 * rings + 1) * (2 * rings + 1) - 1;
        var dirs = new (int DI, int DJ)[] { (1, 0), (0, -1), (-1, 0), (0, 1) };   // right, up, left, down
        int i = 0, j = 0, leg = 1;
        for (var d = 0; cells.Count < total; d++)
        {
            var (di, dj) = dirs[d % 4];
            for (var s = 0; s < leg && cells.Count < total; s++)
            {
                i += di;
                j += dj;
                cells.Add((i, j));
            }
            if (d % 2 == 1) leg++;
        }
        return cells;
    }

    /// <summary>Rows below the last ring, rings + 1 to <paramref name="lastRow"/>, across the ring's
    /// width: the first right to left (it starts under ring 4's last block, (rings, rings)), then back
    /// and forth.</summary>
    private static IEnumerable<(int I, int J)> NearSideRows(int rings, int lastRow)
    {
        for (int j = rings + 1, n = 0; j <= lastRow; j++, n++)
            for (var s = 0; s <= 2 * rings; s++)
                yield return (n % 2 == 0 ? rings - s : -rings + s, j);
    }

    /// <summary>Appends next, after one detour cell when the straight move from the last point would
    /// pass through the centre block. False, appending nothing, when no single detour clears it.</summary>
    private static bool Append(List<(int I, int J)> path, (int I, int J) next, IReadOnlyList<(int I, int J)> cells)
    {
        var last = path[^1];
        if (next == last) return false;
        if (!CrossesCentre(last, next))
        {
            path.Add(next);
            return true;
        }
        (int I, int J)? detour = null;
        var best = double.MaxValue;
        foreach (var w in cells)
        {
            if (w == last || w == next || CrossesCentre(last, w) || CrossesCentre(w, next)) continue;
            var length = Distance(last, w) + Distance(w, next);
            if (length < best)
            {
                best = length;
                detour = w;
            }
        }
        if (detour is not { } via) return false;
        path.Add(via);
        path.Add(next);
        return true;
    }

    private static double Distance((int I, int J) a, (int I, int J) b) =>
        Math.Sqrt((double)(a.I - b.I) * (a.I - b.I) + (double)(a.J - b.J) * (a.J - b.J));
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~SweepPathTests"`
Expected: PASS, 18 tests (14 facts, 4 theory cases).

- [ ] **Step 5: Run the full suite**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj`
Expected: **B + 24 passed**, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add Engine/SweepPath.cs tests/RoRoRo.UrOcr.Tests/Engine/SweepPathTests.cs
git commit -m "feat(pulse): the sweep path, ring by ring around the character" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: The per-account sweep settings

**Files:**
- Modify: `Storage/Pulse.cs` (`PulseConfig`)
- Modify: `Storage/PulseValidation.cs`
- Modify: `tests/RoRoRo.UrOcr.Tests/Storage/PulseValidationTests.cs`, `tests/RoRoRo.UrOcr.Tests/Storage/PulseStorageTests.cs`
- Modify: `README.md` (the pulse file section)

**Interfaces:**
- Consumes (existing): `PulseConfig`, `PulseValidation.Validate`, `TriggerStore.UpsertPulse`, `TriggerStore.Pulses`.
- Produces: `PulseConfig.SweepDwellMs` (int, default `PulseConfig.DefaultSweepDwellMs` = 400), `PulseConfig.SweepNearSide` (bool, default true), JSON keys `sweepDwellMs` and `sweepNearSide`; `PulseValidation.MinSweepDwellMs = 50`, `MaxSweepDwellMs = 5000`.

- [ ] **Step 1: Write the failing tests**

In `tests/RoRoRo.UrOcr.Tests/Storage/PulseValidationTests.cs`, add after `Timings_must_be_in_range`:

```csharp
    [Theory]
    [InlineData(49)]
    [InlineData(5001)]
    public void The_sweep_dwell_must_be_in_range(int dwell) =>
        Assert.Contains($"sweepDwellMs must be 50 to 5000, not {dwell}", Check(Pulse() with { SweepDwellMs = dwell }));

    [Fact]
    public void The_sweep_dwell_limits_are_valid()
    {
        Assert.Null(Check(Pulse() with { SweepDwellMs = 50 }));
        Assert.Null(Check(Pulse() with { SweepDwellMs = 5000 }));
    }
```

In `tests/RoRoRo.UrOcr.Tests/Storage/PulseStorageTests.cs`, in `Missing_settings_take_the_spec_defaults`, add after `Assert.Null(p.Macros);`:

```csharp
        Assert.Equal(400, p.SweepDwellMs);
        Assert.True(p.SweepNearSide);
```

and add after that test:

```csharp
    [Fact]
    public void The_sweep_settings_survive_a_reload_under_camel_case_keys()
    {
        var path = TempFile();
        new TriggerStore(path).UpsertPulse(Pulse() with { SweepDwellMs = 650, SweepNearSide = false });

        var json = File.ReadAllText(path);
        var p = Assert.Single(new TriggerStore(path).Pulses);

        Assert.Contains("\"sweepDwellMs\": 650", json);
        Assert.Contains("\"sweepNearSide\": false", json);
        Assert.Equal((650, false), (p.SweepDwellMs, p.SweepNearSide));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~PulseValidationTests|FullyQualifiedName~PulseStorageTests"`
Expected: build FAILS with `CS0117: 'PulseConfig' does not contain a definition for 'SweepDwellMs'`.

- [ ] **Step 3: Add the settings**

In `Storage/Pulse.cs`, replace the `PulseConfig` record's parameter list tail

```csharp
    bool Enabled = true,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PulseMacros? Macros = null)
{
    public const int DefaultBurstMs = 2000;
```

with

```csharp
    bool Enabled = true,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PulseMacros? Macros = null,
    int SweepDwellMs = PulseConfig.DefaultSweepDwellMs,
    bool SweepNearSide = true)
{
    /// <summary>How long the sweep holds on each block (owner decision 2): the main breaks a
    /// bottom-layer block in about 0.3 s; a weaker pickaxe needs longer.</summary>
    public const int DefaultSweepDwellMs = 400;
    public const int DefaultBurstMs = 2000;
```

and in the record's doc comment, replace `Stored in triggers.json under "pulses"; missing keys load as the spec defaults.` with `SweepDwellMs and SweepNearSide are the sweep's per-account settings (ore-stop sweep spec: dwell per account, default 400 ms; near-side rows, default on). Stored in triggers.json under "pulses"; missing keys load as the spec defaults.`

In `Storage/PulseValidation.cs`, add below `MaxRockCapMinutes`:

```csharp
    /// <summary>The dwell range Ur Task's SweepPath takes.</summary>
    public const int MinSweepDwellMs = 50;
    public const int MaxSweepDwellMs = 5000;
```

and in `Validate`, after the `RockCapMinutes` check:

```csharp
        if (p.SweepDwellMs < MinSweepDwellMs || p.SweepDwellMs > MaxSweepDwellMs)
            return $"sweepDwellMs must be {MinSweepDwellMs} to {MaxSweepDwellMs}, not {p.SweepDwellMs}.";
```

- [ ] **Step 4: Document the keys**

In `README.md`, in the pulse file example, replace

```
          "burstMs": 2000, "settleMs": 1000, "rockCapMinutes": 5 }
```

with

```
          "burstMs": 2000, "settleMs": 1000, "rockCapMinutes": 5,
          "sweepDwellMs": 600, "sweepNearSide": true }
```

and replace `Left out, \`burstMs\` is 2000, \`settleMs\` 1000 and \`rockCapMinutes\` 5.` with:

```
Left out, `burstMs` is 2000, `settleMs` 1000, `rockCapMinutes` 5, `sweepDwellMs` 400 and `sweepNearSide` true. `sweepDwellMs` is how long the sweep holds on each block (50 to 5000): raise it for an account whose pickaxe breaks blocks slowly. `sweepNearSide` lets the sweep reach further toward the bottom of the window, where the blocks nearest the camera sit (see "Stone is swept").
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~PulseValidationTests|FullyQualifiedName~PulseStorageTests|FullyQualifiedName~PulseImporterTests"`
Expected: PASS, including the 4 new tests and every importer test (an imported entry keeps its sweep keys: `PulseImporter` copies the entry `with { Macros = resolved }`).

- [ ] **Step 6: Run the full suite**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj`
Expected: **B + 28 passed**, 0 failed.

- [ ] **Step 7: Commit**

```bash
git add Storage/Pulse.cs Storage/PulseValidation.cs tests/RoRoRo.UrOcr.Tests/Storage/PulseValidationTests.cs tests/RoRoRo.UrOcr.Tests/Storage/PulseStorageTests.cs README.md
git commit -m "feat(pulse): per-account sweep dwell and near-side rows" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Did the sweep break anything

**Files:**
- Create: `Engine/SweepChange.cs`
- Create: `tests/RoRoRo.UrOcr.Tests/Engine/SweepChangeTests.cs`

**Interfaces:**
- Consumes: `FramePixels` (`Width`, `Height`, `At`), `SweepPoint` (Task 1).
- Produces: `public static class SweepChange` with `MinChangeSum = 30`, `static int Count(FramePixels before, FramePixels after, IReadOnlyList<SweepPoint> path, int clientW, int clientH, int pitch)`, `static int Points(IReadOnlyList<SweepPoint> path)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/RoRoRo.UrOcr.Tests/Engine/SweepChangeTests.cs`:

```csharp
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

public class SweepChangeTests
{
    /// <summary>From the start block (450,300) to two blocks and back, at 50 px.</summary>
    private static readonly SweepPoint[] Path =
        { new(450, 300), new(450, 250), new(400, 250), new(450, 300) };

    private static FramePixels Grey(int w = 800, int h = 599, params (int X, int Y, int W, int H, Rgb Colour)[] paint)
    {
        var px = Frames.Solid(w, h, PulseFixtures.Grey);
        foreach (var (x, y, bw, bh, c) in paint) Frames.Fill(px, w, x, y, bw, bh, c);
        return new FramePixels(w, h, px);
    }

    [Fact]
    public void An_unchanged_frame_changed_nothing()
        => Assert.Equal(0, SweepChange.Count(Grey(), Grey(), Path, 800, 599, 50));

    [Fact]
    public void Blocks_that_changed_under_the_path_are_counted_and_the_start_is_not()
    {
        var after = Grey(paint: new[]
        {
            (445, 245, 10, 10, PulseFixtures.Black),               // (450,250) broke
            (395, 245, 10, 10, PulseFixtures.Black),               // (400,250) broke
            (445, 295, 10, 10, new Rgb(250, 250, 250)),            // the outline on the start block, where the pointer rests
        });

        Assert.Equal(2, SweepChange.Count(Grey(), after, Path, 800, 599, 50));
    }

    [Fact]
    public void A_change_off_the_path_is_not_counted()
    {
        var after = Grey(paint: new[] { (295, 295, 10, 10, PulseFixtures.Black) });

        Assert.Equal(0, SweepChange.Count(Grey(), after, Path, 800, 599, 50));
    }

    [Fact]
    public void Frames_at_another_size_are_sampled_scaled()
    {
        // 125%: (450,250) is about (562,312) in a 1000x749 frame, (400,250) about (500,312).
        var after = Grey(1000, 749, new[] { (555, 305, 15, 15, PulseFixtures.Black), (493, 305, 15, 15, PulseFixtures.Black) });

        Assert.Equal(2, SweepChange.Count(Grey(1000, 749), after, Path, 800, 599, 50));
    }

    [Fact]
    public void Points_counts_each_swept_block_once_without_the_start()
    {
        var detoured = new SweepPoint[] { new(450, 300), new(450, 250), new(400, 250), new(450, 250), new(500, 250), new(450, 300) };

        Assert.Equal((2, 3), (SweepChange.Points(Path), SweepChange.Points(detoured)));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~SweepChangeTests"`
Expected: build FAILS with `CS0103: The name 'SweepChange' does not exist in the current context`.

- [ ] **Step 3: Write it**

Create `Engine/SweepChange.cs`:

```csharp
using RoRoRo.UrOcr.Ipc;

namespace RoRoRo.UrOcr.Engine;

/// <summary>
/// Whether a sweep broke anything (ore-stop sweep spec: "A sweep that broke nothing (the frame barely
/// changed) counts as an empty pass"): the calm frame the pass was planned on against the next calm
/// frame, at every block the path held the button over except the start block, where the pointer
/// rests after the release and its hover outline shows. A block changed when the average of a box
/// around its point moved by more than <see cref="MinChangeSum"/>, the three channel differences
/// summed (0 to 765, the scale of the 2026-09-29 drag run: broken blocks moved 45 to 101, noise and
/// blocks the pointer never passed 0). Points are in measured-client pixels; either frame may be
/// another size and is sampled scaled. Power balls from a special pickaxe also break blocks, so a
/// change is not proof the pointer did it (spec "Later inputs" 5). Pure.
/// </summary>
public static class SweepChange
{
    public const int MinChangeSum = 30;

    /// <summary>How many swept blocks changed between the two frames.</summary>
    public static int Count(FramePixels before, FramePixels after, IReadOnlyList<SweepPoint> path, int clientW, int clientH, int pitch)
    {
        var half = Math.Max(1, pitch / 8);    // a box a quarter block wide around each point
        return Swept(path).Count(p =>
        {
            var (r0, g0, b0) = Average(before, p, half, clientW, clientH);
            var (r1, g1, b1) = Average(after, p, half, clientW, clientH);
            return Math.Abs(r0 - r1) + Math.Abs(g0 - g1) + Math.Abs(b0 - b1) > MinChangeSum;
        });
    }

    /// <summary>How many blocks <see cref="Count"/> looks at: each swept block once, the start left out.</summary>
    public static int Points(IReadOnlyList<SweepPoint> path) => Swept(path).Count();

    private static IEnumerable<SweepPoint> Swept(IReadOnlyList<SweepPoint> path) =>
        path.Count == 0 ? Enumerable.Empty<SweepPoint>() : path.Where(p => p != path[0]).Distinct();

    private static (double R, double G, double B) Average(FramePixels f, SweepPoint p, int half, int clientW, int clientH)
    {
        long r = 0, g = 0, b = 0, n = 0;
        for (var dy = -half; dy <= half; dy++)
            for (var dx = -half; dx <= half; dx++)
            {
                var x = Math.Clamp((int)((long)(p.X + dx) * f.Width / clientW), 0, f.Width - 1);
                var y = Math.Clamp((int)((long)(p.Y + dy) * f.Height / clientH), 0, f.Height - 1);
                var c = f.At(x, y);
                r += c.R;
                g += c.G;
                b += c.B;
                n++;
            }
        return ((double)r / n, (double)g / n, (double)b / n);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~SweepChangeTests"`
Expected: PASS, 5 tests.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj`
Expected: **B + 33 passed**, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add Engine/SweepChange.cs tests/RoRoRo.UrOcr.Tests/Engine/SweepChangeTests.cs
git commit -m "feat(pulse): tell whether a sweep changed any of its blocks" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: The pulse sweeps the stone at the target layer

**Files:**
- Modify: `Engine/PulseLoop.cs`
- Modify: `tests/RoRoRo.UrOcr.Tests/Engine/PulseLoopFixtures.cs` (`PulseFixtures`)
- Create: `tests/RoRoRo.UrOcr.Tests/Engine/PulseLoopSweepTests.cs`
- Modify: `README.md` (the ore finder section), `CHANGELOG.md` (`## 0.6.0 — unreleased`)

**Interfaces:**
- Consumes: `MacroCall.BeginSweep`, `ScriptedMacros.SweepId`, `.Sweeps`, `.SweepReplies` (Task 1); `SweepPath.Build(FinderSetup, bool)`, `SweepPath.MinPoints` (Task 2); `PulseConfig.SweepDwellMs`, `.SweepNearSide` (Task 3); `SweepChange.Count`, `.Points` (Task 4); existing `PulseLoop` members `ForPass`, `Route`, `NothingInReach`, `EndPass`, `OnCallEnded`, `Enter`, `Stop`, `_pitchUnread`, `_targets`, `_pass`, `_cleared`, `_clearAtEnded`.
- Produces: the log lines `started: ... with the ore finder (P px blocks, radius R), sweeping stone (D ms a point, near side on|off)`, `<seen> is the target: K ore point(s), then a sweep of N points`, `swept N points: reading again`, `cleared ore and swept N points: reading again`, `the sweep changed C of P points`, `the sweep broke nothing (0 of P points changed): <turning or riding>`; `PulseFixtures.Dot`, `PulseFixtures.RingWithSweep(string layer = "grey")`.

- [ ] **Step 1: Add the fixtures**

In `tests/RoRoRo.UrOcr.Tests/Engine/PulseLoopFixtures.cs`, inside `PulseFixtures`, add after `RingWithFinder`:

```csharp
    /// <summary>The Auto Mine dot guard a live finder carries: 3x3 at 55,289, red, within 30.</summary>
    public static readonly GuardBox Dot = new(55, 289, 3, 3, new Rgb(255, 19, 90), 30);

    /// <summary>A ring whose aim-layer finder has a guard, so a pass with a read block size sweeps.</summary>
    public static RingDefinition RingWithSweep(string layer = "grey") =>
        Ring() with { Finders = new[] { Finder(layer) with { Guard = Dot } } };
```

- [ ] **Step 2: Write the failing loop tests**

Create `tests/RoRoRo.UrOcr.Tests/Engine/PulseLoopSweepTests.cs`:

```csharp
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

/// <summary>
/// The pulse with a guarded ore finder on a frame whose block size reads (32 px seams): the ore through
/// ClearAt, the stone in one SweepPath, then a read that judges the sweep. Every unscripted playback
/// finishes on its first poll.
/// </summary>
public class PulseLoopSweepTests
{
    private sealed record Rig(PulseLoop Loop, ScriptedMacros Macros, ScriptedReader Reader, PulseClock Clock, List<string> Log);

    private static readonly Rgb Seam = new(225, 230, 240);
    private static readonly ClearAtGuard WireDot = new(55, 289, 3, 3, new Rgb(255, 19, 90), 30);

    /// <summary>A cyan crystal inside one 32 px block, about 47 px right of the character.</summary>
    private static readonly (int X, int Y, int W, int H, Rgb Colour) Crystal = (425, 329, 24, 24, PulseFixtures.Cyan);

    /// <summary>800x599 of 32 px <paramref name="block"/> blocks with bright seams, rectangles painted over.</summary>
    private static FramePixels Blocks(Rgb block, params (int X, int Y, int W, int H, Rgb Colour)[] paint)
    {
        var px = Frames.Grid(800, 599, 32, 32, block, Seam);
        foreach (var (x, y, w, h, c) in paint) Frames.Fill(px, 800, x, y, w, h, c);
        return new FramePixels(800, 599, px);
    }

    private static FramePixels Grey(params (int X, int Y, int W, int H, Rgb Colour)[] paint) => Blocks(PulseFixtures.Grey, paint);

    /// <summary>The pass the loop plans on a 32 px frame: the fixture finder at the read block size.</summary>
    private static IReadOnlyList<SweepPoint> PathFor(bool nearSide = true) =>
        SweepPath.Build(PulseFixtures.Finder() with { Pitch = 32, Guard = PulseFixtures.Dot }, nearSide);

    private static Rig Build(FramePixels frame, PulseConfig? config = null, RingDefinition? ring = null)
    {
        var macros = new ScriptedMacros();
        var reader = new ScriptedReader { Next = ScriptedReader.All(PulseFixtures.Grey), Frame = frame };
        var clock = new PulseClock();
        var log = new List<string>();
        var loop = new PulseLoop(config ?? PulseFixtures.Config(), new[] { ring ?? PulseFixtures.RingWithSweep() },
            PulseFixtures.Spots(), reader, macros, clock, log.Add);
        return new Rig(loop, macros, reader, clock, log);
    }

    private static Task Tick(Rig r) => r.Loop.TickAsync(true, 7, CancellationToken.None);

    /// <summary>Auto Mine on, ride 2 s, Auto Mine off, settle 1 s, read: the pass's first call started.</summary>
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
    public async Task Starts_saying_it_sweeps_stone()
    {
        var rig = Build(Grey());

        await Tick(rig);

        Assert.Contains(rig.Log, l => l.StartsWith("started")
            && l.EndsWith("with the ore finder (50 px blocks, radius 2), sweeping stone (400 ms a point, near side on)"));
    }

    [Fact]
    public async Task With_ore_in_view_the_pass_clears_the_ore_then_sweeps_the_stone()
    {
        var rig = Build(Grey(Crystal));

        await FirstRead(rig);                // read: ClearAt with the ore only
        var clear = Assert.Single(rig.Macros.ClearAts);
        Assert.NotEmpty(clear.Points);
        Assert.All(clear.Points, p => Assert.StartsWith("ore ", p.Label));
        Assert.Empty(rig.Macros.Sweeps);

        await Tick(rig);                     // ClearAt finished: the sweep starts
        Assert.Equal(new[] { "id-on", "id-off", ScriptedMacros.ClearAtId, ScriptedMacros.SweepId }, rig.Macros.RunIds);
        Assert.Equal(PathFor(), Assert.Single(rig.Macros.Sweeps).Path);
        var ore = clear.Points.Count;
        Assert.Contains(rig.Log, l => l.EndsWith($"is the target: {ore} ore {(ore == 1 ? "point" : "points")}, then a sweep of {PathFor().Count} points"));
    }

    [Fact]
    public async Task With_no_ore_the_pass_sweeps_at_once()
    {
        var rig = Build(Grey());

        await FirstRead(rig);

        Assert.Empty(rig.Macros.ClearAts);
        Assert.Equal(new[] { "id-on", "id-off", ScriptedMacros.SweepId }, rig.Macros.RunIds);
        Assert.Contains(rig.Log, l => l.EndsWith($"is the target: 0 ore points, then a sweep of {PathFor().Count} points"));
    }

    [Fact]
    public async Task The_sweep_carries_the_account_s_dwell_the_block_size_the_measured_client_and_the_guard()
    {
        var rig = Build(Grey(), PulseFixtures.Config() with { SweepDwellMs = 650 });

        await FirstRead(rig);

        var req = Assert.Single(rig.Macros.Sweeps);
        Assert.Equal(("SweepPath", "42", new ClearAtClient(800, 599), 32, 650, WireDot),
            (req.Method, req.Target, req.Client, req.Step, req.DwellMs, req.Guard));
        Assert.Equal(req.Path[0], req.Path[^1]);
        Assert.Equal(new SweepPoint(422, 340), req.Path[0]);   // one block east of the character at 390,340
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_path_follows_the_account_s_near_side_setting(bool nearSide)
    {
        var rig = Build(Grey(), PulseFixtures.Config() with { SweepNearSide = nearSide });

        await FirstRead(rig);

        Assert.Equal(PathFor(nearSide), Assert.Single(rig.Macros.Sweeps).Path);
    }

    [Fact]
    public async Task A_sweep_that_changed_blocks_counts_as_progress_and_plans_the_next_pass()
    {
        var rig = Build(Grey());
        var path = PathFor();
        await FirstRead(rig);

        await Tick(rig);                     // the sweep finished: settle, Auto Mine stays off
        Assert.Equal(PulseState.Pausing, rig.Loop.State);
        Assert.Contains(rig.Log, l => l == $"swept {path.Count} points: reading again");

        rig.Reader.Frame = Grey((path[1].X - 4, path[1].Y - 4, 8, 8, PulseFixtures.Black),
                                (path[2].X - 4, path[2].Y - 4, 8, 8, PulseFixtures.Black));
        rig.Clock.Advance(1000);
        await Tick(rig);                     // read: judged, then the next pass sweeps

        Assert.Contains(rig.Log, l => l == $"the sweep changed 2 of {SweepChange.Points(path)} points");
        Assert.Equal(PulseState.Clearing, rig.Loop.State);
        Assert.Equal(2, rig.Macros.Sweeps.Count);
    }

    [Fact]
    public async Task A_sweep_that_broke_nothing_rides_a_burst()
    {
        var rig = Build(Grey());
        await FirstRead(rig);
        await Tick(rig);                     // the sweep finished: settle

        rig.Clock.Advance(1000);
        await Tick(rig);                     // read the same frame: nothing changed

        Assert.Equal(PulseState.Bursting, rig.Loop.State);
        Assert.Contains(rig.Log, l => l == $"the sweep broke nothing (0 of {SweepChange.Points(PathFor())} points changed): riding a burst");
        Assert.Single(rig.Macros.Sweeps);
        Assert.Equal("id-on", rig.Macros.RunIds.Last());
    }

    [Fact]
    public async Task Ore_cleared_before_the_sweep_counts_as_progress_whatever_the_sweep_did()
    {
        var rig = Build(Grey(Crystal));
        await FirstRead(rig);                // ClearAt started
        await Tick(rig);                     // ClearAt finished (cleared), the sweep started
        await Tick(rig);                     // the sweep finished

        Assert.Contains(rig.Log, l => l == $"cleared ore and swept {PathFor().Count} points: reading again");
        rig.Clock.Advance(1000);
        await Tick(rig);                     // the same frame: no judgement, straight to the next pass

        Assert.DoesNotContain(rig.Log, l => l.StartsWith("the sweep broke nothing"));
        Assert.Equal(2, rig.Macros.ClearAts.Count);
    }

    [Fact]
    public async Task Without_a_guard_stone_is_cleared_point_by_point_as_before()
    {
        var rig = Build(Grey(), ring: PulseFixtures.RingWithFinder());

        await FirstRead(rig);

        var req = Assert.Single(rig.Macros.ClearAts);
        Assert.Equal(13, req.Points.Count);
        Assert.Equal(new ClearAtPoint(390, 340, "stone 1"), req.Points[0]);
        Assert.Empty(rig.Macros.Sweeps);
    }

    [Fact]
    public async Task An_unread_block_size_never_sweeps()
    {
        var rig = Build(PulseFixtures.Calm());       // plain rock: no block size to read

        await FirstRead(rig);

        Assert.Equal(new ClearAtOutline(240, 240, 60, 225), Assert.Single(rig.Macros.ClearAts).Outline);
        Assert.Empty(rig.Macros.Sweeps);
    }

    [Fact]
    public async Task Never_sweeps_on_the_ride_down()
    {
        var rig = Build(Blocks(PulseFixtures.Navy));  // the top layer: above the target

        await FirstRead(rig);

        Assert.Equal(PulseState.Riding, rig.Loop.State);
        Assert.Empty(rig.Macros.Sweeps);
        Assert.Empty(rig.Macros.ClearAts);
    }

    [Fact]
    public async Task Never_sweeps_past_the_target()
    {
        var rig = Build(Grey(), PulseFixtures.Config(target: 2), PulseFixtures.RingWithSweep("black"));

        await FirstRead(rig);

        Assert.Equal(PulseState.GoingToTop, rig.Loop.State);
        Assert.Empty(rig.Macros.Sweeps);
    }

    [Fact]
    public async Task A_sweep_stopped_at_its_guard_stops_the_loop_with_Auto_Mine_off()
    {
        var rig = Build(Grey());
        rig.Macros.Script(ScriptedMacros.SweepId, ScriptedMacros.CheckFailed);
        await FirstRead(rig);

        await Tick(rig);

        Assert.Equal(PulseState.Stopped, rig.Loop.State);
        Assert.StartsWith($"'SweepPath ({PathFor().Count} points)' stopped at its check", rig.Loop.StopReason);
        Assert.EndsWith("Auto Mine is off.", rig.Loop.StopReason);
    }

    [Fact]
    public async Task An_Ur_Task_without_SweepPath_stops_the_loop_and_says_so()
    {
        var rig = Build(Grey());
        rig.Macros.SweepReplies.Enqueue(new RunMacroResponse(false, null, false, BridgeReasons.Refused, "Unknown method 'SweepPath'."));

        await FirstRead(rig);

        Assert.Equal(PulseState.Stopped, rig.Loop.State);
        Assert.Contains("Unknown method 'SweepPath'.", rig.Loop.StopReason);
        Assert.EndsWith("Auto Mine is off.", rig.Loop.StopReason);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~PulseLoopSweepTests"`
Expected: FAIL. It compiles (Tasks 1 to 4 provide every name), and every test that expects a sweep fails with an empty `rig.Macros.Sweeps` (the loop still sends the stone through ClearAt); `Without_a_guard_...`, `An_unread_block_size_never_sweeps`, `Never_sweeps_on_the_ride_down` and `Never_sweeps_past_the_target` already pass, which is correct.

- [ ] **Step 4: Add the sweep state to PulseLoop**

In `Engine/PulseLoop.cs`, add after the `_toleranceRgb` field:

```csharp
    private IReadOnlyList<SweepPoint>? _sweep;         // this pass's sweep path; null clears stone point by point
    private FramePixels? _sweepFrame;                  // the calm frame the sweep pass was planned on
    private bool _sweeping;                            // the sweep's call is in flight
    private bool _sweepEnded;                          // this pass's sweep has ended
    private SweepCheck? _sweepCheck;                   // a finished sweep, judged on the next calm frame

    /// <summary>A sweep to judge on the next calm frame: the frame it was planned on, its path and the
    /// block size it moved by.</summary>
    private sealed record SweepCheck(FramePixels Before, IReadOnlyList<SweepPoint> Path, int Pitch);
```

In the constructor, replace

```csharp
        var how = _finder is null
            ? "the 8 Clear spot macros"
            : $"the ore finder ({_finder.Pitch} px blocks, radius {_finder.RadiusBlocks})";
```

with

```csharp
        var how = _finder is null
            ? "the 8 Clear spot macros"
            : $"the ore finder ({_finder.Pitch} px blocks, radius {_finder.RadiusBlocks})"
              + (_finder.Guard is null
                  ? ""
                  : $", sweeping stone ({config.SweepDwellMs} ms a point, near side {(config.SweepNearSide ? "on" : "off")})");
```

Add to the class doc comment, after the sentence that starts `At it: Clearing.`: `With a guard in the finder and a block size read off the frame, the stone is swept instead (spec 2026-09-29): ClearAt with the ore points only, then one SweepPath around the character, then a settle and a read; the next read judges the sweep (SweepChange), and a sweep that changed nothing is a pass with nothing in reach.`

- [ ] **Step 5: Plan and judge the sweep in ReadByShare**

In `ReadByShare`, right after `_frameLogged = false;`, insert:

```csharp
        // The last pass swept and cleared no ore: this calm frame says whether the sweep broke anything.
        if (_sweepCheck is { } check)
        {
            _sweepCheck = null;
            var points = SweepChange.Points(check.Path);
            var changed = SweepChange.Count(check.Before, frame, check.Path, finder.ClientW, finder.ClientH, check.Pitch);
            if (changed == 0) return NothingInReach($"the sweep broke nothing (0 of {points} points changed)");
            _emptyPasses = 0;
            _turns = 0;
            _progressAt = now;
            _log($"the sweep changed {changed} of {points} points");
        }
```

Then replace the end of `ReadByShare`

```csharp
        var targets = TargetFinder.Find(frame, pass);
        if (_pitchUnread) targets = NearestStone(targets, UnreadStonePoints);
        if (targets.Count == 0)
        {
            _log($"{seen} is the target, but the ore finder has no point inside the window: riding a burst");
            return Enter(PulseState.Bursting);
        }
        _targets = targets;
        _pass = pass;
        var ore = targets.Count(t => t.Ore);
        _log($"{seen} is the target: clearing at {targets.Count} points ({ore} ore, {targets.Count - ore} stone)");
        return Enter(PulseState.Clearing);
```

with

```csharp
        var targets = TargetFinder.Find(frame, pass);
        if (_pitchUnread) targets = NearestStone(targets, UnreadStonePoints);
        _sweep = SweepFor(pass);
        if (_sweep is not null)
        {
            targets = targets.Where(t => t.Ore).ToList();   // the stone is swept, not pressed point by point
            _sweepFrame = frame;
        }
        else if (targets.Count == 0)
        {
            _log($"{seen} is the target, but the ore finder has no point inside the window: riding a burst");
            return Enter(PulseState.Bursting);
        }
        _targets = targets;
        _pass = pass;
        var ore = targets.Count(t => t.Ore);
        _log(_sweep is { } path
            ? $"{seen} is the target: {ore} ore {(ore == 1 ? "point" : "points")}, then a sweep of {path.Count} points"
            : $"{seen} is the target: clearing at {targets.Count} points ({ore} ore, {targets.Count - ore} stone)");
        return Enter(PulseState.Clearing);
```

and add this method after `NearestStone`:

```csharp
    /// <summary>This pass's sweep path, or null to clear the stone point by point as before: the block
    /// size was not read off the frame (the path's step would be a guess), the finder has no guard (a
    /// held button needs one), or fewer than SweepPath.MinPoints points fit.</summary>
    private IReadOnlyList<SweepPoint>? SweepFor(FinderSetup pass)
    {
        if (_pitchUnread || pass.Guard is null) return null;
        var path = SweepPath.Build(pass, _config.SweepNearSide);
        return path.Count >= SweepPath.MinPoints ? path : null;
    }
```

In `ReadBySpots`, replace `_targets = null;` with:

```csharp
        _targets = null;
        _sweep = null;
```

In `Route`, replace `_clearAtEnded = false;` with:

```csharp
        _clearAtEnded = false;
        _sweepEnded = false;
        _sweeping = false;
```

- [ ] **Step 6: Send the ore, then the sweep, and end the pass**

In `ClearNext`, replace

```csharp
        if (_targets is { } targets)
        {
            if (_clearAtEnded) return EndPass();
            var f = _pass!;
            var guard = f.Guard is { } g ? new ClearAtGuard(g.X, g.Y, g.W, g.H, g.Expect, g.Tolerance) : null;
            _call.BeginClearAt(new ClearAtClient(f.ClientW, f.ClientH),
                targets.Select(t => new ClearAtPoint(t.X, t.Y, t.Label)).ToList(),
                new ClearAtOutline(f.Outline.W, f.Outline.H, f.Outline.MinCount, f.Outline.WhiteMin),
                guard);
            return true;
        }
```

with

```csharp
        if (_targets is { } targets)
        {
            var f = _pass!;
            var guard = f.Guard is { } g ? new ClearAtGuard(g.X, g.Y, g.W, g.H, g.Expect, g.Tolerance) : null;
            if (!_clearAtEnded && targets.Count > 0)      // a sweep pass with no ore goes straight to the sweep
            {
                _call.BeginClearAt(new ClearAtClient(f.ClientW, f.ClientH),
                    targets.Select(t => new ClearAtPoint(t.X, t.Y, t.Label)).ToList(),
                    new ClearAtOutline(f.Outline.W, f.Outline.H, f.Outline.MinCount, f.Outline.WhiteMin),
                    guard);
                return true;
            }
            if (_sweep is { } path && !_sweepEnded)
            {
                _sweeping = true;                          // SweepFor sweeps only with a guard
                _call.BeginSweep(new ClearAtClient(f.ClientW, f.ClientH), path, f.Pitch, _config.SweepDwellMs, guard!);
                return true;
            }
            return EndPass();
        }
```

In `EndPass`, make the first line inside `if (_targets is { } targets)`:

```csharp
            if (_sweep is { } path) return EndSweepPass(path);
```

and add after `EndPass`:

```csharp
    /// <summary>A sweep pass ends in a settle and a read, Auto Mine still off. Ore cleared is progress
    /// whatever the sweep did; otherwise the next calm frame judges the sweep (ReadByShare).</summary>
    private bool EndSweepPass(IReadOnlyList<SweepPoint> path)
    {
        if (_cleared.Count > 0)
        {
            _emptyPasses = 0;
            _turns = 0;
            _log($"cleared ore and swept {path.Count} points: reading again");
            return Enter(PulseState.Pausing, macroDone: true);
        }
        _sweepCheck = new SweepCheck(_sweepFrame!, path, _pass!.Pitch);
        _log($"swept {path.Count} points: reading again");
        return Enter(PulseState.Pausing, macroDone: true);
    }
```

In `OnCallEnded`, inside `if (State == PulseState.Clearing)`, insert as the first statement:

```csharp
            if (_sweeping)
            {
                // The sweep holds the button the whole way, so it always presses: only a stop matters here.
                // Whether it broke anything is read off the next calm frame (EndSweepPass, ReadByShare).
                _sweeping = false;
                _sweepEnded = true;
                if (r.Status == CallStatus.CheckFailed)
                    Stop($"'{r.Label}' stopped at its check ({r.Detail}). Something may be over the game, such as a " +
                         "menu or a player's profile, so the pulse loop stopped rather than click it.");
                return;
            }
```

- [ ] **Step 7: Run the loop tests to verify they pass**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~PulseLoop"`
Expected: PASS: the 15 `PulseLoopSweepTests` and every existing `PulseLoopTests`, `PulseLoopFinderTests` and `PulseLoopTurnTests` (their finders have no guard, so their passes are unchanged).

- [ ] **Step 8: Document it**

In `README.md`, in the "Ore finder" section, add after the paragraph that starts "The block size is read from the screen on every pass.":

```markdown
### Stone is swept

With a guard in the finder and a block size read off the frame, the pulse no longer presses stone one block at a time. Ore still goes first, point by point. Then Ur Task holds the left button down on the block right of your character and walks the pointer around you, one block at a time, ring by ring out to 4 blocks, and lets go back on the block it started on. It never touches the block you stand on, and it skips the game's buttons. Toward the bottom of the window, where the blocks nearest the camera sit, the rows carry on past 4 blocks to just short of the edge, unless you set `sweepNearSide` to false. The next calm frame decides how it went: a sweep that changed none of its blocks counts as a pass with nothing in reach. When the block size can't be read, or the finder has no guard, the stone is cleared point by point as before. Needs Ur Task 0.12.0 or later.
```

In `CHANGELOG.md`, under `## 0.6.0 — unreleased` → `### Added`, add after the "Block size is read from the screen on every pass." bullet:

```markdown
- **The stone around you is swept, not pressed block by block.** At your target layer, with a guard in the finder and a block size read off the frame, a pass clears the ore point by point as before, then asks Ur Task to hold the left button and walk the pointer around your character, ring by ring out to 4 blocks, and let go back where it started. It never touches the block you stand on and skips the game's buttons. Toward the bottom of the window the rows carry on to just short of the edge (`sweepNearSide`, on by default). How long it holds on each block is `sweepDwellMs` per account (400 ms by default; raise it for a weaker pickaxe). The next calm frame decides how it went; a sweep that changed none of its blocks turns the camera, then rides a burst. Never on the ride down.
```

and under `### Notes`, add:

```markdown
- The sweep needs Ur Task 0.12.0 or later. An older Ur Task makes the pulse stop with `Unknown method 'SweepPath'`.
```

- [ ] **Step 9: Run the full suite**

Run: `dotnet build rororo-ur-ocr.csproj`
Expected: Build succeeded, 0 errors.

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj`
Expected: **B + 48 passed**, 0 failed.

Check the version did not move: `git diff --stat HEAD -- rororo-ur-ocr.csproj manifest.json` prints nothing.

- [ ] **Step 10: Commit**

```bash
git add Engine/PulseLoop.cs tests/RoRoRo.UrOcr.Tests/Engine/PulseLoopFixtures.cs tests/RoRoRo.UrOcr.Tests/Engine/PulseLoopSweepTests.cs README.md CHANGELOG.md
git commit -m "feat(pulse): sweep the stone at the target layer, ore first" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Live verification on Dunder-MiffLan (controller only)

**Run by the controller on the live rig, never by a subagent.** Este plays; the controller sends calls, reads frames and logs. No code changes; nothing is committed unless a finding needs a follow-up task.

**Interfaces:**
- Consumes: Tasks 0 to 5 on `feat/ore-stop-pulse`; Ur Task 0.12.0 built from its plan with that plan's Task 5 passed; the measured file named in `%LOCALAPPDATA%\626Labs\ore-stop-sweep\current.txt`, whose aim-layer finder has a `guard`; `tools\ring-sweep.ps1`.
- Produces: the pointer's share, measured apart from the power balls; the hotbar's real extent; a live sweep pass on the main; notes in `sweep-notes.txt` in the sweep folder (no account names, no user ids).

- [ ] **Before Este plays**
  - [ ] Read the rig's live display scale first (Settings, Display) and say it out loud. The measured client is 800x599 at 100%; anything else, stop and ask.
  - [ ] Ur Task 0.12.0 running (its startup line says so). Close Ur OCR, `dotnet build rororo-ur-ocr.csproj`, start it the way the earlier live passes on this branch did.
  - [ ] The finder for the main's aim layer has a `guard` (in the measured file's `finders`). Without one the pulse never sweeps: add the Auto Mine dot guard the ClearAt runs already use, then `--import-ring` again.
  - [ ] `$sweep = (Get-Content (Join-Path $env:LOCALAPPDATA "626Labs\ore-stop-sweep\current.txt") -Raw).Trim()`; create `$sweep\2026-09-29-share`.

- [ ] **The pointer's share (owner input 5).** The main's pickaxe shoots power balls that break blocks on their own, so the drag run did not isolate the pointer. Measure it apart, in the same runs, with control blocks the pointer never passes over.
  - [ ] Este: the main in the bottom bowl (blocks near 50 px), Auto Mine off, camera top-down, Hide My Pets on, calm. Read the block size `$p` from Ur OCR's last `block size N px` line, and the centre `$cx`, `$cy` from the finder.
  - [ ] Choose the distance `$d` (2 or 3 blocks, the larger that fits) so both columns `$cx + $d * $p` and `$cx - $d * $p` lie inside x 160..787 and rows `$cy - $p`..`$cy + $p` inside y 70..470. Swept cells: `(d,-1), (d,0), (d,1)`. Control cells: `(-d,-1), (-d,0), (-d,1)`.
  - [ ] Noise first: `pwsh -File tools\ring-sweep.ps1 -OutDir "$sweep\2026-09-29-share\noise-before" -Seconds 1 -Minutes 0.02`, wait 2 s with nothing sent, the same into `noise-after`.
  - [ ] Run k (three runs): capture `run<k>-before` as above. Send a SweepPath with path `(d,-1), (d,0), (d,1), (d,0), (d,-1)` (it closes on its start), dwell 400, the finder's guard, with this snippet (fill `$target` with the main's user id; never write it to a file):

```powershell
$target = '<main user id>'; $p = 50; $cx = 400; $cy = 310; $d = 3
$cells = @(@($d,-1),@($d,0),@($d,1),@($d,0),@($d,-1))
$path = $cells | ForEach-Object { @{ x = $cx + $_[0] * $p; y = $cy + $_[1] * $p } }
$req = @{ contractVersion = '1.0'; method = 'SweepPath'; callerPluginId = '626labs.controller'; target = $target
          client = @{ w = 800; h = 599 }; path = $path; step = $p; dwellMs = 400
          guard = @{ x = 55; y = 289; w = 3; h = 3; expect = @{ r = 255; g = 19; b = 90 }; tolerance = 30 } } |
       ConvertTo-Json -Depth 5 -Compress
$pipe = [System.IO.Pipes.NamedPipeClientStream]::new('.', '626labs-ur-task', [System.IO.Pipes.PipeDirection]::InOut)
$pipe.Connect(2000)
$body = [Text.Encoding]::UTF8.GetBytes($req)
$len = [BitConverter]::GetBytes([int]$body.Length); [Array]::Reverse($len)   # 4-byte big-endian length prefix
$pipe.Write($len, 0, 4); $pipe.Write($body, 0, $body.Length); $pipe.Flush()
$hdr = [byte[]]::new(4); [void]$pipe.Read($hdr, 0, 4); [Array]::Reverse($hdr)
$resp = [byte[]]::new([BitConverter]::ToInt32($hdr, 0)); [void]$pipe.Read($resp, 0, $resp.Length)
[Text.Encoding]::UTF8.GetString($resp); $pipe.Dispose()
```

    (Use the guard values from the measured finder, not the example ones.) Wait for `swept 5 points` in `ur-task.log`, have Este move the mouse onto the window's title bar (a move, no click), wait 1 s, capture `run<k>-after`.
  - [ ] Per cell, the change between a before and an after frame, on the drag run's scale (a 9 x 9 box, channel differences summed, 0 to 765):

```powershell
Add-Type -AssemblyName System.Drawing
function Get-Avg([Drawing.Bitmap]$b, [int]$x, [int]$y) {
  $r = 0; $g = 0; $bl = 0; $n = 0
  for ($dy = -4; $dy -le 4; $dy++) { for ($dx = -4; $dx -le 4; $dx++) {
    $c = $b.GetPixel($x + $dx, $y + $dy); $r += $c.R; $g += $c.G; $bl += $c.B; $n++ } }
  return @($r / $n, $g / $n, $bl / $n)
}
$before = [Drawing.Bitmap]::new('<a before frame>'); $after = [Drawing.Bitmap]::new('<the matching after frame>')
foreach ($c in @(@($d,-1),@($d,0),@($d,1),@(-$d,-1),@(-$d,0),@(-$d,1))) {
  $x = $cx + $c[0] * $p; $y = $cy + $c[1] * $p
  $u = Get-Avg $before $x $y; $v = Get-Avg $after $x $y
  '{0},{1}: {2:0}' -f $c[0], $c[1], ([math]::Abs($u[0]-$v[0]) + [math]::Abs($u[1]-$v[1]) + [math]::Abs($u[2]-$v[2])) }
```

  - [ ] Repeat the three runs on an account without the power-ball pickaxe (or the main with a plain pickaxe equipped, if Este has one), with that account's dwell.
  - [ ] **Read it:** on the plain-pickaxe account, every swept cell over 30 and every control cell at 30 or under, in every run, means the pointer mines the blocks it passes: the design stands. On the main, note how often control cells change too (the power balls' share) and how far above them the swept cells sit. If on the plain-pickaxe account the swept cells do not change, **stop**: the sweep does not mine as assumed. Tell Este before running the pulse with sweeps; the pulse can be kept on ClearAt for stone by removing the finder's `guard`.
  - [ ] Write every number (the block size, `$d`, each cell's change per run, the noise) into `$sweep\2026-09-29-share\sweep-notes.txt`.
- [ ] **The hotbar's real extent (Review Focus 2).** On one calm frame at 100%, read the hotbar's left and right x and its top y, and the bottom icons' extent. If the hotbar is narrower than the game area, tell Este that `HudMask`'s bottom band could be narrowed to it so the near-side rows reach closer to the bottom edge; that is a follow-up plan, not part of this one. Add the numbers to `sweep-notes.txt`.
- [ ] **The pulse sweeps on the main.** The main's pulse targets its usual layer, `top`, with the default `sweepDwellMs` and `sweepNearSide` (re-import the pulse file if it changed).
  - [ ] `ur-ocr.log` shows `started: ... with the ore finder (P px blocks, radius R), sweeping stone (400 ms a point, near side on)`.
  - [ ] While riding down: no SweepPath in `ur-task.log` until the first read on the target layer.
  - [ ] At the target: `... is the target: K ore points, then a sweep of N points`; a ClearAt only when K is above 0; then Ur Task's `SweepPath (N points)` and `swept N points in S s`; then `swept N points: reading again` (or `cleared ore and swept N points: reading again`).
  - [ ] Watch one sweep: the button goes down on the block right of the character, the pointer walks the rings and the near-side rows, and it lets go back on that block. The character never drops (the block under it is never pressed); no popup opens.
  - [ ] The next read logs `the sweep changed M of P points` or `the sweep broke nothing (0 of P points changed): turning the camera ...`. Compare with what Este sees broke.
  - [ ] Este's call: blocks broken per minute against the ClearAt-only pulse from before this plan (rough is fine).
- [ ] **A weaker account's dwell.** An alt with `"sweepDwellMs": 700` in the pulse file (re-imported): Ur Task's start line reads `700 ms a point`.
- [ ] **Esc during a sweep.** The pointer lets go on the start block; the pulse stops with `was stopped in Ur Task (Esc or StopMacro)` and says Auto Mine is off.
- [ ] **Record and report.** Hand Este the summary: the pointer's share (with the power-ball numbers beside it), the hotbar extent, sweeps per minute and blocks broken, anything a release touched, and whether the near-side rows reached the blocks nearest the camera.
