# Ore Stop Pulse (Ur OCR half) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A per-account pulse loop in Ur OCR that rides Auto Mine in short bursts, stops, reads the mine layer on a calm frame, and at the account's target layer asks Ur Task to clear each ring spot (ore first), going back to the top when it is past the target or stuck on one layer.

**Architecture:** Five new pieces beside the 0.5.0 trigger engine, no bridge contract change. (1) `IMacroRunClient.GetPlaybackAsync`, Ur Task's existing `GetPlayback` call, so Ur OCR can wait for a macro to end. (2) `PulseConfig` per account, stored additively in `triggers.json` as `pulses`, imported with `--import-pulse`, which resolves Ur Task macro names to ids. (3) `MacroCall`: run one macro for one account and follow it to its end, one tick at a time, surviving busy refusals and interruptions. (4) `PulseLoop`: the per-account state machine (Riding, Pausing, Reading, Clearing, Bursting, GoingToTop, Stopped), reading the ring's existing spot triggers through `ISpotReader` and voting the layer with `RingTracker`'s static vote. (5) `PulseRunner`: ticks every enabled loop at 5 Hz, lets only the loop of the foreground account act, and tells the `TriggerCoordinator` which rings it owns so the 0.5.0 ring triggers stand down for those accounts.

**Tech Stack:** C# / .NET 10 (`net10.0-windows10.0.19041.0`), WPF, System.Drawing, System.Text.Json, xUnit 2.9.2. PowerShell 7 (`pwsh`) for the live task.

**Spec:** `../rororo-ur-task/docs/superpowers/specs/2026-09-28-ore-stop-pulse-design.md` (binding, sibling repo), which amends `../rororo-ur-task/docs/superpowers/specs/2026-09-27-ore-stop-loop-design.md`. Read both. The Ur Task half has its own plan; this plan only states what it expects from it (Global Constraints, "From Ur Task").

## Global Constraints

- **Paths:** every path in this plan is relative to the Ur-OCR repo root; the sibling repo is `..\rororo-ur-task`. Run every command from the Ur-OCR root and check with `git rev-parse --show-toplevel` after any `cd`. No absolute user-profile path goes into any committed file, this plan included.
- **Branch:** `feat/ore-stop-pulse`, created from `feat/ore-stop` at `05000ee` (Ur OCR 0.5.0, local, unpushed) in Task 1 Step 1. Never commit to `feat/ore-stop` or `main`.
- **Build:** `dotnet build rororo-ur-ocr.csproj` from the repo root. There is no tracked `.sln`; do not create one.
- **Tests:** `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj` (CI runs exactly this). The IntegrationTests project is not run. Baseline: **227 passed** (confirmed on `feat/ore-stop` at `05000ee`, 2026-09-28). Every task ends with the full suite green. **Known flake (pre-existing, preflight F7):** a test that builds a temp `TriggerStore` can fail once in `TriggerStore.WriteNow` at `File.Move` (`System.IO.FileSystem.MoveFile` in the stack). Rerun the suite once before debugging; a second failure is real.
- **No new NuGet packages.**
- **Commits:** conventional commits (`feat(scope): ...`, `fix(...)`, `test(...)`, `docs(...)`, `chore(...)`), sentence case after the colon, no emoji. Every commit message ends with a blank line then `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **Never `git add`** the untracked `AGENTS.md`, `CLAUDE.md` (GitNexus output) or `.gitnexus/`. Add files by path, never `git add -A` / `git add .`.
- **Bridge contract 1.0, unchanged.** `GetPlayback` already exists in Ur Task (`..\rororo-ur-task\src\Ipc\BridgeContract.cs`, `PlaybackRegistry.cs`). Wire shapes, copied verbatim:
  - request `{ "contractVersion": "1.0", "method": "GetPlayback", "playbackId": "<id RunMacro returned>", "callerPluginId": "626labs.ur-ocr" }`;
  - response `{ "ok": bool, "state": "running" | "finished" | "stopped" | "failed", "reason": string?, "detail": string?, "stepIndex": int? }`. On `failed`, `reason` is `check-failed`, `refused`, `aborted` or `error`, and `stepIndex` is 1-based;
  - unknown or expired id (kept 10 minutes after it ends, lost on an Ur Task restart): `{ "ok": false, "reason": "unknown-playback", "detail": "No playback with id '<id>'. Finished playbacks are kept for 10 minutes." }`;
  - an Ur Task that predates `GetPlayback` answers `{ "ok": false, "reason": "refused", "detail": "Unknown method 'GetPlayback'." }`.
  - Ur OCR's own synthetic refusals (no pipe, cancelled wait) reuse the `RunMacro` codes: `ur-task-not-running`, `ack-timeout`.
- **From Ur Task (the pulse half), by macro name.** The importer resolves names to ids, so ids are Ur Task's business; the ids listed are the ones already in `..\rororo-ur-task\docs\reference\events\macros\space-mine-ore-stop\` or, for the new macros, the ones this plan proposes:

  | name | id | what Ur OCR expects |
  | --- | --- | --- |
  | `Auto Mine off (checked)` | `0e5a0000-0000-4000-8000-000000000001` (exists) | press the pickaxe only when the dot is green; skip when red |
  | `Auto Mine on (checked)` | `0e5a0000-0000-4000-8000-000000000002` (exists) | press only when the dot is red; skip when green |
  | `Go to Top` | `0e5a0000-0000-4000-8000-000000000004` (exists) | checked press on Go to Top, settle, Auto Mine on |
  | `Clear spot N` ... `Clear spot NW` | proposed `0e5a0000-0000-4000-8000-000000000021` ... `...28` (N, NE, E, SE, S, SW, W, NW) | **new.** Move to the spot, check the white outline; outlined: hold until it breaks or the outline goes; not outlined: press nothing. No Auto Mine toggles inside (the loop owns Auto Mine). |

  The 0.5.0 names `Mine spot N` ... `Mine spot NW` and `Camera top-down` stay, because `--import-ring` still resolves them for the old path.
- **The skip signal (cross-repo requirement, the one the loop cannot work without):** a `Clear spot` playback that pressed nothing because there was no outline must end `state: "finished", reason: "skipped"`. `reason` on a finished playback is additive (the field exists; Ur Task sets it only on `failed` today). A `Clear spot` that ends `state: "failed", reason: "check-failed"` is **not** a skip: in Ur Task a hold's check fails only when it cannot run (the window cannot be seen, the check or reach box is outside the window), so the loop stops on it (preflight F1). A skip reported as a plain `finished` with no reason would read as "cleared" and the loop would never ride on; Task 9 Step 2 checks this live before anything else.
- **Esc before a macro plays (Ur Task half, preflight F2):** Esc during Ur Task's focus step, before the macro's first input, used to end the pass with a `Skipped` alt, which read as `finished`. The Ur Task half now ends any pass with a `Skipped` alt as `state: "stopped"`, and `MacroCall` already reads `stopped` as a stop (`Stopped_in_Ur_Task_stops`), so Esc at any point stops the loop. Nothing to change here; Task 9 Step 10.1 presses Esc during a hold.
- **Spec settings, per account, verbatim defaults:** target layer 1 to 3 (counting rock types down, so the ring's `layers` must be listed top first) and `top` or `oneAbove`; ride burst `burstMs` 2000; pause before reading `settleMs` 1000; rock cap `rockCapMinutes` 5.
- **Rock cap log line (spec, and 0.5.0 wording):** `Went to top: N minutes on the <layer> layer` (`1 minute` for one), in `ur-ocr.log`.
- **Account-aware:** a loop acts only while its own account is the foreground alt (same gate as triggers: a RoRoRo alt, not elevated). It never focuses a window, but every `RunMacro` makes Ur Task focus the target (`SequencePlayer`), so the foreground is read again immediately before each `RunMacro`, not reused from the start of the tick (preflight F5). A playback it already started is still polled while the account is in the background (polling is a read, not an action).
- **Storage is additive:** `triggers.json` stays schema 2. New key `pulses` (always written, `[]` when empty). Legacy files load unchanged. 0.5.0 can load a 0.6.0 file (System.Text.Json skips the unknown key) but drops `pulses` the next time it saves.
- **Output:** activity panel (kind `Pulse`, name `(pulse <userId>)`) and `%LOCALAPPDATA%\626Labs\rororo-ur-ocr\logs\ur-ocr.log`, every line prefixed `pulse <userId>: `. No notifications.
- **Copy:** sentence case, second person where it addresses the user, no emoji, em-dashes minimal.
- **Versioning:** `rororo-ur-ocr.csproj` `<Version>` and `manifest.json` `"version"` agree. Task 8 bumps both 0.5.0 to 0.6.0.
- **Account ids are not committed.** The pulse file holds Roblox user ids; it lives in the sweep folder, not in the repo. Committed examples use `123456789`.

## Review Focus

1. **Esc or StopMacro while a pulse macro runs, with the account in front:** the loop stops and logs why; it never runs the macro again on its own. Pinned by `Aborted_while_in_front_stops` (Task 4) and `A_playback_stopped_in_Ur_Task_stops_the_loop` (Task 5).
2. **Tabbing to another window mid-cycle:** nothing starts while the account is behind, no window is focused, a started playback is still followed, the ride timer keeps running so the loop pauses as soon as the account is back, time behind does not count toward the rock cap, and the foreground is checked again right before each macro starts. Pinned by `Waits_while_the_account_is_not_in_front` and `Time_behind_does_not_count_toward_the_rock_cap` (Task 5), `Does_not_start_when_the_account_left_the_front_during_the_tick` (Task 4), and `Only_the_account_in_front_acts` and `The_front_is_checked_again_right_before_a_macro_starts` (Task 6).
3. **A popup or captcha over the Auto Mine dot** (`check-failed` on a toggle), **or a Clear spot whose check could not run** (`check-failed` on a clear): the loop stops rather than keep pressing into it or ride on blind. Pinned by `A_failed_check_on_Auto_Mine_off_stops_the_loop` and `A_failed_check_on_a_clear_stops_the_loop` (Task 5).
4. **Ur Task restarted while a playback ran** (`unknown-playback`): the loop stops with a log line (controller ruling, 2026-09-28: a lost playback may or may not have pressed, and re-running it has no cap). Pinned by `A_lost_playback_is_reported` (Task 4), `A_lost_playback_stops_the_loop` and `A_lost_clear_stops_the_loop_without_running_it_again` (Task 5).
5. **Ur Task closed, or an Ur Task too old for `GetPlayback`:** the loop stops with one line naming the cause, instead of calling the pipe every tick. Pinned by `Missing_Ur_Task_stops` and `An_Ur_Task_without_GetPlayback_stops` (Task 4) and `Missing_Ur_Task_stops_with_a_log_line` (Task 5).

Also pinned, because both paths running on one ring would mean two macros fighting over one account: `A_ring_owned_by_a_pulse_loop_does_not_fire` (Task 6).

## Decisions this plan makes where the spec is open

- **Tick-driven state machine, not an async script.** Each 200 ms tick does everything it can and returns at the first wait (a playback still running, a timer, the account not in front, a busy retry). A fake clock then drives every test without real delays.
- **Riding on the way down is pulsed too.** The layer is only read on calm frames (spec, "Depth"), so every ride, including the first descent, lasts `burstMs` and then pauses to read. The spec gives no separate descent interval and the setting list is fixed, so the descent is slower than a straight ride (about 2 s of riding per 1 s pause plus two macro round trips). If the live run shows that as a problem, a separate `rideMs` is the follow-up.
- **"Top" and "one above".** The aim layer is `targetLayer` (`top`) or `targetLayer - 1` (`oneAbove`). The loop clears on the first calm read that shows the aim layer, which is its top, since every ride is short. `oneAbove` needs `targetLayer >= 2`. A read below the aim goes to top; above it rides on.
- **Ore first.** A spot is ore when its calm sample is more than its tolerance from every rock colour of the read layer and from every ignore colour of that spot. Ore spots go first, furthest from the rock set first; then the rest in ring order (N, NE, E, SE, S, SW, W, NW). Every spot is tried: Ur Task skips one with no outline.
- **After a pass.** Cleared anything: settle `settleMs` and read again without riding (Auto Mine is still off, so no toggle is sent). Cleared nothing: Bursting (Auto Mine on for `burstMs`), then pause and read.
- **Rock cap = no progress while clearing.** Progress is a cleared block or a new layer read. At each read **on the aim layer**, `rockCapMinutes` since the last progress sends the account to the top (spec step 5: "if clearing makes no progress"). Upper layers never trip it: the pulsed descent is slow, and a Go to Top from an upper layer would only land the account on the same layer again (preflight F4). Time with the account behind, paused (F9) or in dry run does not count: the clock is moved forward by that time on the first tick back in front (preflight F3). The Go to Top resets the clock.
- **No layer on a calm frame** (an effect or card over the ring, the camera knocked): log it and ride a burst. The pulse loop does not run `Camera top-down`; the camera is Este's digging view, set by hand (0.5.0 amendment).
- **How playback endings are read** (`MacroCall`): `finished` is done (for a clear: cleared), `finished` + `skipped` is a skip, `check-failed` stops the loop (on a toggle or Go to Top something is over the game; on a clear the check could not run, and a missing outline is `skipped`, never `check-failed`). `aborted` or `refused`: if the loop saw the account leave the front while the playback ran, it was a focus change and the macro runs again when the account is back (at most 5 times in a row); if the account stayed in front, it is taken as Esc and the loop stops. `stopped` (Esc or StopMacro, including Esc during Ur Task's focus step, per the Ur Task half's F2 fix) stops the loop. `unknown-playback` stops the loop (controller ruling). `busy`, `ack-timeout` and `no-targets-resolved` on RunMacro retry every second. `ur-task-not-running` and every other refusal stop the loop with the reason.
- **RunMacro targets the account's user id**, never `foreground`, and asks for no inter-alt delay (`interAltDelayMs: 0`): the account is already in front, so Ur Task's default 500 ms focus wait is dead time on every one of about ten macros a pass (preflight F6). **Ur Task focuses the target on every run** (`SequencePlayer` calls `SetForegroundWindow` even for an explicit user id), so a stale "in front" would pull a window back after Este tabs away. The loop's `foreground` flag is read once at the start of the runner's tick; `MacroCall` therefore asks the runner again (`inFront`, a fresh read of the foreground gate) immediately before each `RunMacro`, and waits if the account is no longer in front (preflight F5). What remains is the gap between that read and Ur Task's focus call, a pipe round trip; Task 9 Step 9.3 watches for it.
- **Pulse vs the 0.5.0 ring triggers: one owner per account and ring.** For an account with an enabled pulse on ring R, `TriggerCoordinator` skips every trigger of ring R (its eight spots, the rock cap, the camera rule) while that account is in front. Accounts without a pulse keep the 0.5.0 behaviour unchanged. The pulse loop reads the same eight spot triggers for their positions and sample boxes, whether or not they are enabled, so Este can also disable the old triggers outright without touching the pulse. Ownership holds while the pulse is enabled, even if its loop has stopped, so a stopped loop never silently hands the ring back to the old path.
- **Macros are resolved at import**, like `--import-ring`, and stored in the pulse as ids, so the loop never reads Ur Task's macro folder at run time. Re-import after regenerating Ur Task macros.
- **Pulses load once.** The runner builds a loop per enabled pulse on first tick; a changed pulse needs an Ur OCR restart, which the import already requires (it refuses while Ur OCR runs).
- **Paused (F9) and dry run hold the whole pulse runner.** Auto Mine keeps doing whatever it was doing in the game.
- **One pulse per account.** Import and `UpsertPulse` key on `accountUserId`.

## File map

| File | Status | Responsibility |
| --- | --- | --- |
| `Ipc/BridgeContract.cs` | modify | `GetPlaybackRequest`, `GetPlaybackResponse`, `ForPlayback`, `MethodGetPlayback`, `BridgeReasons`, `PlaybackStates` |
| `Ipc/IMacroRunClient.cs` | modify | `GetPlaybackAsync` (default refusal so existing fakes still compile) |
| `Ipc/MacroRunClient.cs` | modify | `GetPlaybackAsync`; one shared request/response exchange |
| `Storage/Pulse.cs` | create | `PulseMode`, `PulseMacros`, `PulseConfig`, `PulseMacroNames`, `PulseFile` |
| `Storage/PulseValidation.cs` | create | `Validate`, `SpotsOf` |
| `Storage/Trigger.cs` | modify | `TriggersFile.Pulses` |
| `Storage/TriggerStore.cs` | modify | `Pulses`, `UpsertPulse`, null-safe `pulses` on load |
| `Storage/PulseImporter.cs` | create | Pulse file to stored pulses, macro names to ids |
| `Engine/SpotReader.cs` | create | `SpotReading`, `ISpotReader`, `SpotReader` |
| `Engine/PulseOrder.cs` | create | Clear order: ore first |
| `Engine/MacroCall.cs` | create | `CallStatus`, `CallResult`, `MacroCall` |
| `Engine/PulseLoop.cs` | create | `PulseState`, `PulseLoop` |
| `Engine/PulseRunner.cs` | create | `IAccountLookup`, `PulseRunner` |
| `Engine/ActivityLog.cs` | modify | `ActivityKind.Pulse` |
| `Engine/TriggerCoordinator.cs` | modify | `ringOwner` hook: rings a pulse owns stand down |
| `PluginHost/AccountRegistry.cs` | modify | implements `IAccountLookup` |
| `PluginRuntime.cs` | modify | builds and runs the `PulseRunner` |
| `PulseImportCommand.cs` | create | `--import-pulse` headless command |
| `RingImportCommand.cs` | modify | `OtherInstanceRunning` becomes internal (shared) |
| `Program.cs` | modify | route `--import-pulse` |
| `rororo-ur-ocr.csproj`, `manifest.json` | modify | 0.6.0 |
| `CHANGELOG.md`, `README.md` | modify | 0.6.0 entry, pulse section |
| `docs/reference/ore-stop/mine8.measured.json` | create (Task 9) | measured Mine #8 ring with calm-fitted layers |
| `tests/RoRoRo.UrOcr.Tests/Ipc/GetPlaybackClientTests.cs` | create | wire shape and client behaviour |
| `tests/RoRoRo.UrOcr.Tests/Storage/PulseStorageTests.cs` | create | round trip, defaults, legacy files |
| `tests/RoRoRo.UrOcr.Tests/Storage/PulseValidationTests.cs` | create | every validation rule |
| `tests/RoRoRo.UrOcr.Tests/Engine/SpotReaderTests.cs` | create | reading spots from a painted capture |
| `tests/RoRoRo.UrOcr.Tests/Engine/PulseOrderTests.cs` | create | ore-first ranking |
| `tests/RoRoRo.UrOcr.Tests/Engine/PulseFakes.cs` | create | `PulseClock`, `ScriptedMacros` (shared by Tasks 4 to 6) |
| `tests/RoRoRo.UrOcr.Tests/Engine/MacroCallTests.cs` | create | every playback ending |
| `tests/RoRoRo.UrOcr.Tests/Engine/PulseLoopFixtures.cs` | create | `PulseFixtures`, `ScriptedReader` (shared by Tasks 5 and 6) |
| `tests/RoRoRo.UrOcr.Tests/Engine/PulseLoopTests.cs` | create | the state machine |
| `tests/RoRoRo.UrOcr.Tests/Engine/PulseRunnerTests.cs` | create | foreground gate, ownership, bad pulses |
| `tests/RoRoRo.UrOcr.Tests/Engine/RingOwnershipTests.cs` | create | coordinator stands down for an owned ring |
| `tests/RoRoRo.UrOcr.Tests/Storage/PulseImporterTests.cs` | create | import building and refusals |
| `tests/RoRoRo.UrOcr.Tests/PulseImportCommandTests.cs` | create | the headless command |

---

### Task 1: GetPlayback on the bridge client

**Files:**
- Modify: `Ipc/BridgeContract.cs`
- Modify: `Ipc/IMacroRunClient.cs`
- Modify: `Ipc/MacroRunClient.cs`
- Test: `tests/RoRoRo.UrOcr.Tests/Ipc/GetPlaybackClientTests.cs`

**Interfaces:**
- Consumes: existing `RunMacroRequest`, `RunMacroResponse`, `BridgeContract.ForMacro`, `BridgeContract.Json`, `BridgeContract.CallerId`, `FrameCodec` (internal, visible to tests), `MacroRunClient(Func<CancellationToken, Task<Stream?>>)` (internal test constructor).
- Produces:
  - `public sealed record GetPlaybackRequest(string ContractVersion, string Method, string PlaybackId, string CallerPluginId);`
  - `public sealed record GetPlaybackResponse(bool Ok, string? State, string? Reason, string? Detail, int? StepIndex);`
  - `BridgeContract.MethodGetPlayback = "GetPlayback"`, `BridgeContract.ForPlayback(string playbackId) -> GetPlaybackRequest`, `BridgeContract.ForMacro(string macroId, IReadOnlyList<string>? targets, int? interAltDelayMs = null)` (null is left off the wire, as before)
  - `public static class BridgeReasons` constants: `Busy "busy"`, `NotRunning "ur-task-not-running"`, `AckTimeout "ack-timeout"`, `NoTargets "no-targets-resolved"`, `UnknownPlayback "unknown-playback"`, `CheckFailed "check-failed"`, `Aborted "aborted"`, `Refused "refused"`, `Skipped "skipped"`
  - `public static class PlaybackStates` constants: `Running "running"`, `Finished "finished"`, `Stopped "stopped"`, `Failed "failed"`
  - `IMacroRunClient.GetPlaybackAsync(string playbackId, CancellationToken ct) -> Task<GetPlaybackResponse>` (default body returns `Ok=false, Reason="refused"`), implemented by `MacroRunClient`.
  - `IMacroRunClient.RunAsync(string macroId, IReadOnlyList<string>? targets, int? interAltDelayMs, CancellationToken ct)` (default body forwards to the three-argument `RunAsync`, dropping the delay, so existing fakes compile), implemented by `MacroRunClient`.

- [ ] **Step 1: Create the branch and commit this plan**

```powershell
git rev-parse --show-toplevel
git branch --show-current            # expect feat/ore-stop
git log -1 --format=%h               # expect 05000ee
git switch -c feat/ore-stop-pulse
git add docs/superpowers/plans/2026-09-28-ore-stop-pulse-ur-ocr.md
git commit -m "docs(plan): ore stop pulse, Ur OCR half" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

If `feat/ore-stop` has moved past `05000ee`, branch from its current head and note the hash in the commit body.

- [ ] **Step 2: Write the failing tests**

Create `tests/RoRoRo.UrOcr.Tests/Ipc/GetPlaybackClientTests.cs`:

```csharp
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using RoRoRo.UrOcr.Ipc;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Ipc;

/// <summary>
/// GetPlayback against a fake Ur Task on a real in-process pipe. The request is read back as raw
/// JSON so the wire names are pinned, not just the C# record.
/// </summary>
public class GetPlaybackClientTests
{
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

    [Fact]
    public void The_request_has_the_exact_wire_shape()
    {
        var json = JsonSerializer.Serialize(BridgeContract.ForPlayback("abc"), BridgeContract.Json);

        Assert.Equal(
            "{\"contractVersion\":\"1.0\",\"method\":\"GetPlayback\",\"playbackId\":\"abc\",\"callerPluginId\":\"626labs.ur-ocr\"}",
            json);
    }

    [Fact]
    public void A_response_reads_every_field()
    {
        var r = JsonSerializer.Deserialize<GetPlaybackResponse>(
            "{\"ok\":true,\"state\":\"failed\",\"reason\":\"check-failed\",\"detail\":\"step 2 saw red\",\"stepIndex\":2}",
            BridgeContract.Json)!;

        Assert.True(r.Ok);
        Assert.Equal(PlaybackStates.Failed, r.State);
        Assert.Equal(BridgeReasons.CheckFailed, r.Reason);
        Assert.Equal("step 2 saw red", r.Detail);
        Assert.Equal(2, r.StepIndex);
    }

    [Fact]
    public async Task Sends_GetPlayback_and_returns_the_state()
    {
        var (resp, req) = await RoundTrip(
            c => c.GetPlaybackAsync("pb-123", default),
            "{\"ok\":true,\"state\":\"finished\",\"reason\":\"skipped\",\"detail\":\"No outline.\"}");

        var root = req.RootElement;
        Assert.Equal("1.0", root.GetProperty("contractVersion").GetString());
        Assert.Equal("GetPlayback", root.GetProperty("method").GetString());
        Assert.Equal("pb-123", root.GetProperty("playbackId").GetString());
        Assert.Equal("626labs.ur-ocr", root.GetProperty("callerPluginId").GetString());
        Assert.True(resp.Ok);
        Assert.Equal("finished", resp.State);
        Assert.Equal("skipped", resp.Reason);
        Assert.Null(resp.StepIndex);
    }

    [Fact]
    public async Task An_unknown_playback_comes_back_as_a_refusal()
    {
        var (resp, _) = await RoundTrip(
            c => c.GetPlaybackAsync("gone", default),
            "{\"ok\":false,\"reason\":\"unknown-playback\",\"detail\":\"No playback with id 'gone'. Finished playbacks are kept for 10 minutes.\"}");

        Assert.False(resp.Ok);
        Assert.Null(resp.State);
        Assert.Equal(BridgeReasons.UnknownPlayback, resp.Reason);
    }

    [Fact]
    public async Task Through_the_interface_it_reaches_Ur_Task_not_the_default()
    {
        var (resp, _) = await RoundTrip(
            c => ((IMacroRunClient)c).GetPlaybackAsync("pb-1", default),
            "{\"ok\":true,\"state\":\"running\"}");

        Assert.True(resp.Ok);
        Assert.Equal(PlaybackStates.Running, resp.State);
    }

    [Fact]
    public async Task No_Ur_Task_is_not_running_and_never_throws()
    {
        var client = new MacroRunClient(_ => Task.FromResult<Stream?>(null));

        var resp = await client.GetPlaybackAsync("pb-1", default);

        Assert.False(resp.Ok);
        Assert.Equal(BridgeReasons.NotRunning, resp.Reason);
    }

    [Fact]
    public async Task A_cancelled_wait_is_an_ack_timeout()
    {
        var client = new MacroRunClient(_ => Task.FromException<Stream?>(new OperationCanceledException()));

        var resp = await client.GetPlaybackAsync("pb-1", default);

        Assert.False(resp.Ok);
        Assert.Equal(BridgeReasons.AckTimeout, resp.Reason);
    }

    [Fact]
    public async Task RunAsync_still_sends_RunMacro()
    {
        var (resp, req) = await RoundTrip(
            c => c.RunAsync("macro-1", null, default),
            "{\"ok\":true,\"playbackId\":\"pb-9\",\"queued\":false}");

        Assert.Equal("RunMacro", req.RootElement.GetProperty("method").GetString());
        Assert.Equal("macro-1", req.RootElement.GetProperty("macroId").GetString());
        Assert.False(req.RootElement.TryGetProperty("interAltDelayMs", out _));
        Assert.True(resp.Ok);
        Assert.Equal("pb-9", resp.PlaybackId);
    }

    [Fact]
    public async Task RunAsync_can_ask_for_no_inter_alt_delay()
    {
        var (resp, req) = await RoundTrip(
            c => ((IMacroRunClient)c).RunAsync("macro-1", new[] { "42" }, 0, default),
            "{\"ok\":true,\"playbackId\":\"pb-9\",\"queued\":false}");

        var root = req.RootElement;
        Assert.Equal(0, root.GetProperty("interAltDelayMs").GetInt32());
        Assert.Equal("42", root.GetProperty("targets")[0].GetString());
        Assert.True(resp.Ok);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~GetPlaybackClientTests"`
Expected: build FAILS with `CS0117: 'BridgeContract' does not contain a definition for 'ForPlayback'` (and `GetPlaybackResponse`, `PlaybackStates`, `BridgeReasons` not found).

- [ ] **Step 4: Add the contract types**

Replace `Ipc/BridgeContract.cs` with:

```csharp
// Ipc/BridgeContract.cs
using System.Text.Json;
using System.Text.Json.Serialization;
namespace RoRoRo.UrOcr.Ipc;

public sealed record RunMacroRequest(string ContractVersion, string Method, string MacroId,
    IReadOnlyList<string>? Targets, int? InterAltDelayMs, string? CallerPluginId);

public sealed record RunMacroResponse(bool Ok, string? PlaybackId, bool Queued, string? Reason, string? Detail);

/// <summary>Ur Task's GetPlayback request (bridge 1.x, Ur Task 0.9.0 and later).</summary>
public sealed record GetPlaybackRequest(string ContractVersion, string Method, string PlaybackId, string CallerPluginId);

/// <summary>
/// Ur Task's answer: State is running | finished | stopped | failed. On failed, Reason is
/// check-failed, refused, aborted or error, and StepIndex is 1-based. Ok=false with Reason
/// "unknown-playback" means the id is unknown or expired (kept 10 minutes after it ends).
/// </summary>
public sealed record GetPlaybackResponse(bool Ok, string? State, string? Reason, string? Detail, int? StepIndex);

/// <summary>Reason codes on the bridge, Ur Task's and Ur OCR's own synthetic ones.</summary>
public static class BridgeReasons
{
    public const string Busy = "busy";
    public const string NotRunning = "ur-task-not-running";
    public const string AckTimeout = "ack-timeout";
    public const string NoTargets = "no-targets-resolved";
    public const string UnknownPlayback = "unknown-playback";
    public const string CheckFailed = "check-failed";
    public const string Aborted = "aborted";
    public const string Refused = "refused";
    /// <summary>On a finished playback: it pressed nothing (a Clear spot with no outline). Set by
    /// the Ur Task half of the ore stop pulse.</summary>
    public const string Skipped = "skipped";
}

public static class PlaybackStates
{
    public const string Running = "running";
    public const string Finished = "finished";
    public const string Stopped = "stopped";
    public const string Failed = "failed";
}

public static class BridgeContract
{
    public const string PipeName = "626labs-ur-task";
    public const string Method = "RunMacro";
    public const string MethodGetPlayback = "GetPlayback";
    public const string CallerId = "626labs.ur-ocr";
    public const string ContractVersion = "1.0";

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>interAltDelayMs: Ur Task's wait between focusing a target and playing; null
    /// leaves it to the macro (500 ms by default) and is left off the wire.</summary>
    public static RunMacroRequest ForMacro(string macroId, IReadOnlyList<string>? targets, int? interAltDelayMs = null)
        => new(ContractVersion, Method, macroId,
               targets is { Count: > 0 } ? targets : new[] { "foreground" }, interAltDelayMs, CallerId);

    public static GetPlaybackRequest ForPlayback(string playbackId)
        => new(ContractVersion, MethodGetPlayback, playbackId, CallerId);
}
```

Replace `Ipc/IMacroRunClient.cs` with:

```csharp
// Ipc/IMacroRunClient.cs
namespace RoRoRo.UrOcr.Ipc;

public interface IMacroRunClient
{
    Task<RunMacroResponse> RunAsync(string macroId, IReadOnlyList<string>? targets, CancellationToken ct);

    /// <summary>RunAsync with Ur Task's focus-to-play wait set (0 when the account is already in
    /// front). The default drops the delay so a client that cannot send it (the trigger tests'
    /// fakes) still compiles; MacroRunClient sends it.</summary>
    Task<RunMacroResponse> RunAsync(string macroId, IReadOnlyList<string>? targets, int? interAltDelayMs, CancellationToken ct)
        => RunAsync(macroId, targets, ct);

    /// <summary>How a playback RunAsync started is going. The default answers "refused" so a
    /// client that cannot ask (the trigger tests' fakes) still compiles; MacroRunClient asks Ur Task.</summary>
    Task<GetPlaybackResponse> GetPlaybackAsync(string playbackId, CancellationToken ct)
        => Task.FromResult(new GetPlaybackResponse(false, null, BridgeReasons.Refused,
            "This client cannot ask Ur Task about playbacks.", null));
}
```

- [ ] **Step 5: Implement GetPlaybackAsync on the client**

Replace `Ipc/MacroRunClient.cs` with:

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

    /// <summary>Production constructor — opens the real named pipe.</summary>
    public MacroRunClient() : this(DefaultOpenAsync) { }

    /// <summary>Test constructor — inject any stream-opener (e.g. in-process pipe pair).</summary>
    internal MacroRunClient(Func<CancellationToken, Task<Stream?>> openPipe) => _openPipe = openPipe;

    public Task<RunMacroResponse> RunAsync(string macroId, IReadOnlyList<string>? targets, CancellationToken ct) =>
        RunAsync(macroId, targets, null, ct);

    public Task<RunMacroResponse> RunAsync(string macroId, IReadOnlyList<string>? targets, int? interAltDelayMs, CancellationToken ct) =>
        ExchangeAsync(BridgeContract.ForMacro(macroId, targets, interAltDelayMs),
            (reason, detail) => new RunMacroResponse(false, null, false, reason, detail),
            // The wait was cancelled (e.g. the coordinator's per-tick watchdog) before Ur Task
            // acked. Ur Task acks as soon as it accepts or refuses a run (a running sequence is
            // refused as "busy"), so this means the pipe stalled and the run may or may not have
            // started. Report that honestly, not "not running".
            "Macro request sent; Ur Task did not ack within the tick window (long macro?).",
            ct);

    /// <summary>How a playback RunAsync started is going. Ur Task keeps an ended playback for 10
    /// minutes; after that, or after an Ur Task restart, the answer is "unknown-playback".</summary>
    public Task<GetPlaybackResponse> GetPlaybackAsync(string playbackId, CancellationToken ct) =>
        ExchangeAsync(BridgeContract.ForPlayback(playbackId),
            (reason, detail) => new GetPlaybackResponse(false, null, reason, detail, null),
            "Asked Ur Task how a playback is going; it did not answer within the tick window.",
            ct);

    /// <summary>One request, one response, one connection. Never throws: a missing Ur Task, a closed
    /// pipe or a cancelled wait each come back as a refusal built by <paramref name="fail"/>.</summary>
    private async Task<TResponse> ExchangeAsync<TRequest, TResponse>(
        TRequest request, Func<string, string, TResponse> fail, string timeoutDetail, CancellationToken ct)
    {
        Stream? pipe = null;
        try
        {
            pipe = await _openPipe(ct).ConfigureAwait(false);
            if (pipe is null)
                return fail(BridgeReasons.NotRunning, "Ur Task is not running or refused the connection.");

            var reqBytes = JsonSerializer.SerializeToUtf8Bytes(request, BridgeContract.Json);
            await FrameCodec.WriteFrameAsync(pipe, reqBytes, ct).ConfigureAwait(false);

            var respBytes = await FrameCodec.ReadFrameAsync(pipe, ct).ConfigureAwait(false);
            if (respBytes is null)
                return fail(BridgeReasons.Refused, "Ur Task closed the connection without a response.");

            return JsonSerializer.Deserialize<TResponse>(respBytes, BridgeContract.Json)
                   ?? fail(BridgeReasons.Refused, "Empty response.");
        }
        catch (OperationCanceledException)
        {
            return fail(BridgeReasons.AckTimeout, timeoutDetail);
        }
        catch (Exception ex)
        {
            return fail(BridgeReasons.NotRunning, ex.Message);
        }
        finally
        {
            if (pipe is not null)
                await pipe.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task<Stream?> DefaultOpenAsync(CancellationToken ct)
    {
        var pipe = new NamedPipeClientStream(".", BridgeContract.PipeName,
            PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(ConnectTimeoutMs, ct).ConfigureAwait(false);
            return pipe;
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            return null;
        }
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~GetPlaybackClientTests|FullyQualifiedName~MacroRunClientTests|FullyQualifiedName~BridgeContractTests"`
Expected: PASS (9 new, 3 + 3 existing).

Then the full suite: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj`
Expected: **236 passed** (227 + 9). The five existing `IMacroRunClient` fakes compile unchanged because of the default methods.

- [ ] **Step 7: Commit**

```powershell
git add Ipc/BridgeContract.cs Ipc/IMacroRunClient.cs Ipc/MacroRunClient.cs tests/RoRoRo.UrOcr.Tests/Ipc/GetPlaybackClientTests.cs
git commit -m "feat(ipc): ask Ur Task how a playback is going (GetPlayback)" -m "Same bridge contract 1.0 wire shape Ur Task already serves; unknown-playback and Ur OCR's own not-running and ack-timeout come back as refusals, never exceptions. RunMacro can now send interAltDelayMs." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Pulse settings in storage

**Files:**
- Create: `Storage/Pulse.cs`
- Create: `Storage/PulseValidation.cs`
- Modify: `Storage/Trigger.cs` (the `TriggersFile` class at the end of the file)
- Modify: `Storage/TriggerStore.cs`
- Test: `tests/RoRoRo.UrOcr.Tests/Storage/PulseStorageTests.cs`
- Test: `tests/RoRoRo.UrOcr.Tests/Storage/PulseValidationTests.cs`

**Interfaces:**
- Consumes: `RingDefinition`, `LayerDefinition`, `RingSpot`, `Trigger`, `TriggerMode`, `ColorCriteria`, `TriggerValidation.Find(rings, id)`, `TriggerValidation.Validate(RingDefinition)`, `MeasuredRing.RingOrder`, `TriggerJsonOptions.Default` (internal, camelCase, camelCase enums).
- Produces:
  - `public enum PulseMode { Top, OneAbove }` (JSON `"top"`, `"oneAbove"`)
  - `public sealed record PulseMacros(string AutoMineOff, string AutoMineOn, string GoToTop, IReadOnlyList<string> Clear)` (Clear: 8 macro ids, index = ring order)
  - `public sealed record PulseConfig(long AccountUserId, string RingId, int TargetLayer, PulseMode Mode = PulseMode.Top, int BurstMs = 2000, int SettleMs = 1000, int RockCapMinutes = 5, bool Enabled = true, PulseMacros? Macros = null)` with `[JsonIgnore] int AimLayer`
  - `public static class PulseMacroNames { AutoMineOff, AutoMineOn, GoToTop, Clear(string spot) }`
  - `public sealed record PulseFile(int Schema, IReadOnlyList<PulseConfig> Pulses)` with `CurrentSchema = 1` and `Load(string path)`
  - `PulseValidation.Validate(PulseConfig, IReadOnlyList<RingDefinition>, IReadOnlyList<Trigger>) -> string?`, `PulseValidation.SpotsOf(string ringId, IReadOnlyList<Trigger>) -> IReadOnlyList<Trigger>`, `PulseValidation.SpotCount = 8`
  - `TriggersFile.Pulses : List<PulseConfig>`; `TriggerStore.Pulses : IReadOnlyList<PulseConfig>`; `TriggerStore.UpsertPulse(PulseConfig)` (replaces by `AccountUserId`)

- [ ] **Step 1: Write the failing tests**

Create `tests/RoRoRo.UrOcr.Tests/Storage/PulseStorageTests.cs`:

```csharp
using System.IO;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Storage;

public class PulseStorageTests
{
    private static string TempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "urocr-tests", Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private static PulseMacros Macros() => new("id-off", "id-on", "id-top",
        MeasuredRing.RingOrder.Select(n => $"id-clear-{n}").ToList());

    private static PulseConfig Pulse(long account = 42, int target = 3, PulseMode mode = PulseMode.Top) =>
        new(account, "mine8", target, mode, Macros: Macros());

    [Fact]
    public void Pulses_survive_a_reload()
    {
        var path = TempFile();
        new TriggerStore(path).UpsertPulse(Pulse(mode: PulseMode.OneAbove) with { BurstMs = 1500, RockCapMinutes = 7 });

        var p = Assert.Single(new TriggerStore(path).Pulses);

        Assert.Equal(42, p.AccountUserId);
        Assert.Equal("mine8", p.RingId);
        Assert.Equal(3, p.TargetLayer);
        Assert.Equal(PulseMode.OneAbove, p.Mode);
        Assert.Equal(1500, p.BurstMs);
        Assert.Equal(1000, p.SettleMs);
        Assert.Equal(7, p.RockCapMinutes);
        Assert.True(p.Enabled);
        Assert.Equal("id-off", p.Macros!.AutoMineOff);
        Assert.Equal("id-on", p.Macros.AutoMineOn);
        Assert.Equal("id-top", p.Macros.GoToTop);
        Assert.Equal(MeasuredRing.RingOrder.Select(n => $"id-clear-{n}"), p.Macros.Clear);
    }

    [Fact]
    public void The_file_uses_camel_case_keys_and_the_spec_mode_names()
    {
        var path = TempFile();
        new TriggerStore(path).UpsertPulse(Pulse(mode: PulseMode.OneAbove));

        var json = File.ReadAllText(path);

        Assert.Contains("\"pulses\"", json);
        Assert.Contains("\"accountUserId\": 42", json);
        Assert.Contains("\"targetLayer\": 3", json);
        Assert.Contains("\"mode\": \"oneAbove\"", json);
        Assert.Contains("\"burstMs\": 2000", json);
        Assert.Contains("\"settleMs\": 1000", json);
        Assert.Contains("\"rockCapMinutes\": 5", json);
        Assert.Contains("\"autoMineOff\": \"id-off\"", json);
        Assert.DoesNotContain("aimLayer", json);
    }

    [Fact]
    public void A_legacy_file_without_pulses_loads_empty()
    {
        var path = TempFile();
        File.WriteAllText(path, "{\"schemaVersion\":2,\"rings\":[],\"triggers\":[]}");

        Assert.Empty(new TriggerStore(path).Pulses);
    }

    [Fact]
    public void A_null_pulses_list_loads_empty()
    {
        var path = TempFile();
        File.WriteAllText(path, "{\"schemaVersion\":2,\"rings\":[],\"triggers\":[],\"pulses\":null}");

        Assert.Empty(new TriggerStore(path).Pulses);
    }

    [Fact]
    public void Missing_settings_take_the_spec_defaults()
    {
        var path = TempFile();
        File.WriteAllText(path,
            "{\"schemaVersion\":2,\"rings\":[],\"triggers\":[],\"pulses\":[{\"accountUserId\":42,\"ringId\":\"mine8\",\"targetLayer\":2}]}");

        var p = Assert.Single(new TriggerStore(path).Pulses);

        Assert.Equal(PulseMode.Top, p.Mode);
        Assert.Equal(2000, p.BurstMs);
        Assert.Equal(1000, p.SettleMs);
        Assert.Equal(5, p.RockCapMinutes);
        Assert.True(p.Enabled);
        Assert.Null(p.Macros);
    }

    [Fact]
    public void UpsertPulse_replaces_the_same_accounts_pulse()
    {
        var store = new TriggerStore(TempFile());
        store.UpsertPulse(Pulse(target: 3));
        store.UpsertPulse(Pulse(account: 43, target: 1));
        store.UpsertPulse(Pulse(target: 2));

        Assert.Equal(2, store.Pulses.Count);
        Assert.Equal(2, store.Pulses.Single(p => p.AccountUserId == 42).TargetLayer);
    }

    [Fact]
    public void The_aim_layer_is_the_target_or_the_one_above()
    {
        Assert.Equal(3, Pulse(target: 3).AimLayer);
        Assert.Equal(2, Pulse(target: 3, mode: PulseMode.OneAbove).AimLayer);
    }

    [Fact]
    public void A_pulse_file_loads_with_defaults()
    {
        var path = TempFile();
        File.WriteAllText(path,
            "{\"schema\":1,\"pulses\":[{\"accountUserId\":123456789,\"ringId\":\"mine8\",\"targetLayer\":3,\"mode\":\"oneAbove\"}]}");

        var f = PulseFile.Load(path);

        Assert.Equal(1, f.Schema);
        var p = Assert.Single(f.Pulses);
        Assert.Equal(123456789, p.AccountUserId);
        Assert.Equal(PulseMode.OneAbove, p.Mode);
        Assert.Equal(2000, p.BurstMs);
    }
}
```

Create `tests/RoRoRo.UrOcr.Tests/Storage/PulseValidationTests.cs`:

```csharp
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Storage;

public class PulseValidationTests
{
    private static RingDefinition Ring() => new("mine8", "Mine #8", new[]
    {
        new LayerDefinition("navy", new[] { new Rgb(30, 30, 90) }),
        new LayerDefinition("black", new[] { new Rgb(10, 10, 12) }),
        new LayerDefinition("grey", new[] { new Rgb(120, 120, 120) }),
    }, MinLayerSpots: 3);

    private static Trigger Spot(int order, string ringId = "mine8", bool enabled = true) => new()
    {
        Id = Guid.NewGuid(),
        Name = $"Mine #8: {MeasuredRing.RingOrder[order]}",
        Enabled = enabled,
        Region = new RegionRect(100 + order * 20, 100, 9, 9),
        Mode = TriggerMode.Color,
        Color = new ColorCriteria(new Rgb(0, 0, 0), 20, ColorSamplingMode.SinglePixel,
            Point: new PickPoint(4, 4), Box: new SampleBox(), NoneOf: new[] { new Rgb(135, 206, 235) }),
        Keybind = new KeyCombo("F13", Array.Empty<string>()),
        Ring = new RingSpot(ringId, order),
    };

    private static IReadOnlyList<Trigger> Spots(int count = 8) => Enumerable.Range(0, count).Select(o => Spot(o)).ToList();

    private static PulseMacros Macros(int clears = 8) => new("id-off", "id-on", "id-top",
        MeasuredRing.RingOrder.Take(clears).Select(n => $"id-clear-{n}").ToList());

    private static PulseConfig Pulse() => new(42, "mine8", 3, Macros: Macros());

    private static string? Check(PulseConfig p, IReadOnlyList<Trigger>? spots = null, params RingDefinition[] rings) =>
        PulseValidation.Validate(p, rings.Length == 0 ? new[] { Ring() } : rings, spots ?? Spots());

    [Fact]
    public void A_complete_pulse_is_valid() => Assert.Null(Check(Pulse()));

    [Fact]
    public void The_account_must_be_set() =>
        Assert.Contains("accountUserId", Check(Pulse() with { AccountUserId = 0 }));

    [Fact]
    public void The_ring_must_be_imported_first() =>
        Assert.Contains("import the ring first", Check(Pulse() with { RingId = "mine9" }));

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void The_target_must_be_one_of_the_rings_layers(int target) =>
        Assert.Contains("targetLayer must be 1 to 3", Check(Pulse() with { TargetLayer = target }));

    [Fact]
    public void One_above_needs_a_layer_above_the_target() =>
        Assert.Contains("nothing is above layer 1", Check(Pulse() with { TargetLayer = 1, Mode = PulseMode.OneAbove }));

    [Fact]
    public void One_above_layer_two_is_fine() =>
        Assert.Null(Check(Pulse() with { TargetLayer = 2, Mode = PulseMode.OneAbove }));

    [Theory]
    [InlineData(50, 1000, 5, "burstMs")]
    [InlineData(60001, 1000, 5, "burstMs")]
    [InlineData(2000, -1, 5, "settleMs")]
    [InlineData(2000, 10001, 5, "settleMs")]
    [InlineData(2000, 1000, 0, "rockCapMinutes")]
    [InlineData(2000, 1000, 61, "rockCapMinutes")]
    public void Timings_must_be_in_range(int burst, int settle, int cap, string field) =>
        Assert.Contains(field, Check(Pulse() with { BurstMs = burst, SettleMs = settle, RockCapMinutes = cap }));

    [Fact]
    public void The_ring_needs_all_eight_spot_triggers() =>
        Assert.Contains("needs 8 spot triggers", Check(Pulse(), Spots(7)));

    [Fact]
    public void The_macros_must_be_resolved() =>
        Assert.Contains("macros are missing", Check(Pulse() with { Macros = null }));

    [Fact]
    public void One_clear_macro_per_spot() =>
        Assert.Contains("macros.clear", Check(Pulse() with { Macros = Macros(clears: 7) }));

    [Fact]
    public void Blank_toggle_macros_are_refused() =>
        Assert.Contains("Auto Mine off", Check(Pulse() with { Macros = Macros() with { AutoMineOn = " " } }));

    [Fact]
    public void SpotsOf_takes_this_rings_colour_spots_in_ring_order_enabled_or_not()
    {
        var triggers = new List<Trigger> { Spot(2), Spot(0, enabled: false), Spot(1, ringId: "other"), Spot(1) };

        var spots = PulseValidation.SpotsOf("MINE8", triggers);

        Assert.Equal(new[] { 0, 1, 2 }, spots.Select(t => t.Ring!.Order));
        Assert.All(spots, t => Assert.Equal("mine8", t.Ring!.RingId));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~PulseStorageTests|FullyQualifiedName~PulseValidationTests"`
Expected: build FAILS with `CS0246: The type or namespace name 'PulseConfig' could not be found`.

- [ ] **Step 3: Add the pulse records**

Create `Storage/Pulse.cs`:

```csharp
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoRoRo.UrOcr.Storage;

/// <summary>Top: clear on the target layer. OneAbove: clear on the layer above it, for an
/// under-powered account. JSON "top" / "oneAbove".</summary>
public enum PulseMode { Top, OneAbove }

/// <summary>Ur Task macro ids the loop runs, resolved from names at import. Clear holds one id per
/// ring spot, index = ring order (N, NE, E, SE, S, SW, W, NW).</summary>
public sealed record PulseMacros(string AutoMineOff, string AutoMineOn, string GoToTop, IReadOnlyList<string> Clear);

/// <summary>
/// One account's ore stop pulse (spec 2026-09-28, "Settings per account"). TargetLayer counts the
/// ring's layers from the top, 1-based. Stored in triggers.json under "pulses"; missing keys load
/// as the spec defaults.
/// </summary>
public sealed record PulseConfig(
    long AccountUserId,
    string RingId,
    int TargetLayer,
    PulseMode Mode = PulseMode.Top,
    int BurstMs = PulseConfig.DefaultBurstMs,
    int SettleMs = PulseConfig.DefaultSettleMs,
    int RockCapMinutes = PulseConfig.DefaultRockCapMinutes,
    bool Enabled = true,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PulseMacros? Macros = null)
{
    public const int DefaultBurstMs = 2000;
    public const int DefaultSettleMs = 1000;
    public const int DefaultRockCapMinutes = 5;

    /// <summary>The 1-based layer the loop clears on: the target, or the one above it.</summary>
    [JsonIgnore]
    public int AimLayer => Mode == PulseMode.OneAbove ? TargetLayer - 1 : TargetLayer;
}

/// <summary>The Ur Task macro names the importer looks up. They must match the Ur Task half exactly.</summary>
public static class PulseMacroNames
{
    public const string AutoMineOff = "Auto Mine off (checked)";
    public const string AutoMineOn = "Auto Mine on (checked)";
    public const string GoToTop = "Go to Top";
    public static string Clear(string spot) => $"Clear spot {spot}";
}

/// <summary>The file --import-pulse reads: schema 1 and one entry per account. Any "macros" in it
/// are ignored; the importer looks them up in Ur Task.</summary>
public sealed record PulseFile(int Schema, IReadOnlyList<PulseConfig> Pulses)
{
    public const int CurrentSchema = 1;

    public static PulseFile Load(string path) =>
        JsonSerializer.Deserialize<PulseFile>(File.ReadAllText(path), TriggerJsonOptions.Default)
        ?? throw new InvalidDataException($"{path} is empty.");
}
```

Create `Storage/PulseValidation.cs`:

```csharp
namespace RoRoRo.UrOcr.Storage;

/// <summary>Whether a pulse can run. Null means valid; otherwise one sentence naming the first problem.</summary>
public static class PulseValidation
{
    public const int SpotCount = 8;
    public const int MinBurstMs = 100;
    public const int MaxBurstMs = 60_000;
    public const int MaxSettleMs = 10_000;
    public const int MaxRockCapMinutes = 60;

    /// <summary>The ring's spot triggers in ring order, enabled or not: the pulse reads their
    /// positions and sample boxes even when the 0.5.0 triggers themselves are switched off.</summary>
    public static IReadOnlyList<Trigger> SpotsOf(string ringId, IReadOnlyList<Trigger> triggers) =>
        triggers
            .Where(t => t.Mode == TriggerMode.Color && t.Color is not null && t.Ring is { } r
                        && string.Equals(r.RingId, ringId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Ring!.Order)
            .ToList();

    public static string? Validate(PulseConfig p, IReadOnlyList<RingDefinition> rings, IReadOnlyList<Trigger> triggers)
    {
        if (p.AccountUserId <= 0) return "accountUserId must be the account's Roblox user id.";
        if (string.IsNullOrWhiteSpace(p.RingId)) return "ringId is missing.";
        var ring = TriggerValidation.Find(rings, p.RingId);
        if (ring is null) return $"Ring {p.RingId} is not defined: import the ring first (--import-ring).";
        if (TriggerValidation.Validate(ring) is { } ringProblem) return $"Ring {p.RingId}: {ringProblem}";

        var layers = ring.Layers.Count;
        if (p.TargetLayer < 1 || p.TargetLayer > layers)
            return $"targetLayer must be 1 to {layers} (ring {p.RingId}'s layers, top first), not {p.TargetLayer}.";
        if (p.Mode == PulseMode.OneAbove && p.TargetLayer < 2)
            return "Mode oneAbove needs targetLayer 2 or more: nothing is above layer 1.";
        if (p.BurstMs < MinBurstMs || p.BurstMs > MaxBurstMs)
            return $"burstMs must be {MinBurstMs} to {MaxBurstMs}, not {p.BurstMs}.";
        if (p.SettleMs < 0 || p.SettleMs > MaxSettleMs)
            return $"settleMs must be 0 to {MaxSettleMs}, not {p.SettleMs}.";
        if (p.RockCapMinutes < 1 || p.RockCapMinutes > MaxRockCapMinutes)
            return $"rockCapMinutes must be 1 to {MaxRockCapMinutes}, not {p.RockCapMinutes}.";

        var orders = SpotsOf(p.RingId, triggers).Select(t => t.Ring!.Order).ToList();
        if (!orders.SequenceEqual(Enumerable.Range(0, SpotCount)))
            return $"Ring {p.RingId} needs {SpotCount} spot triggers, orders 0 to {SpotCount - 1}; it has {orders.Count}. Import the ring first (--import-ring).";

        var m = p.Macros;
        if (m is null) return "macros are missing: import the pulse with --import-pulse, which looks them up in Ur Task.";
        if (string.IsNullOrWhiteSpace(m.AutoMineOff) || string.IsNullOrWhiteSpace(m.AutoMineOn) || string.IsNullOrWhiteSpace(m.GoToTop))
            return "macros must name Auto Mine off, Auto Mine on and Go to Top.";
        if (m.Clear is null || m.Clear.Count != SpotCount || m.Clear.Any(string.IsNullOrWhiteSpace))
            return $"macros.clear must list {SpotCount} macro ids, one per spot in ring order.";
        return null;
    }
}
```

- [ ] **Step 4: Store the pulses next to the triggers**

In `Storage/Trigger.cs`, replace the `TriggersFile` class:

```csharp
public sealed class TriggersFile
{
    public int SchemaVersion { get; set; } = 2;
    public List<RingDefinition> Rings { get; set; } = new();
    public List<Trigger> Triggers { get; set; } = new();
    /// <summary>Ore stop pulse loops, one per account (0.6.0, additive: absent loads as empty).</summary>
    public List<PulseConfig> Pulses { get; set; } = new();
}
```

In `Storage/TriggerStore.cs`, add after the `Rings` property:

```csharp
    public IReadOnlyList<PulseConfig> Pulses
    {
        get { lock (_lock) return _state.Pulses.ToArray(); }
    }
```

add after `UpsertRing`:

```csharp
    /// <summary>Adds the pulse, or replaces the one for the same account: one pulse per account.</summary>
    public void UpsertPulse(PulseConfig pulse)
    {
        lock (_lock)
        {
            var idx = _state.Pulses.FindIndex(x => x.AccountUserId == pulse.AccountUserId);
            if (idx < 0) _state.Pulses.Add(pulse); else _state.Pulses[idx] = pulse;
            WriteNow();
        }
    }
```

and in `Load()`, after `_state.Rings ??= new();`:

```csharp
            _state.Pulses ??= new();  // "pulses": null in a hand-edited file
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~PulseStorageTests|FullyQualifiedName~PulseValidationTests"`
Expected: PASS (8 storage, 18 validation cases counting theory rows).

Full suite: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj`
Expected: **262 passed** (236 + 26).

- [ ] **Step 6: Commit**

```powershell
git add Storage/Pulse.cs Storage/PulseValidation.cs Storage/Trigger.cs Storage/TriggerStore.cs tests/RoRoRo.UrOcr.Tests/Storage/PulseStorageTests.cs tests/RoRoRo.UrOcr.Tests/Storage/PulseValidationTests.cs
git commit -m "feat(storage): per-account pulse settings in triggers.json" -m "Additive pulses list, schema stays 2. Target layer and top or one above, burst 2000 ms, settle 1000 ms, rock cap 5 minutes, and the resolved Ur Task macro ids." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Reading the ring at rest and ranking the clears

**Files:**
- Create: `Engine/SpotReader.cs`
- Create: `Engine/PulseOrder.cs`
- Test: `tests/RoRoRo.UrOcr.Tests/Engine/SpotReaderTests.cs`
- Test: `tests/RoRoRo.UrOcr.Tests/Engine/PulseOrderTests.cs`

**Interfaces:**
- Consumes: `ICaptureSource.Capture(RegionRect)`, `IWindowMetrics`, `TriggerRegionResolver.Resolve(Trigger, int pid, IWindowMetrics)`, `ColorMatcher.Sample(Bitmap, ColorCriteria, RegionRect?)`, `ColorMatcher.Distance(Rgb, Rgb)`, `Trigger.Ring`, `Trigger.Color`.
- Produces:
  - `public sealed record SpotReading(int Order, Rgb Sampled, int ToleranceRgb, IReadOnlyList<Rgb> Ignore);`
  - `public interface ISpotReader { IReadOnlyList<SpotReading>? Read(int pid, IReadOnlyList<Trigger> spots); }` (null: the window could not be resolved)
  - `public sealed class SpotReader(ICaptureSource capture, IWindowMetrics metrics) : ISpotReader`
  - `PulseOrder.Rank(IReadOnlyList<SpotReading> spots, IReadOnlyList<Rgb> rock) -> IReadOnlyList<int>` (ring orders, ore first), `PulseOrder.IsOre(SpotReading, IReadOnlyList<Rgb>) -> bool`, `PulseOrder.Nearest(Rgb, IReadOnlyList<Rgb>) -> double`

- [ ] **Step 1: Write the failing tests**

Create `tests/RoRoRo.UrOcr.Tests/Engine/SpotReaderTests.cs`:

```csharp
using System.Drawing;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

public class SpotReaderTests
{
    private static readonly Rgb Grey = new(120, 120, 120);
    private static readonly Rgb Orange = new(240, 160, 40);
    private static readonly Rgb Sky = new(135, 206, 235);

    /// <summary>Paints the whole capture one colour, chosen by the region's X.</summary>
    private sealed class PaintedCapture : ICaptureSource
    {
        public Dictionary<int, Rgb> ByX { get; } = new();
        public List<RegionRect> Regions { get; } = new();
        public Bitmap Capture(RegionRect r)
        {
            Regions.Add(r);
            var c = ByX.TryGetValue(r.X, out var v) ? v : Grey;
            var bmp = new Bitmap(Math.Max(1, r.Width), Math.Max(1, r.Height));
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.FromArgb(c.R, c.G, c.B));
            return bmp;
        }
    }

    private sealed class Metrics : IWindowMetrics
    {
        public bool Gone;
        public IntPtr HwndForPid(int pid) => Gone ? IntPtr.Zero : new IntPtr(0x10);
        public (int X, int Y)? ClientOrigin(IntPtr h) => (0, 0);
        public (int W, int H)? ClientSize(IntPtr h) => (800, 599);
    }

    private static int X(int order) => 100 + order * 20;

    private static Trigger Spot(int order) => new()
    {
        Id = Guid.NewGuid(),
        Name = $"Mine #8: {MeasuredRing.RingOrder[order]}",
        Region = new RegionRect(X(order), 100, 9, 9),
        Mode = TriggerMode.Color,
        Color = new ColorCriteria(new Rgb(0, 0, 0), 25, ColorSamplingMode.SinglePixel,
            Point: new PickPoint(4, 4), Box: new SampleBox(), NoneOf: new[] { Sky }),
        Keybind = new KeyCombo("F13", Array.Empty<string>()),
        CoordSpace = Trigger.CoordSpaceClient,
        RecordedClientW = 800,
        RecordedClientH = 599,
        Ring = new RingSpot("mine8", order),
    };

    [Fact]
    public void Reads_each_spots_box_with_its_order_tolerance_and_ignore_colours()
    {
        var paint = new PaintedCapture();
        paint.ByX[X(5)] = Orange;
        var reader = new SpotReader(paint, new Metrics());

        var readings = reader.Read(7, Enumerable.Range(0, 8).Select(Spot).ToList())!;

        Assert.Equal(Enumerable.Range(0, 8), readings.Select(r => r.Order));
        Assert.Equal(Orange, readings[5].Sampled);
        Assert.Equal(Grey, readings[0].Sampled);
        Assert.All(readings, r => Assert.Equal(25, r.ToleranceRgb));
        Assert.All(readings, r => Assert.Equal(new[] { Sky }, r.Ignore));
    }

    [Fact]
    public void A_window_it_cannot_find_reads_nothing()
    {
        var reader = new SpotReader(new PaintedCapture(), new Metrics { Gone = true });

        Assert.Null(reader.Read(7, Enumerable.Range(0, 8).Select(Spot).ToList()));
    }

    [Fact]
    public void A_trigger_that_is_not_a_ring_spot_is_left_out()
    {
        var paint = new PaintedCapture();
        var notSpot = Spot(0);
        notSpot.Ring = null;

        var readings = new SpotReader(paint, new Metrics()).Read(7, new[] { notSpot, Spot(1) })!;

        Assert.Equal(new[] { 1 }, readings.Select(r => r.Order));
        Assert.Single(paint.Regions);
    }
}
```

Create `tests/RoRoRo.UrOcr.Tests/Engine/PulseOrderTests.cs`:

```csharp
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

public class PulseOrderTests
{
    private static readonly Rgb Grey = new(120, 120, 120);
    private static readonly Rgb Grey2 = new(100, 100, 100);
    private static readonly Rgb Orange = new(240, 160, 40);
    private static readonly Rgb Reddish = new(160, 120, 120);   // 40 from grey: ore, but near it
    private static readonly Rgb Sky = new(135, 206, 235);
    private static readonly IReadOnlyList<Rgb> Rock = new[] { Grey, Grey2 };

    private static IReadOnlyList<SpotReading> Ring(params (int Order, Rgb Colour)[] overrides)
    {
        var list = new List<SpotReading>();
        for (var o = 0; o < 8; o++)
        {
            var c = Grey;
            foreach (var (order, colour) in overrides)
                if (order == o) c = colour;
            list.Add(new SpotReading(o, c, 20, new[] { Sky }));
        }
        return list;
    }

    [Fact]
    public void All_rock_goes_in_ring_order() =>
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, PulseOrder.Rank(Ring(), Rock));

    [Fact]
    public void Ore_goes_first_furthest_from_the_rock_first() =>
        Assert.Equal(new[] { 6, 1, 0, 2, 3, 4, 5, 7 }, PulseOrder.Rank(Ring((1, Reddish), (6, Orange)), Rock));

    [Fact]
    public void An_ignore_colour_is_not_ore() =>
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, PulseOrder.Rank(Ring((3, Sky)), Rock));

    [Fact]
    public void Equal_ores_keep_ring_order() =>
        Assert.Equal(new[] { 2, 5, 0, 1, 3, 4, 6, 7 }, PulseOrder.Rank(Ring((5, Orange), (2, Orange)), Rock));

    [Fact]
    public void Within_tolerance_of_any_rock_colour_is_not_ore()
    {
        Assert.False(PulseOrder.IsOre(new SpotReading(0, new Rgb(105, 105, 105), 20, Array.Empty<Rgb>()), Rock));
        Assert.True(PulseOrder.IsOre(new SpotReading(0, Orange, 20, Array.Empty<Rgb>()), Rock));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~SpotReaderTests|FullyQualifiedName~PulseOrderTests"`
Expected: build FAILS with `CS0246: The type or namespace name 'SpotReader' could not be found`.

- [ ] **Step 3: Implement the reader and the order**

Create `Engine/SpotReader.cs`:

```csharp
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

/// <summary>One ring spot's calm sample, with that spot's tolerance and ignore colours.</summary>
public sealed record SpotReading(int Order, Rgb Sampled, int ToleranceRgb, IReadOnlyList<Rgb> Ignore);

public interface ISpotReader
{
    /// <summary>Samples each ring spot in <paramref name="pid"/>'s window. Null when the window
    /// cannot be resolved (hidden, closed, mid-resize).</summary>
    IReadOnlyList<SpotReading>? Read(int pid, IReadOnlyList<Trigger> spots);
}

/// <summary>
/// Reads ring spots the way the trigger coordinator does: the same region resolution and the same
/// averaged box around the pick point, so the pulse and the 0.5.0 triggers see the same pixels.
/// </summary>
public sealed class SpotReader(ICaptureSource capture, IWindowMetrics metrics) : ISpotReader
{
    public IReadOnlyList<SpotReading>? Read(int pid, IReadOnlyList<Trigger> spots)
    {
        var result = new List<SpotReading>(spots.Count);
        foreach (var t in spots)
        {
            if (t.Ring is not { } spot || t.Color is not { } colour) continue;
            var region = TriggerRegionResolver.Resolve(t, t.IsClientSpace ? pid : 0, metrics);
            if (region is null || region.Width < 1 || region.Height < 1) return null;
            using var bmp = capture.Capture(region);
            result.Add(new SpotReading(spot.Order, ColorMatcher.Sample(bmp, colour, t.Region),
                colour.ToleranceRgb, colour.NoneOf ?? Array.Empty<Rgb>()));
        }
        return result;
    }
}
```

Create `Engine/PulseOrder.cs`:

```csharp
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

/// <summary>
/// The order a clearing pass tries the spots in (spec: "ore first ... then stone"). Ore is a calm
/// sample more than its tolerance from every rock colour of the layer and every ignore colour of
/// the spot. Ore goes first, furthest from the rock set first; then the rest in ring order.
/// </summary>
public static class PulseOrder
{
    public static IReadOnlyList<int> Rank(IReadOnlyList<SpotReading> spots, IReadOnlyList<Rgb> rock)
    {
        var scored = spots
            .Select(s => (s.Order, FromRock: Nearest(s.Sampled, rock), Ore: IsOre(s, rock)))
            .ToList();
        var ore = scored.Where(x => x.Ore).OrderByDescending(x => x.FromRock).ThenBy(x => x.Order).Select(x => x.Order);
        var rest = scored.Where(x => !x.Ore).OrderBy(x => x.Order).Select(x => x.Order);
        return ore.Concat(rest).ToList();
    }

    public static bool IsOre(SpotReading s, IReadOnlyList<Rgb> rock) =>
        Nearest(s.Sampled, rock) > s.ToleranceRgb && Nearest(s.Sampled, s.Ignore) > s.ToleranceRgb;

    /// <summary>Distance to the nearest colour of the set; +Infinity for an empty set.</summary>
    public static double Nearest(Rgb c, IReadOnlyList<Rgb> set)
    {
        var best = double.PositiveInfinity;
        foreach (var s in set)
        {
            var d = ColorMatcher.Distance(c, s);
            if (d < best) best = d;
        }
        return best;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~SpotReaderTests|FullyQualifiedName~PulseOrderTests"`
Expected: PASS (3 + 5).

Full suite: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj`
Expected: **270 passed**.

- [ ] **Step 5: Commit**

```powershell
git add Engine/SpotReader.cs Engine/PulseOrder.cs tests/RoRoRo.UrOcr.Tests/Engine/SpotReaderTests.cs tests/RoRoRo.UrOcr.Tests/Engine/PulseOrderTests.cs
git commit -m "feat(engine): read the ring at rest and rank the clears ore first" -m "Same region resolution and sample box as the ring triggers. Ore is a spot near none of the layer's rock or the spot's ignore colours; furthest from rock goes first, then ring order." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: MacroCall, one macro followed to its end

**Files:**
- Create: `Engine/MacroCall.cs`
- Create: `tests/RoRoRo.UrOcr.Tests/Engine/PulseFakes.cs`
- Test: `tests/RoRoRo.UrOcr.Tests/Engine/MacroCallTests.cs`

**Interfaces:**
- Consumes (Task 1): `IMacroRunClient.RunAsync`, `IMacroRunClient.GetPlaybackAsync`, `RunMacroResponse`, `GetPlaybackResponse`, `BridgeReasons.*`, `PlaybackStates.*`; existing `IClock`.
- Produces:
  - `public enum CallStatus { Waiting, Done, Skipped, CheckFailed, Lost, Stop }`
  - `public sealed record CallResult(CallStatus Status, string Label, string? Detail = null);`
  - `public sealed class MacroCall(IMacroRunClient client, string target, IClock clock, Action<string> log)` with `const int RetryMs = 1000`, `const int MaxInterruptions = 5`, `const int InterAltDelayMs = 0`, `bool Active`, `string Label`, `void Begin(string macroId, string label)`, `Task<CallResult> StepAsync(bool foreground, CancellationToken ct, Func<bool>? inFront = null)`
  - Test helpers in `PulseFakes.cs` (namespace `RoRoRo.UrOcr.Tests.Engine`, internal): `PulseClock` (`Now`, `Advance(int ms)`), `ScriptedMacros : IMacroRunClient` (`Runs` with `MacroId`, `Targets`, `Delay`; `RunIds`, `Polls`, `RunReplies`, `Script(macroId, params GetPlaybackResponse[])`, static `Finished`, `Running`, `Skipped`, `CheckFailed`, `Aborted`, `RefusedFocus`, `Stopped`, `Unknown`, `Gone`, `Refusal(reason)`).

`StepAsync` contract: with no playback yet, it starts the macro (only when `foreground`, past any retry time, and `inFront()` still true when given, read right before `RunMacro`) with `interAltDelayMs: 0`, and returns `Waiting`; with a playback, it polls and returns `Waiting` while it runs, else the ending. Any status other than `Waiting` ends the call (`Active` becomes false).

- [ ] **Step 1: Write the shared fakes**

Create `tests/RoRoRo.UrOcr.Tests/Engine/PulseFakes.cs`:

```csharp
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Ipc;

namespace RoRoRo.UrOcr.Tests.Engine;

internal sealed class PulseClock : IClock
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
    public void Advance(int ms) => Now = Now.AddMilliseconds(ms);
}

/// <summary>
/// A fake Ur Task. RunAsync hands out the queued RunReplies first, then accepts with ids pb1, pb2,
/// ... GetPlayback answers from the script of the playback's macro: replies in order, the last one
/// repeating for every later poll (and every later playback of that macro). Unscripted: finished.
/// </summary>
internal sealed class ScriptedMacros : IMacroRunClient
{
    public static readonly GetPlaybackResponse Finished = new(true, "finished", null, null, null);
    public static readonly GetPlaybackResponse Running = new(true, "running", null, null, null);
    public static readonly GetPlaybackResponse Skipped = new(true, "finished", "skipped", "No outline under the pointer.", null);
    public static readonly GetPlaybackResponse CheckFailed = new(true, "failed", "check-failed", "step 1 'Auto Mine off' found no candidate that matched.", 1);
    public static readonly GetPlaybackResponse Aborted = new(true, "failed", "aborted", "Foreground shifted away at step 1/1.", null);
    public static readonly GetPlaybackResponse RefusedFocus = new(true, "failed", "refused", "Couldn't focus the account.", null);
    public static readonly GetPlaybackResponse Stopped = new(true, "stopped", null, null, null);
    public static readonly GetPlaybackResponse Unknown = new(false, null, "unknown-playback", "No playback with id 'pb1'. Finished playbacks are kept for 10 minutes.", null);
    public static readonly GetPlaybackResponse Gone = new(false, null, "ur-task-not-running", "Ur Task is not running or refused the connection.", null);

    public static RunMacroResponse Refusal(string reason) => new(false, null, false, reason, $"{reason} detail");

    public List<(string MacroId, IReadOnlyList<string>? Targets, int? Delay)> Runs { get; } = new();
    public List<string> RunIds => Runs.Select(r => r.MacroId).ToList();
    public List<string> Polls { get; } = new();
    public Queue<RunMacroResponse> RunReplies { get; } = new();

    private readonly Dictionary<string, Queue<GetPlaybackResponse>> _scripts = new();
    private readonly Dictionary<string, string> _macroOf = new();
    private int _next;

    public void Script(string macroId, params GetPlaybackResponse[] replies) =>
        _scripts[macroId] = new Queue<GetPlaybackResponse>(replies);

    public Task<RunMacroResponse> RunAsync(string macroId, IReadOnlyList<string>? targets, CancellationToken ct) =>
        RunAsync(macroId, targets, null, ct);

    public Task<RunMacroResponse> RunAsync(string macroId, IReadOnlyList<string>? targets, int? interAltDelayMs, CancellationToken ct)
    {
        Runs.Add((macroId, targets, interAltDelayMs));
        if (RunReplies.Count > 0) return Task.FromResult(RunReplies.Dequeue());
        var id = $"pb{++_next}";
        _macroOf[id] = macroId;
        return Task.FromResult(new RunMacroResponse(true, id, false, null, null));
    }

    public Task<GetPlaybackResponse> GetPlaybackAsync(string playbackId, CancellationToken ct)
    {
        Polls.Add(playbackId);
        var macro = _macroOf.GetValueOrDefault(playbackId, "");
        if (_scripts.TryGetValue(macro, out var q) && q.Count > 0)
            return Task.FromResult(q.Count > 1 ? q.Dequeue() : q.Peek());
        return Task.FromResult(Finished);
    }
}
```

- [ ] **Step 2: Write the failing tests**

Create `tests/RoRoRo.UrOcr.Tests/Engine/MacroCallTests.cs`:

```csharp
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Ipc;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

public class MacroCallTests
{
    private sealed record Rig(MacroCall Call, ScriptedMacros Macros, PulseClock Clock, List<string> Log);

    private static Rig Build()
    {
        var macros = new ScriptedMacros();
        var clock = new PulseClock();
        var log = new List<string>();
        var call = new MacroCall(macros, "42", clock, log.Add);
        call.Begin("id-on", "Auto Mine on (checked)");
        return new Rig(call, macros, clock, log);
    }

    private static Task<CallResult> Step(Rig r, bool front = true) => r.Call.StepAsync(front, CancellationToken.None);

    [Fact]
    public async Task Starts_on_the_account_then_reports_the_end()
    {
        var rig = Build();

        Assert.Equal(CallStatus.Waiting, (await Step(rig)).Status);
        var run = Assert.Single(rig.Macros.Runs);
        Assert.Equal("id-on", run.MacroId);
        Assert.Equal(new[] { "42" }, run.Targets);
        Assert.Equal(0, run.Delay);

        var end = await Step(rig);
        Assert.Equal(CallStatus.Done, end.Status);
        Assert.Equal("Auto Mine on (checked)", end.Label);
        Assert.False(rig.Call.Active);
    }

    [Fact]
    public async Task Does_not_start_while_the_account_is_not_in_front()
    {
        var rig = Build();

        Assert.Equal(CallStatus.Waiting, (await Step(rig, front: false)).Status);
        Assert.Empty(rig.Macros.Runs);
        Assert.True(rig.Call.Active);
    }

    [Fact]
    public async Task Does_not_start_when_the_account_left_the_front_during_the_tick()
    {
        var rig = Build();

        Assert.Equal(CallStatus.Waiting, (await rig.Call.StepAsync(true, CancellationToken.None, () => false)).Status);
        Assert.Empty(rig.Macros.Runs);
        Assert.True(rig.Call.Active);

        Assert.Equal(CallStatus.Waiting, (await rig.Call.StepAsync(true, CancellationToken.None, () => true)).Status);
        Assert.Single(rig.Macros.Runs);
    }

    [Fact]
    public async Task Follows_a_started_playback_in_the_background()
    {
        var rig = Build();
        await Step(rig);

        var end = await Step(rig, front: false);

        Assert.Equal(CallStatus.Done, end.Status);
        Assert.Single(rig.Macros.Polls);
    }

    [Fact]
    public async Task Running_keeps_waiting()
    {
        var rig = Build();
        rig.Macros.Script("id-on", ScriptedMacros.Running, ScriptedMacros.Running, ScriptedMacros.Finished);
        await Step(rig);

        Assert.Equal(CallStatus.Waiting, (await Step(rig)).Status);
        Assert.Equal(CallStatus.Waiting, (await Step(rig)).Status);
        Assert.Equal(CallStatus.Done, (await Step(rig)).Status);
        Assert.Single(rig.Macros.Runs);
    }

    [Fact]
    public async Task Busy_waits_a_second_then_tries_again()
    {
        var rig = Build();
        rig.Macros.RunReplies.Enqueue(ScriptedMacros.Refusal("busy"));

        Assert.Equal(CallStatus.Waiting, (await Step(rig)).Status);
        Assert.Equal(CallStatus.Waiting, (await Step(rig)).Status);
        Assert.Single(rig.Macros.Runs);

        rig.Clock.Advance(MacroCall.RetryMs);
        Assert.Equal(CallStatus.Waiting, (await Step(rig)).Status);
        Assert.Equal(2, rig.Macros.Runs.Count);
        Assert.Equal(CallStatus.Done, (await Step(rig)).Status);
        Assert.Single(rig.Log, l => l.Contains("busy"));
    }

    [Theory]
    [InlineData("ack-timeout")]
    [InlineData("no-targets-resolved")]
    public async Task Other_passing_refusals_also_retry(string reason)
    {
        var rig = Build();
        rig.Macros.RunReplies.Enqueue(ScriptedMacros.Refusal(reason));

        await Step(rig);
        rig.Clock.Advance(MacroCall.RetryMs);
        await Step(rig);

        Assert.Equal(2, rig.Macros.Runs.Count);
        Assert.True(rig.Call.Active);
    }

    [Fact]
    public async Task Missing_Ur_Task_stops()
    {
        var rig = Build();
        rig.Macros.RunReplies.Enqueue(ScriptedMacros.Refusal("ur-task-not-running"));

        var end = await Step(rig);

        Assert.Equal(CallStatus.Stop, end.Status);
        Assert.Contains("Ur Task is not running", end.Detail);
        Assert.False(rig.Call.Active);
    }

    [Fact]
    public async Task An_unknown_macro_stops()
    {
        var rig = Build();
        rig.Macros.RunReplies.Enqueue(ScriptedMacros.Refusal("unknown-macro"));

        var end = await Step(rig);

        Assert.Equal(CallStatus.Stop, end.Status);
        Assert.Contains("unknown-macro", end.Detail);
    }

    [Fact]
    public async Task A_skip_is_reported()
    {
        var rig = Build();
        rig.Macros.Script("id-on", ScriptedMacros.Skipped);
        await Step(rig);

        var end = await Step(rig);

        Assert.Equal(CallStatus.Skipped, end.Status);
        Assert.Equal(ScriptedMacros.Skipped.Detail, end.Detail);
    }

    [Fact]
    public async Task A_failed_check_is_reported()
    {
        var rig = Build();
        rig.Macros.Script("id-on", ScriptedMacros.CheckFailed);
        await Step(rig);

        var end = await Step(rig);

        Assert.Equal(CallStatus.CheckFailed, end.Status);
        Assert.Equal(ScriptedMacros.CheckFailed.Detail, end.Detail);
    }

    [Fact]
    public async Task A_lost_playback_is_reported()
    {
        var rig = Build();
        rig.Macros.Script("id-on", ScriptedMacros.Unknown);
        await Step(rig);

        var end = await Step(rig);

        Assert.Equal(CallStatus.Lost, end.Status);
        Assert.Contains("no longer knows", end.Detail);
    }

    [Fact]
    public async Task Stopped_in_Ur_Task_stops()
    {
        var rig = Build();
        rig.Macros.Script("id-on", ScriptedMacros.Stopped);
        await Step(rig);

        var end = await Step(rig);

        Assert.Equal(CallStatus.Stop, end.Status);
        Assert.Contains("stopped in Ur Task", end.Detail);
    }

    [Fact]
    public async Task Aborted_after_the_account_left_the_front_runs_again()
    {
        var rig = Build();
        rig.Macros.Script("id-on", ScriptedMacros.Aborted, ScriptedMacros.Finished);
        await Step(rig);

        Assert.Equal(CallStatus.Waiting, (await Step(rig, front: false)).Status);
        Assert.Single(rig.Macros.Runs);

        Assert.Equal(CallStatus.Waiting, (await Step(rig)).Status);
        Assert.Equal(2, rig.Macros.Runs.Count);
        Assert.Equal(CallStatus.Done, (await Step(rig)).Status);
        Assert.Contains(rig.Log, l => l.Contains("interrupted"));
    }

    [Fact]
    public async Task Aborted_while_in_front_stops()
    {
        var rig = Build();
        rig.Macros.Script("id-on", ScriptedMacros.Aborted);
        await Step(rig);

        var end = await Step(rig);

        Assert.Equal(CallStatus.Stop, end.Status);
        Assert.Contains("Esc", end.Detail);
        Assert.Single(rig.Macros.Runs);
    }

    [Fact]
    public async Task Interrupted_too_often_stops()
    {
        var rig = Build();
        rig.Macros.Script("id-on", ScriptedMacros.RefusedFocus);

        CallResult end = new(CallStatus.Waiting, "");
        for (var i = 0; i <= MacroCall.MaxInterruptions; i++)
        {
            await Step(rig);
            end = await Step(rig);
        }

        Assert.Equal(CallStatus.Stop, end.Status);
        Assert.Contains("interrupted 5 times", end.Detail);
        Assert.Equal(MacroCall.MaxInterruptions + 1, rig.Macros.Runs.Count);
    }

    [Fact]
    public async Task Ur_Task_gone_while_waiting_stops()
    {
        var rig = Build();
        rig.Macros.Script("id-on", ScriptedMacros.Gone);
        await Step(rig);

        Assert.Equal(CallStatus.Stop, (await Step(rig)).Status);
    }

    [Fact]
    public async Task An_Ur_Task_without_GetPlayback_stops()
    {
        var rig = Build();
        rig.Macros.Script("id-on", new GetPlaybackResponse(false, null, "refused", "Unknown method 'GetPlayback'.", null));
        await Step(rig);

        var end = await Step(rig);

        Assert.Equal(CallStatus.Stop, end.Status);
        Assert.Contains("Unknown method 'GetPlayback'", end.Detail);
    }

    [Fact]
    public async Task A_poll_that_timed_out_polls_again()
    {
        var rig = Build();
        rig.Macros.Script("id-on", new GetPlaybackResponse(false, null, "ack-timeout", "slow", null), ScriptedMacros.Finished);
        await Step(rig);

        Assert.Equal(CallStatus.Waiting, (await Step(rig)).Status);
        Assert.Equal(CallStatus.Done, (await Step(rig)).Status);
        Assert.Single(rig.Macros.Runs);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~MacroCallTests"`
Expected: build FAILS with `CS0246: The type or namespace name 'MacroCall' could not be found`.

- [ ] **Step 4: Implement MacroCall**

Create `Engine/MacroCall.cs`:

```csharp
using RoRoRo.UrOcr.Ipc;

namespace RoRoRo.UrOcr.Engine;

public enum CallStatus
{
    /// <summary>Not started yet (account behind, busy retry pending) or still running.</summary>
    Waiting,
    /// <summary>Finished normally. For a Clear spot: it held and let go, so a block was cleared.</summary>
    Done,
    /// <summary>Finished having pressed nothing (reason "skipped": no outline).</summary>
    Skipped,
    /// <summary>A colour check failed and the playback stopped (reason "check-failed").</summary>
    CheckFailed,
    /// <summary>Ur Task no longer knows the playback (restarted, or kept past 10 minutes).</summary>
    Lost,
    /// <summary>The loop must stop; Detail says why.</summary>
    Stop,
}

public sealed record CallResult(CallStatus Status, string Label, string? Detail = null);

/// <summary>
/// Runs one Ur Task macro for one account and follows it to its end, one tick at a time.
/// Starting needs the account in front, read again right before RunMacro when the caller gives
/// <c>inFront</c> (Ur Task focuses the target on every run); polling does not. busy, ack-timeout and
/// no-targets-resolved retry every <see cref="RetryMs"/>. A playback that ends aborted or refused
/// after the account was seen behind was a focus change and runs again (at most
/// <see cref="MaxInterruptions"/> times in a row); aborted while the account stayed in front is
/// taken as Esc and stops. Every other refusal stops, with the reason in Detail.
/// </summary>
public sealed class MacroCall(IMacroRunClient client, string target, IClock clock, Action<string> log)
{
    public const int RetryMs = 1000;
    public const int MaxInterruptions = 5;
    /// <summary>Ur Task's focus-to-play wait: none, the account is already in front.</summary>
    public const int InterAltDelayMs = 0;

    private string? _macroId;
    private string _label = "";
    private string? _playbackId;
    private DateTimeOffset _retryAt = DateTimeOffset.MinValue;
    private string? _loggedRefusal;
    private int _interruptions;
    private bool _sawBehind;

    public bool Active => _macroId is not null;
    public string Label => _label;

    public void Begin(string macroId, string label)
    {
        _macroId = macroId;
        _label = label;
        _playbackId = null;
        _retryAt = DateTimeOffset.MinValue;
        _loggedRefusal = null;
        _interruptions = 0;
        _sawBehind = false;
    }

    /// <param name="inFront">A fresh read of "is this account in front", called right before
    /// RunMacro. The foreground flag is read once per tick; this closes most of the gap.</param>
    public async Task<CallResult> StepAsync(bool foreground, CancellationToken ct, Func<bool>? inFront = null)
    {
        if (_macroId is null) throw new InvalidOperationException("No macro to step: call Begin first.");
        return _playbackId is null
            ? await StartAsync(foreground, inFront, ct).ConfigureAwait(false)
            : await PollAsync(foreground, ct).ConfigureAwait(false);
    }

    private async Task<CallResult> StartAsync(bool foreground, Func<bool>? inFront, CancellationToken ct)
    {
        var now = clock.Now;
        if (!foreground || now < _retryAt) return Waiting;
        // Ur Task focuses the target on every run: never ask while someone else is in front.
        if (inFront is not null && !inFront()) return Waiting;

        var resp = await client.RunAsync(_macroId!, new[] { target }, InterAltDelayMs, ct).ConfigureAwait(false);
        if (resp.Ok && !string.IsNullOrEmpty(resp.PlaybackId))
        {
            _playbackId = resp.PlaybackId;
            _loggedRefusal = null;
            _sawBehind = false;
            return Waiting;
        }
        switch (resp.Reason)
        {
            case BridgeReasons.Busy:
            case BridgeReasons.AckTimeout:
            case BridgeReasons.NoTargets:
                _retryAt = now.AddMilliseconds(RetryMs);
                if (_loggedRefusal != resp.Reason)
                {
                    log($"'{_label}': Ur Task said {resp.Reason}, trying again every {RetryMs / 1000} s");
                    _loggedRefusal = resp.Reason;
                }
                return Waiting;
            case BridgeReasons.NotRunning:
                return End(CallStatus.Stop, $"Ur Task is not running, so '{_label}' could not start.");
            default:
                return End(CallStatus.Stop, resp.Ok
                    ? $"Ur Task accepted '{_label}' but gave no playback id."
                    : $"Ur Task refused '{_label}': {resp.Reason}. {resp.Detail}".TrimEnd());
        }
    }

    private async Task<CallResult> PollAsync(bool foreground, CancellationToken ct)
    {
        if (!foreground) _sawBehind = true;
        var r = await client.GetPlaybackAsync(_playbackId!, ct).ConfigureAwait(false);
        if (!r.Ok)
        {
            return r.Reason switch
            {
                BridgeReasons.AckTimeout => Waiting,
                BridgeReasons.UnknownPlayback => End(CallStatus.Lost,
                    $"Ur Task no longer knows playback {_playbackId} of '{_label}' (restarted, or kept past 10 minutes)."),
                BridgeReasons.NotRunning => End(CallStatus.Stop, $"Ur Task stopped answering while '{_label}' ran."),
                _ => End(CallStatus.Stop, $"Ur Task would not say how '{_label}' went: {r.Reason}. {r.Detail}".TrimEnd()),
            };
        }

        switch (r.State)
        {
            case PlaybackStates.Running:
                return Waiting;
            case PlaybackStates.Finished:
                return string.Equals(r.Reason, BridgeReasons.Skipped, StringComparison.OrdinalIgnoreCase)
                    ? End(CallStatus.Skipped, r.Detail)
                    : End(CallStatus.Done, r.Detail);
            case PlaybackStates.Stopped:
                return End(CallStatus.Stop, $"'{_label}' was stopped in Ur Task (Esc or StopMacro), so the pulse loop stops too.");
            case PlaybackStates.Failed when r.Reason == BridgeReasons.CheckFailed:
                return End(CallStatus.CheckFailed, r.Detail);
            case PlaybackStates.Failed when r.Reason == BridgeReasons.Aborted && !_sawBehind:
                return End(CallStatus.Stop,
                    $"'{_label}' ended while the account was in front ({r.Detail}); taken as Esc, so the pulse loop stops.");
            case PlaybackStates.Failed when r.Reason is BridgeReasons.Aborted or BridgeReasons.Refused:
                if (++_interruptions > MaxInterruptions)
                    return End(CallStatus.Stop, $"'{_label}' was interrupted {MaxInterruptions} times in a row: {r.Detail}");
                log($"'{_label}' was interrupted ({r.Detail}); it runs again when the account is in front");
                _playbackId = null;
                return Waiting;
            default:
                return End(CallStatus.Stop, $"'{_label}' failed in Ur Task: {r.Reason}. {r.Detail}".TrimEnd());
        }
    }

    private CallResult Waiting => new(CallStatus.Waiting, _label);

    private CallResult End(CallStatus status, string? detail)
    {
        var label = _label;
        _macroId = null;
        _playbackId = null;
        return new CallResult(status, label, detail);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~MacroCallTests"`
Expected: PASS (20 counting theory rows).

Full suite: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj`
Expected: **290 passed**.

- [ ] **Step 6: Commit**

```powershell
git add Engine/MacroCall.cs tests/RoRoRo.UrOcr.Tests/Engine/PulseFakes.cs tests/RoRoRo.UrOcr.Tests/Engine/MacroCallTests.cs
git commit -m "feat(engine): follow one Ur Task macro to its end" -m "Starts only with the account in front (read again right before RunMacro, no inter-alt delay), polls GetPlayback, retries busy every second, reruns a macro a focus change interrupted, and stops on Esc, StopMacro, a missing Ur Task or any other refusal." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: The pulse loop

**Files:**
- Create: `Engine/PulseLoop.cs`
- Create: `tests/RoRoRo.UrOcr.Tests/Engine/PulseLoopFixtures.cs`
- Test: `tests/RoRoRo.UrOcr.Tests/Engine/PulseLoopTests.cs`

**Interfaces:**
- Consumes: `PulseConfig`, `PulseMacros`, `PulseMode`, `PulseValidation.Validate/SpotsOf` (Task 2); `ISpotReader`, `SpotReading`, `PulseOrder.Rank` (Task 3); `MacroCall`, `CallStatus`, `CallResult` (Task 4); existing `RingTracker.Vote(RingDefinition, IReadOnlyList<SpotSample>)`, `RingTracker.Pick(RingDefinition, IReadOnlyDictionary<string,int>, string?)`, `SpotSample(Order, Sampled, ToleranceRgb)`, `TriggerValidation.Find`, `MeasuredRing.RingOrder`, `IClock`. Tests: `PulseClock`, `ScriptedMacros` from `tests/.../Engine/PulseFakes.cs` (Task 4).
- Produces:
  - `public enum PulseState { Riding, Pausing, Reading, Clearing, Bursting, GoingToTop, Stopped }`
  - `public sealed class PulseLoop(PulseConfig config, IReadOnlyList<RingDefinition> rings, IReadOnlyList<Trigger> triggers, ISpotReader reader, IMacroRunClient macros, IClock clock, Action<string> log)` with `long AccountUserId`, `PulseState State`, `string? StopReason`, `string? Layer`, `Task TickAsync(bool foreground, int pid, CancellationToken ct, Func<bool>? inFront = null)`, `void NoteBehind()`, `const int MaxStepsPerTick = 12`
  - Test helpers in `PulseLoopFixtures.cs` (internal): `PulseFixtures` (`Navy`, `Black`, `Grey`, `Orange`, `Sky`, `Ring()`, `Spot(int)`, `Spots()`, `Macros()`, `Config(int target = 3, PulseMode mode = PulseMode.Top, long account = 42)`), `ScriptedReader : ISpotReader` (`Next`, `Reads`, static `All(Rgb, params (int Order, Rgb Colour)[])`).

The macro ids in the fixtures are `id-off`, `id-on`, `id-top` and `id-clear-N` ... `id-clear-NW`.

- [ ] **Step 1: Write the fixtures**

Create `tests/RoRoRo.UrOcr.Tests/Engine/PulseLoopFixtures.cs`:

```csharp
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Tests.Engine;

internal static class PulseFixtures
{
    public static readonly Rgb Navy = new(30, 30, 90);
    public static readonly Rgb Black = new(10, 10, 12);
    public static readonly Rgb Grey = new(120, 120, 120);
    public static readonly Rgb Orange = new(240, 160, 40);
    public static readonly Rgb Sky = new(135, 206, 235);

    /// <summary>Three layers, top first: navy (1), black (2), grey (3).</summary>
    public static RingDefinition Ring() => new("mine8", "Mine #8", new[]
    {
        new LayerDefinition("navy", new[] { Navy }),
        new LayerDefinition("black", new[] { Black }),
        new LayerDefinition("grey", new[] { Grey }),
    }, MinLayerSpots: 3);

    public static Trigger Spot(int order) => new()
    {
        Id = Guid.NewGuid(),
        Name = $"Mine #8: {MeasuredRing.RingOrder[order]}",
        Region = new RegionRect(100 + order * 20, 100, 9, 9),
        Mode = TriggerMode.Color,
        Color = new ColorCriteria(new Rgb(0, 0, 0), 20, ColorSamplingMode.SinglePixel,
            Point: new PickPoint(4, 4), Box: new SampleBox(), NoneOf: new[] { Sky }),
        Keybind = new KeyCombo("F13", Array.Empty<string>()),
        AccountAware = true,
        Action = TriggerAction.RunMacro,
        MacroId = $"mine-{order}",
        Ring = new RingSpot("mine8", order),
    };

    public static IReadOnlyList<Trigger> Spots() => Enumerable.Range(0, 8).Select(Spot).ToList();

    public static PulseMacros Macros() => new("id-off", "id-on", "id-top",
        MeasuredRing.RingOrder.Select(n => $"id-clear-{n}").ToList());

    public static PulseConfig Config(int target = 3, PulseMode mode = PulseMode.Top, long account = 42) =>
        new(account, "mine8", target, mode, Macros: Macros());
}

internal sealed class ScriptedReader : ISpotReader
{
    public IReadOnlyList<SpotReading>? Next { get; set; }
    public int Reads { get; private set; }

    public IReadOnlyList<SpotReading>? Read(int pid, IReadOnlyList<Trigger> spots)
    {
        Reads++;
        return Next;
    }

    /// <summary>Eight spots of one colour, with per-spot overrides; tolerance 20, sky ignored.</summary>
    public static IReadOnlyList<SpotReading> All(Rgb colour, params (int Order, Rgb Colour)[] overrides)
    {
        var list = new List<SpotReading>();
        for (var o = 0; o < 8; o++)
        {
            var c = colour;
            foreach (var (order, col) in overrides)
                if (order == o) c = col;
            list.Add(new SpotReading(o, c, 20, new[] { PulseFixtures.Sky }));
        }
        return list;
    }
}
```

- [ ] **Step 2: Write the failing tests**

Create `tests/RoRoRo.UrOcr.Tests/Engine/PulseLoopTests.cs`:

```csharp
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

/// <summary>
/// The pulse state machine against a scripted Ur Task, a scripted ring and a fake clock. Every
/// unscripted macro finishes on its first poll, so one tick starts a macro and the next sees it end.
/// </summary>
public class PulseLoopTests
{
    private sealed record Rig(PulseLoop Loop, ScriptedMacros Macros, ScriptedReader Reader, PulseClock Clock, List<string> Log);

    private static Rig Build(PulseConfig? config = null, Rgb? everywhere = null)
    {
        var macros = new ScriptedMacros();
        var reader = new ScriptedReader { Next = ScriptedReader.All(everywhere ?? PulseFixtures.Grey) };
        var clock = new PulseClock();
        var log = new List<string>();
        var loop = new PulseLoop(config ?? PulseFixtures.Config(), new[] { PulseFixtures.Ring() },
            PulseFixtures.Spots(), reader, macros, clock, log.Add);
        return new Rig(loop, macros, reader, clock, log);
    }

    private static Task Tick(Rig r, bool front = true) => r.Loop.TickAsync(front, 7, CancellationToken.None);

    /// <summary>Auto Mine on, ride 2 s, Auto Mine off, settle 1 s, read: the first calm read.</summary>
    private static async Task FirstRead(Rig r)
    {
        await Tick(r);            // start Auto Mine on
        await Tick(r);            // it finished: ride timer starts
        r.Clock.Advance(2000);
        await Tick(r);            // ride over: start Auto Mine off
        await Tick(r);            // it finished: settle timer starts
        r.Clock.Advance(1000);
        await Tick(r);            // settled: read, then act on the read
    }

    private static void SkipAllClears(Rig r)
    {
        foreach (var n in MeasuredRing.RingOrder) r.Macros.Script($"id-clear-{n}", ScriptedMacros.Skipped);
    }

    private static async Task Ticks(Rig r, int n)
    {
        for (var i = 0; i < n; i++) await Tick(r);
    }

    [Fact]
    public async Task Starts_by_turning_Auto_Mine_on_for_its_account()
    {
        var rig = Build();

        await Tick(rig);

        var run = Assert.Single(rig.Macros.Runs);
        Assert.Equal("id-on", run.MacroId);
        Assert.Equal(new[] { "42" }, run.Targets);
        Assert.Equal(PulseState.Riding, rig.Loop.State);
        Assert.Contains(rig.Log, l => l.StartsWith("started"));
    }

    [Fact]
    public async Task Rides_burstMs_then_pauses_and_settles_before_reading()
    {
        var rig = Build();
        await Tick(rig);
        await Tick(rig);

        rig.Clock.Advance(1999);
        await Tick(rig);
        Assert.Equal(new[] { "id-on" }, rig.Macros.RunIds);

        rig.Clock.Advance(1);
        await Tick(rig);
        Assert.Equal(new[] { "id-on", "id-off" }, rig.Macros.RunIds);
        Assert.Equal(PulseState.Pausing, rig.Loop.State);

        await Tick(rig);
        rig.Clock.Advance(999);
        await Tick(rig);
        Assert.Equal(0, rig.Reader.Reads);

        rig.Clock.Advance(1);
        await Tick(rig);
        Assert.Equal(1, rig.Reader.Reads);
    }

    [Fact]
    public async Task Above_the_target_rides_on()
    {
        var rig = Build(everywhere: PulseFixtures.Navy);

        await FirstRead(rig);

        Assert.Equal(PulseState.Riding, rig.Loop.State);
        Assert.Equal(new[] { "id-on", "id-off", "id-on" }, rig.Macros.RunIds);
        Assert.Equal("navy", rig.Loop.Layer);
        Assert.Contains(rig.Log, l => l.Contains("above the target"));
    }

    [Fact]
    public async Task At_the_target_clears_ore_first_then_the_rest_in_ring_order()
    {
        var rig = Build();
        rig.Reader.Next = ScriptedReader.All(PulseFixtures.Grey, (5, PulseFixtures.Orange), (2, PulseFixtures.Sky));

        await FirstRead(rig);
        Assert.Equal(PulseState.Clearing, rig.Loop.State);
        await Ticks(rig, 7);

        Assert.Equal(
            new[] { "id-clear-SW", "id-clear-N", "id-clear-NE", "id-clear-E", "id-clear-SE", "id-clear-S", "id-clear-W", "id-clear-NW" },
            rig.Macros.RunIds.Skip(2));
    }

    [Fact]
    public async Task Below_the_target_goes_to_top_then_rides()
    {
        var rig = Build(PulseFixtures.Config(target: 1));

        await FirstRead(rig);
        Assert.Equal(PulseState.GoingToTop, rig.Loop.State);
        Assert.Equal("id-top", rig.Macros.RunIds.Last());
        Assert.Contains(rig.Log, l => l.Contains("past the target"));

        await Tick(rig);
        Assert.Equal(PulseState.Riding, rig.Loop.State);
        Assert.Equal(new[] { "id-on", "id-off", "id-top", "id-on" }, rig.Macros.RunIds);
    }

    [Fact]
    public async Task One_above_clears_on_the_layer_above_the_target()
    {
        var rig = Build(PulseFixtures.Config(target: 3, mode: PulseMode.OneAbove), PulseFixtures.Black);

        await FirstRead(rig);

        Assert.Equal(PulseState.Clearing, rig.Loop.State);
        Assert.Equal("id-clear-N", rig.Macros.RunIds.Last());
    }

    [Fact]
    public async Task A_pass_that_clears_nothing_rides_a_burst()
    {
        var rig = Build();
        SkipAllClears(rig);

        await FirstRead(rig);
        await Ticks(rig, 8);

        Assert.Equal(PulseState.Bursting, rig.Loop.State);
        Assert.Equal("id-on", rig.Macros.RunIds.Last());
        Assert.Contains(rig.Log, l => l.Contains("nothing in reach to clear"));
    }

    [Fact]
    public async Task A_pass_that_clears_something_reads_again_without_riding()
    {
        var rig = Build();

        await FirstRead(rig);
        await Ticks(rig, 8);
        Assert.Equal(PulseState.Pausing, rig.Loop.State);
        Assert.Equal(10, rig.Macros.Runs.Count);

        rig.Clock.Advance(999);
        await Tick(rig);
        Assert.Equal(1, rig.Reader.Reads);

        rig.Clock.Advance(1);
        await Tick(rig);
        Assert.Equal(2, rig.Reader.Reads);
        Assert.Equal("id-clear-N", rig.Macros.RunIds.Last());
        Assert.Single(rig.Macros.RunIds, id => id == "id-off");
        Assert.Contains(rig.Log, l => l.StartsWith("cleared N NE E SE S SW W NW"));
    }

    [Fact]
    public async Task A_failed_check_on_a_clear_stops_the_loop()
    {
        var rig = Build();
        rig.Macros.Script("id-clear-N", ScriptedMacros.CheckFailed);

        await FirstRead(rig);             // Clear spot N started
        await Tick(rig);                  // its check could not run

        Assert.Equal(PulseState.Stopped, rig.Loop.State);
        Assert.Contains("could not check", rig.Loop.StopReason);
        await Tick(rig);
        Assert.Equal(3, rig.Macros.Runs.Count);
    }

    [Fact]
    public async Task Rock_cap_goes_to_top_and_says_why()
    {
        var rig = Build();
        SkipAllClears(rig);
        await FirstRead(rig);
        await Ticks(rig, 8);              // pass skipped everything: Auto Mine on started
        await Tick(rig);                  // on finished: ride timer

        rig.Clock.Advance(300_000);
        await Tick(rig);                  // ride over: Auto Mine off
        await Tick(rig);                  // off finished: settle
        rig.Clock.Advance(1000);
        await Tick(rig);                  // read: same layer, 5 minutes without a clear

        Assert.Equal(PulseState.GoingToTop, rig.Loop.State);
        Assert.Equal("id-top", rig.Macros.RunIds.Last());
        Assert.Contains(rig.Log, l => l == "Went to top: 5 minutes on the grey layer");
    }

    [Fact]
    public async Task Time_behind_does_not_count_toward_the_rock_cap()
    {
        var rig = Build();
        SkipAllClears(rig);
        await FirstRead(rig);             // t = 3 s, the grey layer starts
        await Ticks(rig, 8);              // pass skipped everything: Auto Mine on started
        await Tick(rig);                  // on finished: ride timer

        await Tick(rig, front: false);    // behind from t = 3 s
        rig.Clock.Advance(300_000);
        await Tick(rig);                  // back at t = 303 s: the ride is over, Auto Mine off
        await Tick(rig);                  // off finished: settle
        rig.Clock.Advance(1000);
        await Tick(rig);                  // read at t = 304 s: 1 s in front since the layer started

        Assert.Equal(PulseState.Clearing, rig.Loop.State);
        Assert.DoesNotContain("id-top", rig.Macros.RunIds);
    }

    [Fact]
    public async Task Riding_through_an_upper_layer_never_trips_the_rock_cap()
    {
        var rig = Build(everywhere: PulseFixtures.Navy);
        await FirstRead(rig);             // t = 3 s: navy, above the target, Auto Mine on started
        await Tick(rig);                  // on finished: ride timer

        rig.Clock.Advance(300_000);
        await Tick(rig);                  // ride over: Auto Mine off
        await Tick(rig);                  // off finished: settle
        rig.Clock.Advance(1000);
        await Tick(rig);                  // read: still navy, 301 s later

        Assert.Equal(PulseState.Riding, rig.Loop.State);
        Assert.Equal("id-on", rig.Macros.RunIds.Last());
        Assert.DoesNotContain("id-top", rig.Macros.RunIds);
    }

    [Fact]
    public async Task A_cleared_block_restarts_the_rock_cap()
    {
        var rig = Build();
        SkipAllClears(rig);
        await FirstRead(rig);             // t = 3 s, the grey layer starts
        await Ticks(rig, 8);
        await Tick(rig);
        rig.Clock.Advance(240_000);
        await Tick(rig);
        await Tick(rig);
        rig.Clock.Advance(1000);
        await Tick(rig);                  // t = 244 s: read, Clear spot N started
        rig.Macros.Script("id-clear-N", ScriptedMacros.Finished);
        await Ticks(rig, 8);              // N cleared at 244 s, the rest skipped

        rig.Clock.Advance(60_000);
        await Tick(rig);                  // t = 304 s: 60 s since the clear, not 301 s since the layer

        Assert.Equal(PulseState.Clearing, rig.Loop.State);
        Assert.DoesNotContain("id-top", rig.Macros.RunIds);
    }

    [Fact]
    public async Task Waits_while_the_account_is_not_in_front()
    {
        var rig = Build();

        await Tick(rig, front: false);
        Assert.Empty(rig.Macros.Runs);

        await Tick(rig);
        await Tick(rig, front: false);    // follows the started playback from behind
        Assert.Single(rig.Macros.Polls);

        rig.Clock.Advance(5000);
        await Tick(rig, front: false);
        Assert.Equal(new[] { "id-on" }, rig.Macros.RunIds);

        await Tick(rig);                  // back in front: the ride is long over, so pause now
        Assert.Equal(new[] { "id-on", "id-off" }, rig.Macros.RunIds);
    }

    [Fact]
    public async Task Missing_Ur_Task_stops_with_a_log_line()
    {
        var rig = Build();
        rig.Macros.RunReplies.Enqueue(ScriptedMacros.Refusal("ur-task-not-running"));

        await Tick(rig);
        await Tick(rig);

        Assert.Equal(PulseState.Stopped, rig.Loop.State);
        Assert.Contains("Ur Task is not running", rig.Loop.StopReason);
        Assert.Contains(rig.Log, l => l.StartsWith("stopped: Ur Task is not running"));
        Assert.Single(rig.Macros.Runs);
    }

    [Fact]
    public async Task A_failed_check_on_Auto_Mine_off_stops_the_loop()
    {
        var rig = Build();
        rig.Macros.Script("id-off", ScriptedMacros.CheckFailed);

        await Tick(rig);
        await Tick(rig);
        rig.Clock.Advance(2000);
        await Tick(rig);
        await Tick(rig);

        Assert.Equal(PulseState.Stopped, rig.Loop.State);
        Assert.Contains("popup or captcha", rig.Loop.StopReason);
    }

    [Fact]
    public async Task A_playback_stopped_in_Ur_Task_stops_the_loop()
    {
        var rig = Build();
        rig.Macros.Script("id-clear-N", ScriptedMacros.Stopped);

        await FirstRead(rig);
        await Tick(rig);

        Assert.Equal(PulseState.Stopped, rig.Loop.State);
        Assert.Contains("stopped in Ur Task", rig.Loop.StopReason);
        await Tick(rig);
        Assert.Equal(3, rig.Macros.Runs.Count);
    }

    [Fact]
    public async Task An_interrupted_clear_runs_the_same_spot_again()
    {
        var rig = Build();
        rig.Macros.Script("id-clear-N", ScriptedMacros.Aborted, ScriptedMacros.Finished);

        await FirstRead(rig);
        await Tick(rig, front: false);    // aborted while the account was behind
        await Tick(rig);                  // in front again: N once more

        Assert.Equal(2, rig.Macros.RunIds.Count(id => id == "id-clear-N"));
        Assert.Equal(PulseState.Clearing, rig.Loop.State);
    }

    [Fact]
    public async Task A_lost_playback_moves_on()
    {
        var rig = Build();
        rig.Macros.Script("id-off", ScriptedMacros.Unknown);

        await Tick(rig);
        await Tick(rig);
        rig.Clock.Advance(2000);
        await Tick(rig);
        await Tick(rig);                  // Ur Task lost the off playback: carry on and settle
        rig.Clock.Advance(1000);
        await Tick(rig);

        Assert.Equal(PulseState.Clearing, rig.Loop.State);
        Assert.Contains(rig.Log, l => l.Contains("no longer knows"));
    }

    [Fact]
    public async Task No_layer_on_a_calm_frame_rides_a_burst()
    {
        var rig = Build(everywhere: PulseFixtures.Orange);

        await FirstRead(rig);

        Assert.Equal(PulseState.Bursting, rig.Loop.State);
        Assert.Equal(new[] { "id-on", "id-off", "id-on" }, rig.Macros.RunIds);
        Assert.Contains(rig.Log, l => l.StartsWith("no layer on a calm frame"));
    }

    [Fact]
    public async Task An_unreadable_window_waits_in_Reading()
    {
        var rig = Build();
        rig.Reader.Next = null;

        await FirstRead(rig);
        await Tick(rig);

        Assert.Equal(PulseState.Reading, rig.Loop.State);
        Assert.Equal(2, rig.Reader.Reads);
        Assert.Equal(2, rig.Macros.Runs.Count);
        Assert.Single(rig.Log, l => l.Contains("could not read the ring"));
    }

    [Fact]
    public async Task A_bad_config_starts_stopped_and_does_nothing()
    {
        var rig = Build(PulseFixtures.Config(target: 4));

        await Tick(rig);

        Assert.Equal(PulseState.Stopped, rig.Loop.State);
        Assert.Contains("targetLayer must be 1 to 3", rig.Loop.StopReason);
        Assert.Empty(rig.Macros.Runs);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~PulseLoopTests"`
Expected: build FAILS with `CS0246: The type or namespace name 'PulseLoop' could not be found`.

- [ ] **Step 4: Implement the loop**

Create `Engine/PulseLoop.cs`:

```csharp
using System.Globalization;
using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

public enum PulseState { Riding, Pausing, Reading, Clearing, Bursting, GoingToTop, Stopped }

/// <summary>
/// One account's ore stop pulse (spec 2026-09-28). Riding: Auto Mine on for BurstMs. Pausing: Auto
/// Mine off, then SettleMs for the effects to clear. Reading: vote the layer on that calm frame.
/// Above the aim layer: ride again. Past it: Go to Top. At it: Clearing, one Clear spot per ring
/// spot, ore first; Ur Task skips a spot with no outline. A pass that cleared something settles and
/// reads again; a pass that cleared nothing rides a burst (Bursting) first. RockCapMinutes on one
/// aim layer with nothing cleared (time behind or paused not counted): Go to Top. Each tick does
/// everything it can and returns at the first wait. Nothing starts unless the account is in front,
/// read again right before each macro; a playback already started is still followed.
/// </summary>
public sealed class PulseLoop
{
    public const int MaxStepsPerTick = 12;

    private readonly PulseConfig _config;
    private readonly RingDefinition? _ring;
    private readonly IReadOnlyList<Trigger> _spots;
    private readonly ISpotReader _reader;
    private readonly IClock _clock;
    private readonly Action<string> _log;
    private readonly MacroCall _call;

    private bool _macroDone;               // this state's macro has ended
    private DateTimeOffset? _until;        // this state's timer: set when its macro ends
    private readonly Queue<int> _queue = new();
    private int _spot = -1;                // the ring order being cleared, -1 between clears
    private readonly List<string> _cleared = new();
    private readonly List<string> _skipped = new();
    private DateTimeOffset _progressAt;    // last new layer or cleared block, for the rock cap
    private bool _unreadableLogged;
    private DateTimeOffset? _behindSince;  // first tick behind (or held), for the rock cap

    public PulseLoop(PulseConfig config, IReadOnlyList<RingDefinition> rings, IReadOnlyList<Trigger> triggers,
        ISpotReader reader, IMacroRunClient macros, IClock clock, Action<string> log)
    {
        _config = config;
        _reader = reader;
        _clock = clock;
        _log = log;
        _ring = TriggerValidation.Find(rings, config.RingId);
        _spots = PulseValidation.SpotsOf(config.RingId, triggers);
        _call = new MacroCall(macros, config.AccountUserId.ToString(CultureInfo.InvariantCulture), clock, log);

        if (PulseValidation.Validate(config, rings, triggers) is { } problem)
        {
            Stop(problem);
            return;
        }
        var mode = config.Mode == PulseMode.OneAbove ? "one above" : "top";
        _log($"started: ring {config.RingId}, target layer {config.TargetLayer} ({mode}), clearing on {_ring!.Layers[config.AimLayer - 1].Name}");
    }

    public long AccountUserId => _config.AccountUserId;
    public PulseState State { get; private set; } = PulseState.Riding;
    public string? StopReason { get; private set; }
    /// <summary>The layer of the last calm read, null before the first and after a Go to Top.</summary>
    public string? Layer { get; private set; }

    private PulseMacros M => _config.Macros!;

    /// <param name="inFront">A fresh read of the foreground gate for this account, asked right
    /// before each RunMacro (Ur Task focuses the target on every run).</param>
    public async Task TickAsync(bool foreground, int pid, CancellationToken ct, Func<bool>? inFront = null)
    {
        if (foreground) NoteFront(); else NoteBehind();
        for (var step = 0; step < MaxStepsPerTick && State != PulseState.Stopped; step++)
        {
            if (_call.Active)
            {
                var r = await _call.StepAsync(foreground, ct, inFront).ConfigureAwait(false);
                if (r.Status == CallStatus.Waiting) return;
                OnCallEnded(r);
                continue;
            }
            if (!foreground) return;
            if (!Act(pid)) return;
        }
    }

    /// <summary>The account is behind, or the runner is held (F9, dry run): from now until it is
    /// back in front does not count toward the rock cap.</summary>
    public void NoteBehind() => _behindSince ??= _clock.Now;

    private void NoteFront()
    {
        if (_behindSince is not { } since) return;
        _behindSince = null;
        if (Layer is not null) _progressAt += _clock.Now - since;
    }

    /// <summary>One move in the current state. False when it has to wait (a timer, an unreadable ring).</summary>
    private bool Act(int pid)
    {
        var now = _clock.Now;
        switch (State)
        {
            case PulseState.Riding:
            case PulseState.Bursting:
                if (!_macroDone) return Begin(M.AutoMineOn, PulseMacroNames.AutoMineOn);
                return now >= _until && Enter(PulseState.Pausing);

            case PulseState.Pausing:
                if (!_macroDone) return Begin(M.AutoMineOff, PulseMacroNames.AutoMineOff);
                return now >= _until && Enter(PulseState.Reading);

            case PulseState.Reading:
                return Read(pid, now);

            case PulseState.Clearing:
                return ClearNext();

            case PulseState.GoingToTop:
                if (!_macroDone) return Begin(M.GoToTop, PulseMacroNames.GoToTop);
                Layer = null;              // back at the top: the rock cap starts over
                return Enter(PulseState.Riding);

            default:
                return false;
        }
    }

    private bool Read(int pid, DateTimeOffset now)
    {
        var samples = _reader.Read(pid, _spots);
        if (samples is null || samples.Count == 0)
        {
            if (!_unreadableLogged) _log("could not read the ring (window hidden or gone); waiting");
            _unreadableLogged = true;
            return false;
        }
        _unreadableLogged = false;

        var ring = _ring!;
        var votes = RingTracker.Vote(ring, samples.Select(s => new SpotSample(s.Order, s.Sampled, s.ToleranceRgb)).ToList());
        var layer = RingTracker.Pick(ring, votes, Layer);
        if (layer is null)
        {
            var best = votes.Count == 0 ? 0 : votes.Values.Max();
            _log($"no layer on a calm frame ({best} of {samples.Count} spots at best): riding a burst");
            return Enter(PulseState.Bursting);
        }

        if (!string.Equals(layer, Layer, StringComparison.OrdinalIgnoreCase))
        {
            Layer = layer;
            _progressAt = now;
        }
        var number = LayerNumber(layer);
        var aim = _config.AimLayer;
        var aimName = ring.Layers[aim - 1].Name;
        var seen = $"layer {layer} ({votes[layer]} of {samples.Count} spots)";

        if (number > aim)
        {
            _log($"{seen} is past the target ({aimName}): going to top");
            return Enter(PulseState.GoingToTop);
        }
        if (number < aim)
        {
            _log($"{seen} is above the target ({aimName}): riding on");
            return Enter(PulseState.Riding);
        }
        // The rock cap counts only while clearing (spec step 5), so the slow pulsed descent never trips it.
        if ((now - _progressAt).TotalMinutes >= _config.RockCapMinutes)
        {
            var unit = _config.RockCapMinutes == 1 ? "minute" : "minutes";
            _log($"Went to top: {_config.RockCapMinutes} {unit} on the {layer} layer");
            return Enter(PulseState.GoingToTop);
        }

        var order = PulseOrder.Rank(samples, ring.Layers[number - 1].Rock);
        _queue.Clear();
        foreach (var o in order) _queue.Enqueue(o);
        _spot = -1;
        _cleared.Clear();
        _skipped.Clear();
        _log($"{seen} is the target: clearing {string.Join(" ", order.Select(SpotName))}");
        return Enter(PulseState.Clearing);
    }

    private bool ClearNext()
    {
        if (_spot < 0)
        {
            if (_queue.Count == 0) return EndPass();
            _spot = _queue.Dequeue();
        }
        return Begin(M.Clear[_spot], PulseMacroNames.Clear(SpotName(_spot)));
    }

    private bool EndPass()
    {
        if (_cleared.Count > 0)
        {
            var skipped = _skipped.Count > 0 ? $"; nothing to clear at {string.Join(" ", _skipped)}" : "";
            _log($"cleared {string.Join(" ", _cleared)}{skipped}: reading again");
            return Enter(PulseState.Pausing, macroDone: true);   // Auto Mine is still off: just settle
        }
        _log($"nothing in reach to clear ({_skipped.Count} spots skipped): riding a burst");
        return Enter(PulseState.Bursting);
    }

    private void OnCallEnded(CallResult r)
    {
        if (r.Status == CallStatus.Stop)
        {
            Stop(r.Detail ?? $"'{r.Label}' could not run.");
            return;
        }

        if (State == PulseState.Clearing)
        {
            var name = SpotName(_spot);
            switch (r.Status)
            {
                case CallStatus.Done:
                    _cleared.Add(name);
                    _progressAt = _clock.Now;
                    break;
                case CallStatus.CheckFailed:
                    // A missing outline is "skipped"; check-failed means the check could not run.
                    Stop($"'{r.Label}' stopped at its check ({r.Detail}): Ur Task could not check the spot " +
                         "(window hidden, or a box outside it), so the pulse loop stopped rather than ride on blind.");
                    return;
                case CallStatus.Lost:
                    _skipped.Add(name);
                    _log($"'{r.Label}': {r.Detail} Counted as nothing to clear.");
                    break;
                default:
                    _skipped.Add(name);
                    break;
            }
            _spot = -1;
            return;
        }

        if (r.Status == CallStatus.CheckFailed)
        {
            Stop($"'{r.Label}' stopped at its colour check ({r.Detail}). Something may be over the game, " +
                 "such as a popup or captcha, so the pulse loop stopped rather than click it.");
            return;
        }
        if (r.Status == CallStatus.Lost) _log($"'{r.Label}': {r.Detail} Moving on.");
        _macroDone = true;
        _until = TimerFor(State);
    }

    private bool Begin(string macroId, string label)
    {
        _call.Begin(macroId, label);
        return true;
    }

    /// <summary>Always true, so Act can `return Enter(...)` and the tick carries on in the new state.</summary>
    private bool Enter(PulseState next, bool macroDone = false)
    {
        State = next;
        _macroDone = macroDone;
        _until = macroDone ? TimerFor(next) : null;
        return true;
    }

    private DateTimeOffset? TimerFor(PulseState state) => state switch
    {
        PulseState.Riding or PulseState.Bursting => _clock.Now.AddMilliseconds(_config.BurstMs),
        PulseState.Pausing => _clock.Now.AddMilliseconds(_config.SettleMs),
        _ => null,
    };

    private void Stop(string reason)
    {
        State = PulseState.Stopped;
        StopReason = reason;
        _log($"stopped: {reason}");
    }

    private int LayerNumber(string name)
    {
        var layers = _ring!.Layers;
        for (var i = 0; i < layers.Count; i++)
            if (string.Equals(layers[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i + 1;
        return 0;
    }

    private static string SpotName(int order) =>
        order >= 0 && order < MeasuredRing.RingOrder.Length
            ? MeasuredRing.RingOrder[order]
            : order.ToString(CultureInfo.InvariantCulture);
}
```

Note on `return now >= _until && Enter(...)`: `_until` is set whenever `_macroDone` is true in Riding, Bursting and Pausing, and the lifted comparison is false while it would be null, so the loop waits rather than skipping the timer.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~PulseLoopTests"`
Expected: PASS (22).

If a tick-count test fails by one tick, re-derive it from the rule "each tick does everything it can and returns at the first wait" and the FirstRead comments before changing the loop: the tests pin that rule, not just the end states.

Full suite: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj`
Expected: **312 passed**.

- [ ] **Step 6: Commit**

```powershell
git add Engine/PulseLoop.cs tests/RoRoRo.UrOcr.Tests/Engine/PulseLoopFixtures.cs tests/RoRoRo.UrOcr.Tests/Engine/PulseLoopTests.cs
git commit -m "feat(engine): the ore stop pulse loop" -m "Ride a burst, pause, read the layer on the calm frame, and at the target clear every ring spot ore first. Nothing cleared rides another burst; past the target, or 5 minutes in front on the target layer without a clear, goes to top. A clear whose check could not run stops the loop." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Run the loops, and stand the ring triggers down for pulse accounts

**Files:**
- Create: `Engine/PulseRunner.cs`
- Modify: `Engine/ActivityLog.cs:3`
- Modify: `Engine/TriggerCoordinator.cs` (constructor parameters at lines 39-52, the trigger loop in `TickOnceAsync` at lines 143-174, and a new private method)
- Modify: `PluginHost/AccountRegistry.cs:8`
- Modify: `PluginRuntime.cs`
- Test: `tests/RoRoRo.UrOcr.Tests/Engine/PulseRunnerTests.cs`
- Test: `tests/RoRoRo.UrOcr.Tests/Engine/RingOwnershipTests.cs`

**Interfaces:**
- Consumes: `PulseLoop`, `PulseState` (Task 5); `ISpotReader`, `SpotReader` (Task 3); `TriggerStore.Pulses`, `TriggerStore.UpsertPulse` (Task 2); existing `IForegroundCheck`, `IElevationCheck`, `IClock`, `ActivityLog`, `TriggerCoordinator`. Tests: `PulseClock`, `ScriptedMacros` (Task 4, `PulseFakes.cs`), `PulseFixtures` (Task 5, `PulseLoopFixtures.cs`).
- Produces:
  - `public interface IAccountLookup { bool TryGetUserId(int pid, out long userId); }` (namespace `RoRoRo.UrOcr.Engine`), implemented by `AccountRegistry`
  - `public sealed class PulseRunner(TriggerStore store, ISpotReader reader, IMacroRunClient macros, IForegroundCheck foreground, IElevationCheck elevation, IAccountLookup accounts, IClock clock, ActivityLog log, Action<string>? diag = null)` with `TickRateHz`, `WatchdogTimeout`, `Func<bool> Hold` (held ticks call `NoteBehind` on every loop), a fresh foreground read passed to each loop as `inFront`, `PulseLoop? LoopFor(long)`, `bool OwnsRing(int pid, string ringId)`, `Task TickOnceAsync(CancellationToken)`, `Start()`, `Task StopAsync()`
  - `ActivityKind.Pulse` (appended last)
  - `TriggerCoordinator(..., Action<string>? diag = null, Func<int, string, bool>? ringOwner = null)`: a trigger whose ring (`Ring.RingId` or `Layer.RingId`) is owned for this tick's foreground pid is skipped and disarmed.
  - `PluginRuntime.Pulse : PulseRunner?`

- [ ] **Step 1: Write the failing tests**

Create `tests/RoRoRo.UrOcr.Tests/Engine/PulseRunnerTests.cs`:

```csharp
using System.IO;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

public class PulseRunnerTests
{
    private sealed class Front : IForegroundCheck
    {
        public bool IsAlt = true;
        public int Pid = 100;
        /// <summary>Pids handed out first, one per read, before falling back to Pid.</summary>
        public Queue<int> Next { get; } = new();
        public bool IsForegroundAnAlt() => IsAlt;
        public int GetForegroundPid() => Next.Count > 0 ? Next.Dequeue() : Pid;
    }
    private sealed class Elevation : IElevationCheck
    {
        public bool Elevated;
        public bool IsForegroundProcessLikelyElevated(int pid) => Elevated;
    }
    private sealed class Accounts : IAccountLookup
    {
        public Dictionary<int, long> Map { get; } = new() { [100] = 42, [200] = 43 };
        public bool TryGetUserId(int pid, out long userId) => Map.TryGetValue(pid, out userId);
    }
    private sealed class NoRing : ISpotReader
    {
        public IReadOnlyList<SpotReading>? Read(int pid, IReadOnlyList<Trigger> spots) => null;
    }

    private sealed record Rig(PulseRunner Runner, TriggerStore Store, ScriptedMacros Macros, Front Front,
        Elevation Elevation, ActivityLog Log, List<string> Diag);

    private static Rig Build(params PulseConfig[] pulses)
    {
        var store = new TriggerStore(Path.Combine(Path.GetTempPath(), "urocr-tests", Guid.NewGuid().ToString("N") + ".json"));
        store.UpsertRing(PulseFixtures.Ring());
        foreach (var spot in PulseFixtures.Spots()) store.Upsert(spot);
        foreach (var p in pulses.Length == 0 ? new[] { PulseFixtures.Config(account: 42), PulseFixtures.Config(account: 43) } : pulses)
            store.UpsertPulse(p);
        var macros = new ScriptedMacros();
        var front = new Front();
        var elevation = new Elevation();
        var log = new ActivityLog(capacity: 1000);
        var diag = new List<string>();
        var runner = new PulseRunner(store, new NoRing(), macros, front, elevation, new Accounts(),
            new PulseClock(), log, diag.Add);
        return new Rig(runner, store, macros, front, elevation, log, diag);
    }

    private static Task Tick(Rig r) => r.Runner.TickOnceAsync(CancellationToken.None);

    [Fact]
    public async Task Only_the_account_in_front_acts()
    {
        var rig = Build();

        await Tick(rig);
        Assert.Equal(new[] { "42" }, Assert.Single(rig.Macros.Runs).Targets);

        rig.Front.Pid = 200;
        await Tick(rig);

        Assert.Equal(2, rig.Macros.Runs.Count);
        Assert.Equal(new[] { "43" }, rig.Macros.Runs[1].Targets);
        Assert.Single(rig.Macros.Polls);   // 42 still followed its playback from behind
    }

    [Fact]
    public async Task The_front_is_checked_again_right_before_a_macro_starts()
    {
        var rig = Build(PulseFixtures.Config(account: 42));
        rig.Front.Next.Enqueue(100);      // the tick starts with 42 in front
        rig.Front.Pid = 300;              // then Este tabs to a window that is no RoRoRo account

        await Tick(rig);

        Assert.Empty(rig.Macros.Runs);
        Assert.Equal(PulseState.Riding, rig.Runner.LoopFor(42)!.State);
    }

    [Fact]
    public async Task A_window_that_is_not_an_alt_moves_nobody()
    {
        var rig = Build();
        rig.Front.IsAlt = false;

        await Tick(rig);

        Assert.Empty(rig.Macros.Runs);
    }

    [Fact]
    public async Task An_elevated_foreground_moves_nobody()
    {
        var rig = Build();
        rig.Elevation.Elevated = true;

        await Tick(rig);

        Assert.Empty(rig.Macros.Runs);
    }

    [Fact]
    public async Task Hold_stops_everything()
    {
        var rig = Build();
        rig.Runner.Hold = () => true;

        await Tick(rig);

        Assert.Empty(rig.Macros.Runs);
        Assert.Null(rig.Runner.LoopFor(42));
    }

    [Fact]
    public void Owns_the_ring_only_for_an_account_with_an_enabled_pulse()
    {
        var rig = Build(PulseFixtures.Config(account: 42), PulseFixtures.Config(account: 43) with { Enabled = false });

        Assert.True(rig.Runner.OwnsRing(100, "mine8"));
        Assert.True(rig.Runner.OwnsRing(100, "MINE8"));
        Assert.False(rig.Runner.OwnsRing(100, "mine9"));
        Assert.False(rig.Runner.OwnsRing(200, "mine8"));   // pulse turned off
        Assert.False(rig.Runner.OwnsRing(300, "mine8"));   // not a RoRoRo account
        Assert.False(rig.Runner.OwnsRing(0, "mine8"));     // no foreground alt
    }

    [Fact]
    public async Task A_pulse_on_a_missing_ring_logs_why_it_stopped()
    {
        var rig = Build(PulseFixtures.Config(account: 42) with { RingId = "mine9" });

        await Tick(rig);

        Assert.Equal(PulseState.Stopped, rig.Runner.LoopFor(42)!.State);
        Assert.Empty(rig.Macros.Runs);
        Assert.Contains(rig.Log.Snapshot(), e => e.Kind == ActivityKind.Pulse && e.TriggerName == "(pulse 42)"
                                                && e.Detail!.Contains("Ring mine9 is not defined"));
        Assert.Contains(rig.Diag, l => l.StartsWith("pulse 42: stopped: Ring mine9 is not defined"));
    }

    [Fact]
    public async Task A_disabled_pulse_gets_no_loop()
    {
        var rig = Build(PulseFixtures.Config(account: 42) with { Enabled = false });

        await Tick(rig);

        Assert.Null(rig.Runner.LoopFor(42));
        Assert.Empty(rig.Macros.Runs);
    }
}
```

Create `tests/RoRoRo.UrOcr.Tests/Engine/RingOwnershipTests.cs`:

```csharp
using System.Drawing;
using System.IO;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

/// <summary>The 0.5.0 ring triggers stand down for an account whose pulse owns the ring.</summary>
public class RingOwnershipTests
{
    private sealed class Clock : IClock { public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch; }
    private sealed class PaintedCapture : ICaptureSource
    {
        public Dictionary<int, Rgb> ByX { get; } = new();
        public Bitmap Capture(RegionRect r)
        {
            var c = ByX.TryGetValue(r.X, out var v) ? v : PulseFixtures.Grey;
            var bmp = new Bitmap(Math.Max(1, r.Width), Math.Max(1, r.Height));
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.FromArgb(c.R, c.G, c.B));
            return bmp;
        }
    }
    private sealed class NoText : ITextMatchEngine
    {
        public Task<(bool matched, string text)> RunAsync(Bitmap b, TextCriteria c) => Task.FromResult((false, ""));
        public Task<(bool matched, string text)> RunWithPreprocessAsync(Bitmap b, TextCriteria c) => Task.FromResult((false, ""));
    }
    private sealed class Fg : IForegroundCheck { public bool IsForegroundAnAlt() => true; public int GetForegroundPid() => 1; }
    private sealed class NotElevated : IElevationCheck { public bool IsForegroundProcessLikelyElevated(int pid) => false; }
    private sealed class NoKeys : IKeyPress { public void Press(KeyCombo c) { } }
    private sealed class Metrics : IWindowMetrics
    {
        public IntPtr HwndForPid(int pid) => new(0x10);
        public (int X, int Y)? ClientOrigin(IntPtr h) => (0, 0);
        public (int W, int H)? ClientSize(IntPtr h) => (800, 599);
    }
    private sealed class RecordingMacros : IMacroRunClient
    {
        public List<string> Calls { get; } = new();
        public Task<RunMacroResponse> RunAsync(string macroId, IReadOnlyList<string>? targets, CancellationToken ct)
        {
            Calls.Add(macroId);
            return Task.FromResult(new RunMacroResponse(true, "pb", false, null, null));
        }
    }

    private static (TriggerCoordinator C, RecordingMacros Macros) Build(Func<int, string, bool> owner, bool accountAware = true)
    {
        var store = new TriggerStore(Path.Combine(Path.GetTempPath(), "urocr-tests", Guid.NewGuid().ToString("N") + ".json"));
        store.UpsertRing(PulseFixtures.Ring());
        foreach (var spot in PulseFixtures.Spots())
        {
            spot.AccountAware = accountAware;
            store.Upsert(spot);
        }
        var paint = new PaintedCapture();
        paint.ByX[100 + 3 * 20] = PulseFixtures.Orange;   // spot 3 (SE) shows ore
        var macros = new RecordingMacros();
        var c = new TriggerCoordinator(store, paint, new ColorMatcher(), new NoText(), new Fg(), new NotElevated(),
            new NoKeys(), new ActivityLog(capacity: 1000), new Clock(), new Metrics(),
            macroClient: macros, ringOwner: owner);
        return (c, macros);
    }

    [Fact]
    public async Task A_ring_owned_by_a_pulse_loop_does_not_fire()
    {
        var (c, macros) = Build((pid, ring) => pid == 1 && ring == "mine8");

        await c.TickOnceAsync(CancellationToken.None);

        Assert.Empty(macros.Calls);
    }

    [Fact]
    public async Task The_same_ring_fires_for_an_account_without_a_pulse()
    {
        var (c, macros) = Build((pid, _) => pid == 2);

        await c.TickOnceAsync(CancellationToken.None);

        Assert.Equal(new[] { "mine-3" }, macros.Calls);
    }

    [Fact]
    public async Task A_trigger_without_the_account_gate_is_never_owned()
    {
        var (c, macros) = Build((_, _) => true, accountAware: false);

        await c.TickOnceAsync(CancellationToken.None);

        Assert.Equal(new[] { "mine-3" }, macros.Calls);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~PulseRunnerTests|FullyQualifiedName~RingOwnershipTests"`
Expected: build FAILS with `CS0246: The type or namespace name 'PulseRunner' could not be found` and `CS1739: ... does not have a parameter named 'ringOwner'`.

- [ ] **Step 3: Add the activity kind and the account lookup**

In `Engine/ActivityLog.cs`, replace line 3:

```csharp
public enum ActivityKind { Fired, WouldFire, NoMatch, SkippedCooldown, SkippedNotAlt, BlockedElevated, Error, Busy, Deferred, LayerChanged, Holding, Pulse }
```

In `PluginHost/AccountRegistry.cs`, replace line 8:

```csharp
public sealed class AccountRegistry : RoRoRo.UrOcr.Engine.IAccountLookup
```

(`TryGetUserId(int pid, out long userId)` already exists with the right signature.)

- [ ] **Step 4: Implement the runner**

Create `Engine/PulseRunner.cs`:

```csharp
using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

/// <summary>Which RoRoRo account a process is. AccountRegistry in production.</summary>
public interface IAccountLookup { bool TryGetUserId(int pid, out long userId); }

/// <summary>
/// Ticks every enabled pulse loop at TickRateHz. Only the loop of the foreground account (a
/// RoRoRo alt, not elevated) may act; the others only follow playbacks they already started. A
/// loop is built once per enabled pulse and keeps its state; a changed pulse needs a restart,
/// which --import-pulse requires anyway. Hold (pause, dry run) stops everything.
/// </summary>
public sealed class PulseRunner(
    TriggerStore store,
    ISpotReader reader,
    IMacroRunClient macros,
    IForegroundCheck foreground,
    IElevationCheck elevation,
    IAccountLookup accounts,
    IClock clock,
    ActivityLog log,
    Action<string>? diag = null)
{
    public int TickRateHz { get; set; } = 5;
    public TimeSpan WatchdogTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public Func<bool> Hold { get; set; } = () => false;

    private readonly Dictionary<long, PulseLoop> _loops = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public PulseLoop? LoopFor(long accountUserId) => _loops.GetValueOrDefault(accountUserId);

    /// <summary>
    /// True when the account in <paramref name="pid"/> has an enabled pulse on the ring, so the
    /// ring's 0.5.0 triggers stand down for it. Enabled, not running: a stopped loop still owns its
    /// ring, so the old path never takes over unannounced.
    /// </summary>
    public bool OwnsRing(int pid, string ringId)
    {
        if (pid == 0 || !accounts.TryGetUserId(pid, out var userId)) return false;
        return store.Pulses.Any(p => p.Enabled && p.AccountUserId == userId
                                     && string.Equals(p.RingId, ringId, StringComparison.OrdinalIgnoreCase));
    }

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_loop is not null)
        {
            try { await _loop; }
            catch (OperationCanceledException) { }
        }
        _loop = null;
        _cts = null;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        var period = TimeSpan.FromSeconds(1.0 / TickRateHz);
        while (!ct.IsCancellationRequested)
        {
            var tickStart = clock.Now;
            try
            {
                using var tickCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                tickCts.CancelAfter(WatchdogTimeout);
                await TickOnceAsync(tickCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                log.Record(Guid.Empty, "(pulse)", ActivityKind.Error, $"watchdog: tick exceeded {WatchdogTimeout.TotalSeconds:0}s");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                log.Record(Guid.Empty, "(pulse)", ActivityKind.Error, ex.Message);
            }
            var remain = period - (clock.Now - tickStart);
            if (remain > TimeSpan.Zero)
            {
                try { await Task.Delay(remain, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    public async Task TickOnceAsync(CancellationToken ct)
    {
        if (Hold())
        {
            // Paused or dry run: nothing acts, and the time does not count toward any rock cap.
            foreach (var held in _loops.Values) held.NoteBehind();
            return;
        }
        Sync();
        if (_loops.Count == 0) return;

        var (front, pid) = Front();
        foreach (var loop in _loops.Values.ToList())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // Ur Task focuses the target on every run, so the gate is read again right before one.
                var account = loop.AccountUserId;
                await loop.TickAsync(account == front, pid, ct, () => Front().Front == account);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One loop's failure must not stop the others.
                Write(loop.AccountUserId, $"tick failed: {ex.Message}");
            }
        }
    }

    /// <summary>The account in front, the same gate triggers use: a RoRoRo alt, not elevated.</summary>
    private (long? Front, int Pid) Front()
    {
        if (!foreground.IsForegroundAnAlt()) return (null, 0);
        var pid = foreground.GetForegroundPid();
        if (elevation.IsForegroundProcessLikelyElevated(pid)) return (null, 0);
        return accounts.TryGetUserId(pid, out var userId) ? (userId, pid) : (null, 0);
    }

    private void Sync()
    {
        var enabled = store.Pulses.Where(p => p.Enabled).ToList();
        foreach (var gone in _loops.Keys.Where(id => enabled.All(p => p.AccountUserId != id)).ToList())
        {
            _loops.Remove(gone);
            Write(gone, "pulse turned off");
        }
        foreach (var pulse in enabled)
        {
            if (_loops.ContainsKey(pulse.AccountUserId)) continue;   // first pulse per account wins
            var account = pulse.AccountUserId;
            _loops[account] = new PulseLoop(pulse, store.Rings, store.All, reader, macros, clock,
                message => Write(account, message));
        }
    }

    private void Write(long account, string message)
    {
        log.Record(Guid.Empty, $"(pulse {account})", ActivityKind.Pulse, message);
        diag?.Invoke($"pulse {account}: {message}");
    }
}
```

- [ ] **Step 5: Let the coordinator stand down for owned rings**

In `Engine/TriggerCoordinator.cs`, change the end of the constructor parameter list from:

```csharp
    IMacroRunClient? macroClient = null,
    Action<string>? diag = null)
```

to:

```csharp
    IMacroRunClient? macroClient = null,
    Action<string>? diag = null,
    Func<int, string, bool>? ringOwner = null)
```

In `TickOnceAsync`, in the `foreach (var trig in store.All)` loop, insert right after the closing brace of the `if (IsGated(trig)) { ... }` block and before `Reading? reading;`:

```csharp
            if (OwnedByPulse(trig, pid))
            {
                // This account's pulse loop runs this ring; its spots and layer triggers stand down.
                Disarm(trig.Id);
                continue;
            }
```

Add this method after `IsGated`:

```csharp
    /// <summary>
    /// A ring an enabled pulse runs for this tick's foreground account belongs to the pulse: its
    /// spots, rock cap and camera rule stand down so two paths never drive one account. pid is 0
    /// for an ungated trigger, which no pulse can own. With no owned spot read, the ring reads
    /// "not visible" for that account, so its layer triggers never fire either.
    /// </summary>
    private bool OwnedByPulse(Trigger trig, int pid)
    {
        if (ringOwner is null || pid == 0) return false;
        var ringId = trig.Ring?.RingId ?? trig.Layer?.RingId;
        return ringId is not null && ringOwner(pid, ringId);
    }
```

- [ ] **Step 6: Wire it into the runtime**

In `PluginRuntime.cs`, add after the `Coordinator` property:

```csharp
    public PulseRunner? Pulse { get; private set; }
```

In `StartAsync`, replace the block from `Coordinator = new TriggerCoordinator(` through `Coordinator.Start();` with:

```csharp
        Pulse = new PulseRunner(
            Triggers, new SpotReader(Capture, WindowMetrics), MacroClient, Foreground, Elevation, Accounts,
            new SystemClock(), Activity, Diagnostics.DiagLog.Write)
        {
            TickRateHz = Settings.Current.TickRateHz,
        };

        Coordinator = new TriggerCoordinator(
            Triggers, Capture, Color, Text, Foreground, Elevation, Keys, Activity,
            new SystemClock(), WindowMetrics,
            onFirstFire: t => Toasts.Show(t.Action == Storage.TriggerAction.RunMacro
                ? $"✓ \"{t.Name}\" ran a macro"
                : $"✓ \"{t.Name}\" fired ({t.Keybind.Key})"),
            macroClient: MacroClient,
            diag: Diagnostics.DiagLog.Write,
            ringOwner: Pulse.OwnsRing)
        {
            TickRateHz = Settings.Current.TickRateHz,
        };
        // F9 (pause all) and dry run hold the pulse too.
        Pulse.Hold = () => Coordinator?.Paused == true || Coordinator?.DryRun == true;
        Coordinator.Start();
        Pulse.Start();
```

In `StopAsync`, after `if (Coordinator is not null) await Coordinator.StopAsync();` add:

```csharp
        if (Pulse is not null) await Pulse.StopAsync();
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~PulseRunnerTests|FullyQualifiedName~RingOwnershipTests"`
Expected: PASS (8 + 3).

Build the app too: `dotnet build rororo-ur-ocr.csproj`
Expected: `Build succeeded`, 0 errors.

Full suite: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj`
Expected: **323 passed**. Every existing coordinator test passes unchanged (`ringOwner` defaults to null).

- [ ] **Step 8: Commit**

```powershell
git add Engine/PulseRunner.cs Engine/ActivityLog.cs Engine/TriggerCoordinator.cs PluginHost/AccountRegistry.cs PluginRuntime.cs tests/RoRoRo.UrOcr.Tests/Engine/PulseRunnerTests.cs tests/RoRoRo.UrOcr.Tests/Engine/RingOwnershipTests.cs
git commit -m "feat(engine): run pulse loops for the account in front" -m "Only the foreground account's loop acts, checked again right before each macro, pause and dry run hold it, and an account with an enabled pulse owns its ring: the 0.5.0 ring triggers stand down for that account and keep working for every other." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Import a pulse file (`--import-pulse`)

**Files:**
- Create: `Storage/PulseImporter.cs`
- Create: `PulseImportCommand.cs`
- Modify: `RingImportCommand.cs` (the `OtherInstanceRunning` method at the end)
- Modify: `Program.cs`
- Test: `tests/RoRoRo.UrOcr.Tests/Storage/PulseImporterTests.cs`
- Test: `tests/RoRoRo.UrOcr.Tests/PulseImportCommandTests.cs`

**Interfaces:**
- Consumes: `PulseFile`, `PulseConfig`, `PulseMacros`, `PulseMacroNames`, `PulseValidation.Validate` (Task 2); `TriggerStore.UpsertPulse/Pulses/Rings/All`; existing `RingImporter.ResolveMacro(IReadOnlyList<UrTaskMacro>, string)` (throws `InvalidDataException` naming a missing or duplicated macro), `RingImporter.Apply`, `UrTaskMacros.Load(dir)`, `MeasuredRing.RingOrder`, `PluginPaths`, `DiagLog`. Tests: `RingImporterTests.Measured()` and `RingImporterTests.Macros()` (internal static, existing; the measured ring has 2 layers, `navy` and `grey`, and macros `Mine spot N` ... `NW`, `Go to Top`, `Camera top-down`).
- Produces:
  - `PulseImporter.Build(PulseFile, IReadOnlyList<RingDefinition>, IReadOnlyList<Trigger>, IReadOnlyList<UrTaskMacro>) -> IReadOnlyList<PulseConfig>` (throws `InvalidDataException`), `PulseImporter.Apply(TriggerStore, PulseFile, IReadOnlyList<UrTaskMacro>) -> IReadOnlyList<PulseConfig>`, `PulseImporter.Resolve(IReadOnlyList<UrTaskMacro>) -> PulseMacros`
  - `PulseImportCommand.Flag = "--import-pulse"`, `PulseImportCommand.Run(IReadOnlyList<string>)`, `PulseImportCommand.Run(args, triggersPath, macrosDir, Func<bool> otherInstanceRunning, reportPath, Action<string> diag)`, `PulseImportCommand.Describe(PulseConfig) -> string`. Exit codes: 0 imported, 2 bad input, 3 Ur OCR running, 64 usage. Report: `pulse-import.log` in the plugin data folder.
  - `RingImportCommand.OtherInstanceRunning()` becomes `internal static`.

- [ ] **Step 1: Write the failing tests**

Create `tests/RoRoRo.UrOcr.Tests/Storage/PulseImporterTests.cs`:

```csharp
using System.IO;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Storage;

public class PulseImporterTests
{
    internal static IReadOnlyList<UrTaskMacro> Macros(params string[] leaveOut) =>
        RingImporterTests.Macros()
            .Append(new UrTaskMacro("id-off", "Auto Mine off (checked)"))
            .Append(new UrTaskMacro("id-on", "Auto Mine on (checked)"))
            .Concat(MeasuredRing.RingOrder.Select(n => new UrTaskMacro($"id-clear-{n}", $"Clear spot {n}")))
            .Where(m => !leaveOut.Contains(m.Name))
            .ToList();

    internal static PulseConfig Entry(long account = 42, int target = 2) => new(account, "mine8", target);

    private static TriggerStore StoreWithRing()
    {
        var store = new TriggerStore(Path.Combine(Path.GetTempPath(), "urocr-tests", Guid.NewGuid().ToString("N") + ".json"));
        RingImporter.Apply(store, RingImporterTests.Measured(), RingImporterTests.Macros());
        return store;
    }

    private static IReadOnlyList<PulseConfig> Build(TriggerStore store, IReadOnlyList<UrTaskMacro>? macros, params PulseConfig[] entries) =>
        PulseImporter.Build(new PulseFile(1, entries), store.Rings, store.All, macros ?? Macros());

    [Fact]
    public void Builds_one_pulse_per_account_with_the_macros_looked_up()
    {
        var pulses = Build(StoreWithRing(), null, Entry(42, 2), Entry(43, 1));

        Assert.Equal(new long[] { 42, 43 }, pulses.Select(p => p.AccountUserId));
        var m = pulses[0].Macros!;
        Assert.Equal("id-off", m.AutoMineOff);
        Assert.Equal("id-on", m.AutoMineOn);
        Assert.Equal("id-go-to-top", m.GoToTop);
        Assert.Equal(MeasuredRing.RingOrder.Select(n => $"id-clear-{n}"), m.Clear);
    }

    [Fact]
    public void Macros_in_the_file_are_replaced_by_the_ones_looked_up()
    {
        var entry = Entry() with { Macros = new PulseMacros("x", "y", "z", new[] { "a" }) };

        var p = Assert.Single(Build(StoreWithRing(), null, entry));

        Assert.Equal("id-off", p.Macros!.AutoMineOff);
    }

    [Fact]
    public void A_target_past_the_rings_layers_is_refused()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Build(StoreWithRing(), null, Entry(target: 3)));

        Assert.Contains("Account 42", ex.Message);
        Assert.Contains("targetLayer must be 1 to 2", ex.Message);
    }

    [Fact]
    public void A_missing_clear_macro_is_refused_by_name()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Build(StoreWithRing(), Macros("Clear spot W"), Entry()));

        Assert.Contains("Clear spot W", ex.Message);
    }

    [Fact]
    public void The_same_account_twice_is_refused()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Build(StoreWithRing(), null, Entry(42), Entry(42, 1)));

        Assert.Contains("42 is listed twice", ex.Message);
    }

    [Fact]
    public void Without_the_ring_it_says_to_import_the_ring_first()
    {
        var empty = new TriggerStore(Path.Combine(Path.GetTempPath(), "urocr-tests", Guid.NewGuid().ToString("N") + ".json"));

        var ex = Assert.Throws<InvalidDataException>(() => Build(empty, null, Entry()));

        Assert.Contains("import the ring first", ex.Message);
    }

    [Fact]
    public void A_wrong_schema_or_an_empty_file_is_refused()
    {
        var store = StoreWithRing();

        Assert.Contains("schema must be 1",
            Assert.Throws<InvalidDataException>(() => PulseImporter.Build(new PulseFile(2, new[] { Entry() }), store.Rings, store.All, Macros())).Message);
        Assert.Contains("pulses is empty",
            Assert.Throws<InvalidDataException>(() => PulseImporter.Build(new PulseFile(1, Array.Empty<PulseConfig>()), store.Rings, store.All, Macros())).Message);
    }

    [Fact]
    public void Apply_replaces_the_accounts_pulse_on_reimport_and_a_bad_file_writes_nothing()
    {
        var store = StoreWithRing();
        PulseImporter.Apply(store, new PulseFile(1, new[] { Entry(target: 2) }), Macros());
        PulseImporter.Apply(store, new PulseFile(1, new[] { Entry(target: 1) }), Macros());

        Assert.Throws<InvalidDataException>(() =>
            PulseImporter.Apply(store, new PulseFile(1, new[] { Entry(43, 1), Entry(44, 9) }), Macros()));

        var p = Assert.Single(store.Pulses);
        Assert.Equal(1, p.TargetLayer);
    }
}
```

Create `tests/RoRoRo.UrOcr.Tests/PulseImportCommandTests.cs`:

```csharp
using System.IO;
using System.Text.Json;
using RoRoRo.UrOcr.Storage;
using RoRoRo.UrOcr.Tests.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests;

public class PulseImportCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "urocr-tests", "pulse-" + Guid.NewGuid().ToString("N"));

    public PulseImportCommandTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "macros"));
        RingImporter.Apply(new TriggerStore(Triggers), RingImporterTests.Measured(), RingImporterTests.Macros());
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Triggers => Path.Combine(_dir, "triggers.json");
    private string Report => Path.Combine(_dir, "pulse-import.log");
    private string MacrosDir => Path.Combine(_dir, "macros");

    private string WritePulses()
    {
        var path = Path.Combine(_dir, "pulse.json");
        File.WriteAllText(path,
            "{\"schema\":1,\"pulses\":[" +
            "{\"accountUserId\":42,\"ringId\":\"mine8\",\"targetLayer\":2}," +
            "{\"accountUserId\":43,\"ringId\":\"mine8\",\"targetLayer\":2,\"mode\":\"oneAbove\",\"rockCapMinutes\":1}]}");
        return path;
    }

    private void WriteMacros()
    {
        foreach (var mac in PulseImporterTests.Macros())
            File.WriteAllText(Path.Combine(MacrosDir, mac.Id + ".json"), JsonSerializer.Serialize(new { id = mac.Id, name = mac.Name }));
    }

    private int Run(Func<bool> running, params string[] args) =>
        PulseImportCommand.Run(args, Triggers, MacrosDir, running, Report, _ => { });

    [Fact]
    public void Imports_the_pulses_and_reports_them()
    {
        WriteMacros();

        var code = Run(() => false, WritePulses());

        Assert.Equal(0, code);
        Assert.Equal(2, new TriggerStore(Triggers).Pulses.Count);
        var report = File.ReadAllText(Report);
        Assert.Contains("imported 2 pulse loops", report);
        Assert.Contains("account 42: ring mine8, target layer 2 (top), burst 2000 ms, settle 1000 ms, rock cap 5 min", report);
        Assert.Contains("account 43: ring mine8, target layer 2 (one above)", report);
    }

    [Fact]
    public void Refuses_while_Ur_OCR_is_running()
    {
        WriteMacros();

        var code = Run(() => true, WritePulses());

        Assert.Equal(3, code);
        Assert.Empty(new TriggerStore(Triggers).Pulses);
        Assert.Contains("Close it first", File.ReadAllText(Report));
    }

    [Fact]
    public void Missing_macros_exit_2_and_write_no_pulse()
    {
        var code = Run(() => false, WritePulses());

        Assert.Equal(2, code);
        Assert.Empty(new TriggerStore(Triggers).Pulses);
        Assert.Contains("No Ur Task macro", File.ReadAllText(Report));
    }

    [Fact]
    public void A_missing_file_exits_2_and_says_why()
    {
        var code = Run(() => false, Path.Combine(_dir, "nope.json"));

        Assert.Equal(2, code);
        Assert.Contains("import failed", File.ReadAllText(Report));
    }

    [Fact]
    public void No_file_named_is_a_usage_error()
    {
        Assert.Equal(64, Run(() => false));
        Assert.Contains("usage", File.ReadAllText(Report));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~PulseImporterTests|FullyQualifiedName~PulseImportCommandTests"`
Expected: build FAILS with `CS0103: The name 'PulseImporter' does not exist in the current context`.

- [ ] **Step 3: Implement the importer**

Create `Storage/PulseImporter.cs`:

```csharp
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

    /// <summary>Every macro the loop runs, by name, exactly one each (RingImporter.ResolveMacro rules).</summary>
    public static PulseMacros Resolve(IReadOnlyList<UrTaskMacro> macros) => new(
        RingImporter.ResolveMacro(macros, PulseMacroNames.AutoMineOff),
        RingImporter.ResolveMacro(macros, PulseMacroNames.AutoMineOn),
        RingImporter.ResolveMacro(macros, PulseMacroNames.GoToTop),
        MeasuredRing.RingOrder.Select(n => RingImporter.ResolveMacro(macros, PulseMacroNames.Clear(n))).ToList());
}
```

- [ ] **Step 4: Implement the command**

In `RingImportCommand.cs`, change the last method's declaration from `private static bool OtherInstanceRunning()` to:

```csharp
    internal static bool OtherInstanceRunning()
```

Create `PulseImportCommand.cs`:

```csharp
using System.IO;
using System.Text.Json;
using RoRoRo.UrOcr.Diagnostics;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr;

/// <summary>
/// RoRoRo.UrOcr.exe --import-pulse &lt;pulse.json&gt; writes one ore stop pulse per account into
/// triggers.json and exits without opening a window. Import the ring first (--import-ring): a pulse
/// reads that ring's layers and spot triggers. Ur OCR must be closed, and Ur Task's macros must be
/// in its macro folder (the names are looked up there). The result goes to pulse-import.log in the
/// plugin data folder and to ur-ocr.log. Exit codes: 0 imported, 2 bad input, 3 Ur OCR running, 64 usage.
/// </summary>
internal static class PulseImportCommand
{
    public const string Flag = "--import-pulse";

    public static string ReportPath => Path.Combine(PluginPaths.PluginDataDir, "pulse-import.log");

    public static int Run(IReadOnlyList<string> args) =>
        Run(args, PluginPaths.TriggersFile, UrTaskMacros.MacrosDir, RingImportCommand.OtherInstanceRunning, ReportPath, DiagLog.Write);

    public static int Run(IReadOnlyList<string> args, string triggersPath, string macrosDir,
        Func<bool> otherInstanceRunning, string reportPath, Action<string> diag)
    {
        var lines = new List<string>();
        int code;
        if (args.Count != 1)
        {
            lines.Add($"usage: RoRoRo.UrOcr.exe {Flag} <pulse.json>");
            code = 64;
        }
        else if (otherInstanceRunning())
        {
            lines.Add("Ur OCR is running. Close it first: a running Ur OCR rewrites triggers.json and would undo the import.");
            code = 3;
        }
        else
        {
            try
            {
                var file = PulseFile.Load(args[0]);
                var macros = UrTaskMacros.Load(macrosDir);
                var store = new TriggerStore(triggersPath);
                if (store.CorruptedBackupPath is { } backup)
                    lines.Add($"triggers.json was unreadable; the old file is kept at {backup}");
                var pulses = PulseImporter.Apply(store, file, macros);   // builds first: a bad file writes nothing
                lines.Add($"imported {pulses.Count} pulse loop{(pulses.Count == 1 ? "" : "s")} into {triggersPath}");
                foreach (var p in pulses) lines.Add("  " + Describe(p));
                code = 0;
            }
            catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException or UnauthorizedAccessException)
            {
                lines.Add($"import failed: {ex.Message}");
                code = 2;
            }
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            File.WriteAllLines(reportPath, lines);
        }
        catch (IOException) { }
        foreach (var line in lines) diag($"pulse import: {line}");
        return code;
    }

    internal static string Describe(PulseConfig p) =>
        $"account {p.AccountUserId}: ring {p.RingId}, target layer {p.TargetLayer} " +
        $"({(p.Mode == PulseMode.OneAbove ? "one above" : "top")}), burst {p.BurstMs} ms, " +
        $"settle {p.SettleMs} ms, rock cap {p.RockCapMinutes} min{(p.Enabled ? "" : ", turned off")}";
}
```

In `Program.cs`, add after the `--import-ring` routing (before `var app = new App();`):

```csharp
        // Headless: write pulse loops into triggers.json and exit, no window.
        if (args.Length > 0 && string.Equals(args[0], PulseImportCommand.Flag, StringComparison.OrdinalIgnoreCase))
            return PulseImportCommand.Run(args.Skip(1).ToArray());
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~PulseImporterTests|FullyQualifiedName~PulseImportCommandTests|FullyQualifiedName~RingImportCommandTests"`
Expected: PASS (8 + 5 new, ring import tests unchanged).

Full suite: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj`
Expected: **336 passed**.

- [ ] **Step 6: Commit**

```powershell
git add Storage/PulseImporter.cs PulseImportCommand.cs RingImportCommand.cs Program.cs tests/RoRoRo.UrOcr.Tests/Storage/PulseImporterTests.cs tests/RoRoRo.UrOcr.Tests/PulseImportCommandTests.cs
git commit -m "feat: import pulse loops with --import-pulse" -m "One entry per account, validated against the imported ring; Auto Mine off and on, Go to Top and Clear spot N to NW are looked up by name in Ur Task. A bad file or a missing macro writes nothing." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: 0.6.0, changelog and README

**Files:**
- Modify: `rororo-ur-ocr.csproj:10`
- Modify: `manifest.json:5`
- Modify: `CHANGELOG.md` (new section at the top)
- Modify: `README.md` (new section after "Ore stop (ring triggers)")

**Interfaces:**
- Consumes: everything from Tasks 1 to 7 (names in the text must match: `--import-pulse`, `pulse-import.log`, `Clear spot N`, `Went to top: N minutes on the <layer> layer`).
- Produces: version 0.6.0 in both files; user-facing docs.

- [ ] **Step 1: Check the versions agree before the bump**

```powershell
Select-String -Path rororo-ur-ocr.csproj -Pattern "<Version>"
Select-String -Path manifest.json -Pattern '"version"'
```

Expected: `<Version>0.5.0</Version>` and `"version": "0.5.0",`. If either differs, stop and ask: they must move together.

- [ ] **Step 2: Bump both to 0.6.0**

In `rororo-ur-ocr.csproj`, change `<Version>0.5.0</Version>` to:

```xml
    <Version>0.6.0</Version>
```

In `manifest.json`, change `"version": "0.5.0",` to:

```json
  "version": "0.6.0",
```

- [ ] **Step 3: Write the changelog entry**

In `CHANGELOG.md`, insert after the `# Changelog` line and its blank line:

```markdown
## 0.6.0 — 2026-09-28

### Added

- **Ore stop pulse.** Riding and watching at once did not work in Mine #8 (effects cover the ring, and plain rock spans navy to hot magenta), so each account now pulses: Auto Mine rides for a short burst, stops, waits a second for the effects to settle, and reads the layer on that calm frame. Above your target layer it rides on. At the target it runs Ur Task's "Clear spot" macro for each of the eight ring spots, ore first (the spot furthest from the layer's rock), then the rest; Ur Task skips any spot without the white outline. A pass that clears nothing rides another burst. Past the target it presses Go to Top, and 5 minutes on the target layer without clearing a block also goes to the top, logged as `Went to top: 5 minutes on the <layer> layer` (time with the account behind or paused does not count).
- **Settings per account:** target layer (1 to 3, counting rock types down) and whether to clear on it (`top`) or on the layer above it (`oneAbove`, for an under-powered account); ride burst (2 s); pause before reading (1 s); rock cap (5 minutes).
- `RoRoRo.UrOcr.exe --import-pulse <pulse.json>` writes the pulse settings into triggers.json without opening a window, looking up Ur Task's macros by name. Import the ring first (`--import-ring`), and close Ur OCR first. The result is in `pulse-import.log`.
- Ur OCR asks Ur Task how each macro ended (`GetPlayback`), so the pulse waits for a macro to finish before its next move.
- Pulse decisions go to the activity panel and `ur-ocr.log`, each line starting `pulse <account id>:`.

### Changed

- **One owner per account and ring.** For an account with a pulse, the pulse replaces that ring's 0.5.0 triggers (the eight spots, the rock cap and the camera rule), which stand down while that account is in front. Accounts without a pulse keep the 0.5.0 ring triggers as they were.

### Notes

- The pulse acts only while its own account is the foreground alt, and never brings a window to the front. Pause all (F9) and dry run pause the pulse too.
- If a macro stops at a colour check (a popup or captcha over the Auto Mine dot, or a Clear spot that cannot see the window), if you press Esc during one of its macros, or if Ur Task closes, that account's pulse stops and says why. Restart Ur OCR to start it again.
- Needs Ur Task 0.11.0 or later (the "Clear spot" macros, the outline check and the `skipped` reason). An older Ur Task makes the pulse stop with `Unknown method 'GetPlayback'` or `No Ur Task macro is named "Clear spot N"`.
- Downgrading to 0.5.0 keeps your triggers, but 0.5.0 drops the pulse settings the next time it saves.
```

- [ ] **Step 4: Write the README section**

In `README.md`, insert after the "Ore stop (ring triggers)" section (after the line ending `as with every account-aware trigger.`) and before `## Capabilities`:

```markdown
## Ore stop pulse

Watching the ring while Auto Mine rides misses ore: effects and popups sit over the spots, and plain rock comes in colours that look like ore. The pulse stops to look instead. Per account, it rides Auto Mine for a short burst, turns it off, waits a second, and reads the layer from the ring on the still frame. At your target layer it asks Ur Task to clear each ring spot, ore first. Ur Task only holds the mouse on a block the game outlines in white (one the pickaxe can reach and break), and skips the rest. When nothing is left to clear, it rides another burst.

Set it up per account in a pulse file (one entry per account; your Roblox user id is on your profile URL):

    {
      "schema": 1,
      "pulses": [
        { "accountUserId": 123456789, "ringId": "mine8", "targetLayer": 3, "mode": "top" },
        { "accountUserId": 987654321, "ringId": "mine8", "targetLayer": 2, "mode": "oneAbove",
          "burstMs": 2000, "settleMs": 1000, "rockCapMinutes": 5 }
      ]
    }

`targetLayer` counts the ring's layers from the top. `top` clears on that layer; `oneAbove` clears on the layer above it, for an account whose pickaxe struggles there. Left out, `burstMs` is 2000, `settleMs` 1000 and `rockCapMinutes` 5. Import the ring first, then the pulse, with Ur OCR closed and Ur Task's ore stop macros installed:

    RoRoRo.UrOcr.exe --import-ring mine8.measured.json
    RoRoRo.UrOcr.exe --import-pulse pulse.json

The result is in `pulse-import.log`. An account with a pulse no longer uses the ring triggers above; every other account still does. The pulse only acts while its account's window is in front, and never switches windows for you.
```

- [ ] **Step 5: Build and run the whole suite**

```powershell
dotnet build rororo-ur-ocr.csproj
dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj
pwsh -NoProfile -File build/build-plugin.ps1
```

Expected: build succeeds; **336 passed**; the plugin build prints `Building rororo-ur-ocr v0.6.0` and ends without errors.

- [ ] **Step 6: Commit**

```powershell
git add rororo-ur-ocr.csproj manifest.json CHANGELOG.md README.md
git commit -m "chore: 0.6.0, the ore stop pulse" -m "Changelog and README for the per-account pulse loop, --import-pulse and ring ownership." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

Do not commit anything under `artifacts/` or `bin/`.

---

### Task 9: Live on Dunder-MiffLan (manual, Este present)

**Files:**
- Create: `docs/reference/ore-stop/mine8.measured.json` (the measured ring with calm-fitted layers)
- Sibling repo, only if the Ur Task half's plan says so: its `docs\reference\events\macros\space-mine-ore-stop\measured.json` outline values, committed there on its own branch
- Working folder, never committed: `%LOCALAPPDATA%\626Labs\ore-stop-sweep\2026-09-28\` (already holds the 60 ride frames, `samples.csv` and a first `mine8.measured.json` with the eight spots and empty `layers`)

**Interfaces:**
- Consumes: the 0.6.0 build and `--import-ring` / `--import-pulse` (Tasks 7 and 8); `tools/ring-sweep.ps1`, `tools/ring-sample.ps1`, `tools/ring-fit.ps1` (0.5.0); from the Ur Task pulse half: its build installed, the `Clear spot N` ... `NW` macros in its macro folder, the outline check and the `skipped` reason; Ur MCP (`list_accounts`, `run_macro`) for account ids and single macro runs.
- Produces: the committed measured ring; the outline threshold measured (numbers handed to the Ur Task half); a live-run record commit.

**Dependencies:** Tasks 1 to 8 are on `feat/ore-stop-pulse`. The Ur Task pulse half is complete on its branch in `..\rororo-ur-task` (`git -C ..\rororo-ur-task branch --show-current`) and its build is installed. Este is at Dunder-MiffLan (the 4K scale rig, "the Surface"): check its live display scale first, never assume it. Run every block from the Ur-OCR repo root.

**Shell variables do not carry between commands.** Every block starts from the sweep folder written once in Step 1:

```powershell
$sweep = (Get-Content (Join-Path $env:LOCALAPPDATA "626Labs\ore-stop-sweep\current.txt") -Raw).Trim()
```

- [ ] **Step 1: Preconditions**

```powershell
$sweep = Join-Path $env:LOCALAPPDATA "626Labs\ore-stop-sweep\2026-09-28"
Set-Content -Path (Join-Path $env:LOCALAPPDATA "626Labs\ore-stop-sweep\current.txt") -Value $sweep
Test-Path (Join-Path $sweep "mine8.measured.json")
git branch --show-current
git -C ..\rororo-ur-task branch --show-current
$client = (Get-Content "..\rororo-ur-task\docs\reference\events\macros\space-mine-ore-stop\measured.json" -Raw | ConvertFrom-Json).client
"Ur Task measured.json: $($client.w)x$($client.h) at $($client.displayScale)%"
pwsh -NoProfile -File ..\rororo-ur-task\tools\grid-capture.ps1 -OutDir $env:TEMP
$dest = Join-Path $env:LOCALAPPDATA "626Labs\RoRoRoUrTask\macros"
$names = Get-ChildItem $dest -Filter *.json | ForEach-Object { (Get-Content $_.FullName -Raw | ConvertFrom-Json).name }
$want = @('Auto Mine off (checked)', 'Auto Mine on (checked)', 'Go to Top', 'Camera top-down') + @('N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW' | ForEach-Object { "Mine spot $_"; "Clear spot $_" })
foreach ($w in $want) { "{0,-26} {1}" -f $w, @($names | Where-Object { $_ -eq $w }).Count }
```

Pass, each recorded in the session notes:
1. `True`, `feat/ore-stop-pulse`, and the Ur Task pulse branch.
2. The grid-capture line `game area WxH ... scale N%` equals Ur Task's `client` (800x599 at 100%). **If not, stop**: Este resizes the window or sets the scale, and this check runs again.
3. Every macro line ends in `1` (a `0` is missing, a `2` is a duplicate the importers refuse).
4. The main is in Mine #8 (Eclipse Rift) in Este's digging view, windowed, Auto Mine off. Ur Task is running. Ur OCR is closed (`Get-Process RoRoRo.UrOcr -ErrorAction SilentlyContinue` prints nothing).
5. The account ids: Ur MCP `list_accounts`. Write down the main's id and the alt Este picks, with the alt's target (1 or 2) and mode.

- [ ] **Step 2: Confirm the skip signal before building on it**

The loop cannot work if a skipped clear reads as a finished one (Global Constraints, "The skip signal"). Este puts the main where the N spot shows air or a block the pickaxe cannot break (no outline when hovered). Run `Clear spot N` for the main through Ur MCP `run_macro`, then:

```powershell
Select-String -Path "$env:LOCALAPPDATA\626Labs\RoRoRoUrTask\logs\ur-task.log" -Pattern "bridge playback .* 'Clear spot N'" | Select-Object -Last 3
```

Pass: the last line reads `bridge playback <id> 'Clear spot N': finished (skipped)...` (Ur Task's log prints the same state and reason `GetPlayback` returns). **A plain `finished` with no `(skipped)` fails this step, and so does `failed (check-failed)` (the pulse stops on it): stop and hand it back to the Ur Task half.** Then Este stands the main next to a breakable block at N and runs it again: pass is `finished` with no reason, and the block breaks.

- [ ] **Step 3: Measure the outline threshold (numbers for the Ur Task half)**

For each spot, Este hovers the pointer on that spot's block while it shows the white outline, and the session captures a few frames into `outline\on-<spot>`; then once with the pointer off the game area into `outline\off`. Auto Mine stays off. Per spot (repeat with `N`, `NE`, `E`, `SE`, `S`, `SW`, `W`, `NW`):

```powershell
$sweep = (Get-Content (Join-Path $env:LOCALAPPDATA "626Labs\ore-stop-sweep\current.txt") -Raw).Trim()
$spot = "N"
pwsh -NoProfile -File tools\ring-sweep.ps1 -OutDir (Join-Path $sweep "outline\on-$spot") -Seconds 1 -Minutes 0.05
```

and once:

```powershell
$sweep = (Get-Content (Join-Path $env:LOCALAPPDATA "626Labs\ore-stop-sweep\current.txt") -Raw).Trim()
pwsh -NoProfile -File tools\ring-sweep.ps1 -OutDir (Join-Path $sweep "outline\off") -Seconds 1 -Minutes 0.05
```

If a spot never shows an outline (nothing breakable there right now), Este moves the character a block and that spot is captured again; do not skip a spot. Then count near-white pixels (every channel >= 225, the spec's outline rule) in a block-sized square around each spot:

```powershell
$sweep = (Get-Content (Join-Path $env:LOCALAPPDATA "626Labs\ore-stop-sweep\current.txt") -Raw).Trim()
$half = 20        # a 41x41 square: about one block face at 800x599. Match it to the block pitch in the grid picture.
Add-Type -AssemblyName System.Drawing
$m = Get-Content (Join-Path $sweep "mine8.measured.json") -Raw | ConvertFrom-Json
function Count-White($bmp, [int]$cx, [int]$cy) {
    $n = 0
    for ($y = [math]::Max($cy - $half, 0); $y -le [math]::Min($cy + $half, $bmp.Height - 1); $y++) {
        for ($x = [math]::Max($cx - $half, 0); $x -le [math]::Min($cx + $half, $bmp.Width - 1); $x++) {
            $p = $bmp.GetPixel($x, $y)
            if ($p.R -ge 225 -and $p.G -ge 225 -and $p.B -ge 225) { $n++ }
        }
    }
    $n
}
$rows = foreach ($dir in Get-ChildItem (Join-Path $sweep "outline") -Directory) {
    foreach ($f in Get-ChildItem $dir.FullName -Filter "frame-*.png") {
        $bmp = [System.Drawing.Bitmap]::FromFile($f.FullName)
        foreach ($s in $m.spots) {
            [pscustomobject]@{ shot = $dir.Name; frame = $f.Name; spot = $s.name; white = (Count-White $bmp ([int]$s.x) ([int]$s.y)) }
        }
        $bmp.Dispose()
    }
}
$rows | Export-Csv (Join-Path $sweep "outline\counts.csv") -NoTypeInformation
$on = @($rows | Where-Object { $_.shot -eq "on-$($_.spot)" })
$off = @($rows | Where-Object { $_.shot -ne "on-$($_.spot)" })
foreach ($s in $m.spots) {
    $a = @($on | Where-Object spot -eq $s.name); $b = @($off | Where-Object spot -eq $s.name)
    "{0,-3} outlined min {1,5}   not outlined max {2,5}" -f $s.name, ($a | Measure-Object white -Minimum).Minimum, ($b | Measure-Object white -Maximum).Maximum
}
$minOn = ($on | Measure-Object white -Minimum).Minimum
$maxOff = ($off | Measure-Object white -Maximum).Maximum
if ($minOn -gt $maxOff) { "separated: outlined >= $minOn, not outlined <= $maxOff, threshold $([int][math]::Floor(($minOn + $maxOff) / 2)) at a $(2 * $half + 1)px square" }
else { "NOT SEPARATED: outlined min $minOn <= not outlined max $maxOff" }
```

Pass: `separated`, with a clear gap (the outlined minimum at least twice the not-outlined maximum). If not separated, look at the frames of the overlapping spot first: a white effect or a "New Item!" card in the square is a bad frame, re-capture it; a spot whose square takes in white UI needs a smaller `$half`, rerun with it. Hand the threshold, the square size and the per-spot lines to the Ur Task half (its measured.json names the fields; write them there only as its plan says, then regenerate and reinstall its macros and restart Ur Task). Paste the output into the session notes.

- [ ] **Step 4: Calm samples of each Mine #8 layer, and fit the rock sets**

Layer names go in the order they appear, top first: the loop's target numbers count this order.

For each layer, top first: Este rides Auto Mine down until the rock around the character is that layer's, turns Auto Mine off (`Auto Mine off (checked)`), waits a second, and the session captures:

```powershell
$sweep = (Get-Content (Join-Path $env:LOCALAPPDATA "626Labs\ore-stop-sweep\current.txt") -Raw).Trim()
pwsh -NoProfile -File tools\ring-sweep.ps1 -OutDir (Join-Path $sweep "calm") -Seconds 1 -Minutes 0.1
```

Capture two or three stops per layer at different depths, and at least one stop next to ore if Este sees one. Este names each layer as it is captured (for example `navy`, `black`, `grey`); the session writes each call-out with the frame numbers. Then sample:

```powershell
$sweep = (Get-Content (Join-Path $env:LOCALAPPDATA "626Labs\ore-stop-sweep\current.txt") -Raw).Trim()
Copy-Item (Join-Path $sweep "mine8.measured.json") (Join-Path $sweep "calm\mine8.measured.json") -Force
pwsh -NoProfile -File tools\ring-sample.ps1 -Frames (Join-Path $sweep "calm") -Measured (Join-Path $sweep "calm\mine8.measured.json")
```

Read the `calm\ring-NNNN.png` sheets and write `calm\labels.csv` (header `frame,spot,kind,layer`): one `<frame>,*,rock,<layer>` row per frame, plus a row per spot that is `ore`, `empty` (a mined-out hole, air) or `self` (the character), and `skip` for any frame with an effect or card over the ring. Then fit:

```powershell
$sweep = (Get-Content (Join-Path $env:LOCALAPPDATA "626Labs\ore-stop-sweep\current.txt") -Raw).Trim()
$calm = Join-Path $sweep "calm"
pwsh -NoProfile -File tools\ring-fit.ps1 -Samples (Join-Path $calm "samples.csv") -Labels (Join-Path $calm "labels.csv") -Measured (Join-Path $calm "mine8.measured.json")
```

Acceptance, in priority order:
1. **0 WRONG LAYER.** The pulse's every decision rests on the layer read; on calm frames it must be exact. If a layer reads as another, the rock sets overlap: lower `-Tolerance` (25, 20) and re-run; check mislabelled frames first.
2. **0 MISSED ORE** when ore is labelled. For the pulse, a missed ore only changes the clear order (every spot is tried), so "fit untested for ore" is acceptable when no ore was next to the character at any stop; say so in the notes.
3. FALSE STOP is irrelevant to the pulse (a rock spot ranked as ore only goes earlier in the pass).

When it passes, add `-Write` (and the chosen `-Tolerance`) and re-run. Confirm the layer order:

```powershell
$sweep = (Get-Content (Join-Path $env:LOCALAPPDATA "626Labs\ore-stop-sweep\current.txt") -Raw).Trim()
(Get-Content (Join-Path $sweep "calm\mine8.measured.json") -Raw | ConvertFrom-Json).layers | ForEach-Object -Begin { $i = 1 } -Process { "{0}: {1} ({2} colours)" -f $i++, $_.name, @($_.rock).Count }
```

Pass: layer 1 is the top layer Este named first, and there are as many layers as Mine #8 has rock types (3 expected). If the order is wrong, reorder the `labels.csv` rows so the top layer's frames come first and fit again.

- [ ] **Step 4b (controller ruling, Ur Task merge gate F4): the outline while held, and the reach measurement**

This gates Ur Task 0.11.0's merge. With Auto Mine off, stand the main next to a breakable block. Run one `Clear spot <name>` on that spot through Ur MCP `run_macro`, then read `ur-task.log`:
- **Pass:** "outline seen (N near-white px, needs M)" followed by "held N s, released: colour moved", and the block is visibly broken. The outline stays drawn while the button is held.
- **Fail:** "released: outline gone" within about 0.2 s of the press while the block is still there. The game hides the outline during the hold. Then the Ur Task fallback applies before merge: disable the outline-gone release during a hold, and keep the pre-press check.

Then measure the reach threshold:
1. Take the near-white counts from the "outline seen / no outline" log lines, with the pointer on a breakable block and on plain rock or out of reach.
2. Set `ring.reach.minCount` (and w/h if needed) in `..\rororo-ur-task\docs\reference\events\macros\space-mine-ore-stop\measured.json`, halfway between the two, and set `ring.reach.measuredOn`.
3. Run its `generate.ps1` and its `OreStopExampleMacrosTests`, commit there, and copy the regenerated macros into `%LOCALAPPDATA%\626Labs\RoRoRoUrTask\macros`. Restart Ur Task.

Record pass or fail for (a) and the chosen threshold in Step 11.

- [ ] **Step 5: Commit the measured ring**

```powershell
$sweep = (Get-Content (Join-Path $env:LOCALAPPDATA "626Labs\ore-stop-sweep\current.txt") -Raw).Trim()
New-Item -ItemType Directory -Force -Path docs\reference\ore-stop | Out-Null
Copy-Item (Join-Path $sweep "calm\mine8.measured.json") docs\reference\ore-stop\mine8.measured.json
git add docs/reference/ore-stop/mine8.measured.json
git commit -m "docs(ore-stop): Mine #8 ring with layers fitted from calm frames" -m "<the ring-fit summary line, the per-layer colour lines, the tolerance, the layer order, and the number of calm frames per layer>" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

Replace the angle-bracket body with the actual output from Step 4.

If Ur Task's `measured.json` `ring.spots` differ from this file's `spots` (compare the eight `x`, `y`), the Clear spot and Mine spot points are not the watched pixels: copy them across, regenerate, test and commit in the sibling repo exactly as the Ur Task half's plan describes, reinstall its macros and restart Ur Task before going on.

- [ ] **Step 6: Import the ring and the pulses**

Write the pulse file (not committed; it holds account ids). Replace the two ids and the alt's target and mode with Step 1's answers:

```powershell
$sweep = (Get-Content (Join-Path $env:LOCALAPPDATA "626Labs\ore-stop-sweep\current.txt") -Raw).Trim()
$main = 0      # the main's Roblox user id from Step 1
$alt = 0       # the alt's Roblox user id from Step 1
$altTarget = 2 # 1 or 2, Este's pick
$altMode = "top"
@{
    schema = 1
    pulses = @(
        [ordered]@{ accountUserId = $main; ringId = "mine8"; targetLayer = 3; mode = "top" },
        [ordered]@{ accountUserId = $alt; ringId = "mine8"; targetLayer = $altTarget; mode = $altMode }
    )
} | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $sweep "pulse.json") -Encoding utf8
Get-Content (Join-Path $sweep "pulse.json")
```

Then import, with Ur OCR closed:

```powershell
$sweep = (Get-Content (Join-Path $env:LOCALAPPDATA "626Labs\ore-stop-sweep\current.txt") -Raw).Trim()
Get-Process RoRoRo.UrOcr -ErrorAction SilentlyContinue      # must print nothing
dotnet build rororo-ur-ocr.csproj
$exe = (Resolve-Path "bin\Debug\net10.0-windows10.0.19041.0\RoRoRo.UrOcr.exe").Path
$ring = (Resolve-Path "docs\reference\ore-stop\mine8.measured.json").Path
(Start-Process -FilePath $exe -ArgumentList "--import-ring", "`"$ring`"" -Wait -PassThru).ExitCode
Get-Content "$env:LOCALAPPDATA\626Labs\rororo-ur-ocr\ring-import.log"
(Start-Process -FilePath $exe -ArgumentList "--import-pulse", "`"$(Join-Path $sweep 'pulse.json')`"" -Wait -PassThru).ExitCode
Get-Content "$env:LOCALAPPDATA\626Labs\rororo-ur-ocr\pulse-import.log"
```

Expected: `0`, `imported ring mine8 (Mine #8): 3 layers, 10 triggers ...`; `0`, `imported 2 pulse loops ...` and one `account <id>: ring mine8, target layer ...` line per account. Then install the 0.6.0 build the way Este installs dev builds (`pwsh ./build/build-plugin.ps1`, then RoRoRo Plugins, Install, pointed at `artifacts`) and start Ur OCR through RoRoRo. Pass: `ur-ocr.log` has `=== RoRoRo Ur OCR v0.6.0 starting`, then one `pulse <id>: started: ring mine8, target layer ...` line per account once an alt is running.

- [ ] **Step 7: Live run on the main (target 3)**

Este puts the main at the top of Mine #8 in the digging view, in front. Auto Mine off. Watch 15 to 20 minutes:

```powershell
Select-String -Path "$env:LOCALAPPDATA\626Labs\rororo-ur-ocr\logs\ur-ocr.log" -Pattern "pulse " | Select-Object -Last 40
Get-Content "$env:LOCALAPPDATA\626Labs\RoRoRoUrTask\logs\ur-task.log" -Tail 40
```

Pass criteria, each with its evidence line:
1. **Descends in pulses:** repeated `layer <top> (n of 8 spots) is above the target (<layer 3>): riding on`, then the middle layer, with `Auto Mine on (checked)` and `Auto Mine off (checked)` playbacks ending `finished` in `ur-task.log`.
2. **Stops at the target's top:** the first read on layer 3 gives `... is the target: clearing <order>`, with an ore spot first when one is next to the character (Este confirms the first cleared spot on screen is the ore).
3. **Clears and skips:** `cleared <spots>` lines, and `Clear spot` playbacks ending `finished` (held) and `finished (skipped)` (no outline) in `ur-task.log`. No hold on a block out of reach.
4. **Rides on when stuck on rock:** at least one `nothing in reach to clear (8 spots skipped): riding a burst`, followed by a ride and a new read.
5. **Past the target:** if a read lands below layer 3 (or the mine bottoms out), `is past the target ... going to top` and a `Go to Top` playback ending `finished`; the next reads are the top layer again.
6. **No stall:** Auto Mine is never off for longer than one ore's hold plus a few seconds (Este watches).

- [ ] **Step 8: Rock cap, shortened, then restored**

```powershell
$sweep = (Get-Content (Join-Path $env:LOCALAPPDATA "626Labs\ore-stop-sweep\current.txt") -Raw).Trim()
$p = Get-Content (Join-Path $sweep "pulse.json") -Raw | ConvertFrom-Json
$p.pulses[0] | Add-Member -Force -NotePropertyName rockCapMinutes -NotePropertyValue 1
$p | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $sweep "pulse.rockcap-1min.json") -Encoding utf8
```

Close Ur OCR, import `pulse.rockcap-1min.json` as in Step 6 (only the `--import-pulse` line), start Ur OCR. Este keeps the main on its target layer (layer 3) where nothing is outlined (next to cleared-out holes); the rock cap no longer counts on upper layers. Pass: within about a minute and a pulse, `pulse <main>: Went to top: 1 minute on the <layer> layer`, a `Go to Top` playback ending `finished`, and reads of the top layer after it. Then close Ur OCR, re-import `pulse.json`, confirm `rock cap 5 min` in `pulse-import.log`, and start Ur OCR.

- [ ] **Step 9: The alt, and the account gate**

Este brings the alt to the front (the main behind, idle). Pass criteria:
1. **The alt runs its own target:** `pulse <alt>: ... is the target: clearing ...` on layer `<altTarget>` (or the layer above it for `oneAbove`), not layer 3.
2. **Only the front account acts:** while the alt is in front, no `pulse <main>:` line starts a macro (the main may only log a playback it had already started), and `ur-task.log` shows playbacks for the alt only.
3. **No focus stealing:** Este switches between the two windows a few times, and between Roblox and another program, including right as a pulse is about to start a macro (just after a ride ends). The windows never jump to the front on their own (Ur Task focuses the target on every run, so a jump here means the foreground re-check before `RunMacro` missed; note the `ur-task.log` line); each account's loop picks up where it was when it comes back (a ride that ran out while behind pauses at once).
4. **Tabbing away mid-clear:** Este focuses another program during a `Clear spot` hold. `ur-task.log` shows the playback ending (foreground lost); `ur-ocr.log` shows `'Clear spot <n>' was interrupted (...); it runs again when the account is in front`, and on return that spot is cleared again.

- [ ] **Step 10: Stopping**

1. **Esc:** during a `Clear spot` hold, with the account in front, Este presses Esc. Pass: `pulse <id>: stopped: 'Clear spot <n>' ...` naming Esc or StopMacro, and nothing more for that account. Restart Ur OCR to resume.
2. **Ur Task closed:** Este closes Ur Task while the main pulses. Pass: one `pulse <main>: stopped: Ur Task is not running ...` (or `Ur Task stopped answering while ...`), no repeated lines. Este starts Ur Task, then restarts Ur OCR.
3. **The old path is untouched for an account without a pulse** (optional, if time allows): Este turns off the alt's pulse (`"enabled": false`, re-import, restart) and brings the alt to the front in Mine #8. Pass: `trigger "Mine #8: <spot>"` lines for the alt, the 0.5.0 behaviour.

- [ ] **Step 11: Record the run**

If a value changed in Steps 7 to 10 (a tolerance, a spot, the layer order), update `docs/reference/ore-stop/mine8.measured.json` and commit it with the run; if a spot moved, repeat the Step 5 sync into Ur Task. Either way, make one commit that records the live run:

```powershell
git add docs/reference/ore-stop/mine8.measured.json
git commit --allow-empty -m "docs(ore-stop): 0.6.0 pulse live run on Dunder-MiffLan" -m "<pass or fail for Steps 2, 3 and 7 to 10, each with its evidence line; the outline threshold and square handed to Ur Task; the alt's target and mode; any value changed; the descent time from the top to layer 3>" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

Replace the angle-bracket body with the observed results. A failed criterion becomes a follow-up for Este to triage; do not paper over it in the message. Note the descent time explicitly: it decides whether the pulsed descent needs its own ride setting (Decisions).

---

## Self-review

**1. Spec coverage** (`2026-09-28-ore-stop-pulse-design.md`, section by section).
- "What it does" 1, ride to the account's target, stop Auto Mine at the target, target layer 1-3 and top or one above: Task 5 (`Above_the_target_rides_on`, `At_the_target_...`, `One_above_clears_on_the_layer_above_the_target`), Task 2 (settings, `AimLayer`, validation).
- 2, pause about a second, then per spot the outline decides, order ore first then stone: Task 5 (`Rides_burstMs_then_pauses_and_settles_before_reading`, `At_the_target_clears_ore_first_...`), Task 3 (`PulseOrder`). The outline check itself is Ur Task's; Ur OCR reads its result (Task 4 `A_skip_is_reported`, Global Constraints "The skip signal"), checked live in Task 9 Step 2.
- 3, nothing outlined: burst (default 2 s), then pause and clear again: Task 5 (`A_pass_that_clears_nothing_rides_a_burst`); a pass that cleared something reads again (`A_pass_that_clears_something_reads_again_without_riding`).
- 4, past the target: Go to Top and ride down: Task 5 (`Below_the_target_goes_to_top_then_rides`); Auto Mine's own return to the top reads as the top layer and rides on (covered by `Above_the_target_rides_on`).
- 5, rock cap per layer, logged `Went to top: N minutes on the <layer> layer`: Task 5 (`Rock_cap_goes_to_top_and_says_why`, `A_cleared_block_restarts_the_rock_cap`), live in Task 9 Step 8.
- "The outline check (new in Ur Task)": out of this plan's code by the split; its threshold is measured live in Task 9 Step 3 and handed over.
- "Depth": the vote only on calm frames (Reading runs only after Pausing's settle), per-zone rock sets from calm samples in one measured file per mine (Task 9 Step 4), the ride timed not read (`burstMs`).
- "Settings per account", all four with their defaults: Task 2, Task 7 (file format and defaults), README (Task 8).
- "Not in v1": no bombs, walking or hotbar in any task.
- "Testing", Ur OCR line (states against fakes, per-account target): Tasks 4, 5, 6. Live line (outline threshold, calm samples per layer, main target 3 and one alt at 1 or 2): Task 9 Steps 3, 4, 7, 9.
- From the request: GetPlayback matching the wire contract and unknown-playback handling (Task 1, Task 4); account-aware, waits without stealing focus, busy retry, missing Ur Task stops with a line (Tasks 4, 5, 6); pulse config additive with the listed fields, importable (Tasks 2, 7); relation to the 0.5.0 ring triggers decided and implemented (Decisions, Task 6); 0.6.0 and changelog (Task 8); live task (Task 9).

**2. Placeholder scan.** Every code step carries complete code. The angle-bracket commit bodies in Task 9 (Steps 5 and 11) are live results that do not exist until the run, and each says exactly what goes there. The account ids in Task 9 Step 6 are read live in Step 1 by design (they are not committed). The Ur Task outline field names are owned by the Ur Task half's plan and named as such.

**3. Type consistency.** Checked across tasks: `GetPlaybackResponse(Ok, State, Reason, Detail, StepIndex)`, `BridgeContract.ForPlayback`, `BridgeReasons.*`, `PlaybackStates.*`, `IMacroRunClient.GetPlaybackAsync(string, CancellationToken)`; `PulseConfig(AccountUserId, RingId, TargetLayer, Mode, BurstMs, SettleMs, RockCapMinutes, Enabled, Macros)`, `.AimLayer`, `PulseMacros(AutoMineOff, AutoMineOn, GoToTop, Clear)`, `PulseMode.Top/OneAbove`, `PulseMacroNames.AutoMineOff/AutoMineOn/GoToTop/Clear(string)`, `PulseFile(Schema, Pulses)`, `PulseValidation.Validate/SpotsOf/SpotCount`, `TriggerStore.Pulses/UpsertPulse`; `SpotReading(Order, Sampled, ToleranceRgb, Ignore)`, `ISpotReader.Read(int, IReadOnlyList<Trigger>)`, `SpotReader(ICaptureSource, IWindowMetrics)`, `PulseOrder.Rank/IsOre/Nearest`; `CallStatus`, `CallResult(Status, Label, Detail)`, `MacroCall(client, target, clock, log)`, `.Begin/.StepAsync(foreground, ct, inFront?)/.Active/.Label/.RetryMs/.MaxInterruptions/.InterAltDelayMs`, `IMacroRunClient.RunAsync(macroId, targets, interAltDelayMs, ct)`, `BridgeContract.ForMacro(macroId, targets, interAltDelayMs?)`; `PulseState`, `PulseLoop(config, rings, triggers, reader, macros, clock, log)`, `.TickAsync(bool, int, CancellationToken, Func<bool>?)/.NoteBehind/.State/.StopReason/.Layer/.AccountUserId`; `IAccountLookup.TryGetUserId`, `PulseRunner(store, reader, macros, foreground, elevation, accounts, clock, log, diag)`, `.Hold/.LoopFor/.OwnsRing/.TickOnceAsync/.Start/.StopAsync`, `ActivityKind.Pulse`; `TriggerCoordinator(..., ringOwner)`; `PulseImporter.Build/Apply/Resolve`, `PulseImportCommand.Flag/Run/Describe`, `RingImportCommand.OtherInstanceRunning`. Test fakes: `PulseClock.Advance`, `ScriptedMacros.Runs/RunIds/Polls/RunReplies/Script/Refusal` and its static replies (Task 4) are what Tasks 5 and 6 use; `PulseFixtures` and `ScriptedReader` (Task 5) are what Task 6 uses. The log strings asserted in tests (`started`, `above the target`, `past the target`, `is the target: clearing`, `nothing in reach to clear`, `cleared N NE ...`, `Went to top: 5 minutes on the grey layer`, `no layer on a calm frame`, `could not read the ring`, `could not check`, `no longer knows`, `stopped: Ur Task is not running`, `popup or captcha`, `stopped in Ur Task`, `Esc`, `interrupted`) match the strings in `PulseLoop` and `MacroCall`.

**4. Review Focus.** Five lines, each pinned by named tests in its owning task: Esc in front (Task 4 `Aborted_while_in_front_stops`, Task 5 `A_playback_stopped_in_Ur_Task_stops_the_loop`); tabbing away (Task 5 `Waits_while_the_account_is_not_in_front`, Task 6 `Only_the_account_in_front_acts`); a popup over the dot or a clear whose check could not run (Task 5 `A_failed_check_on_Auto_Mine_off_stops_the_loop`, `A_failed_check_on_a_clear_stops_the_loop`); Ur Task restarted (Task 4 `A_lost_playback_is_reported`, Task 5 `A_lost_playback_stops_the_loop`); Ur Task closed or too old (Task 4 `Missing_Ur_Task_stops`, `An_Ur_Task_without_GetPlayback_stops`, Task 5 `Missing_Ur_Task_stops_with_a_log_line`). Two paths on one ring: Task 6 `A_ring_owned_by_a_pulse_loop_does_not_fire`.

**Test counts** (baseline 227): Task 1 +9 = 236; Task 2 +26 = 262; Task 3 +8 = 270; Task 4 +20 = 290; Task 5 +22 = 312; Task 6 +11 = 323; Task 7 +13 = 336. Theory rows count as tests. Verified before handoff: every code block in Tasks 1 to 7 was applied to a scratch copy of `feat/ore-stop` at `05000ee`; the app built with 0 errors and the suite ran 331 passed, 0 failed. After the preflight rulings (F1, F3 to F8; F2 is fixed in the Ur Task half), the blocks were re-applied to a scratch copy of `b1357c5` and the app and test project build with 0 errors; the 336 count is traced by hand, not yet run.
