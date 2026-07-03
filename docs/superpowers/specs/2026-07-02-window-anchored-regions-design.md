# Window-anchored trigger regions — design

**Date:** 2026-07-02
**Status:** Approved (Este, this session)
**Target version:** Ur-OCR v0.4.0 (schema-changing feature — parallels Ur Task v0.4.0 window awareness)
**Repo:** Ur-OCR

## Problem

Trigger regions are absolute screen coordinates (`Trigger.Region = RegionRect(X, Y, Width, Height)` in screen pixels; `TriggerCoordinator` captures at those coords). Move the Roblox window and the region keeps watching the old screen spot — the game UI moved, so the trigger never matches. Observed live during the bridge test: the loop only fired after re-picking the region at the window's new position.

This is the Ur-OCR counterpart to the Ur Task v0.4.0 window-relative-coordinates work, and the two must **feel like a family** — same vocabulary, stored fields, module shapes, and migration pattern — so a developer moving between the repos recognizes the pattern.

## The shared family goal

*Watch (Ur-OCR) or act on (Ur Task) the same relative spot inside the alt window, wherever the window sits and whatever its size.* The two plugins reach it differently because one **drives** and one **watches**:

- **Ur Task** resizes the target window to the recorded client size, then replays client-relative coordinates.
- **Ur-OCR** cannot resize a window it is only watching — so it **scales** the region to the current client size instead.

Same goal, role-appropriate mechanism. The size-aware scaling is Este's chosen behavior (over fixed-offset) and is the correct fit for a passive watcher.

## Decisions (made with Este)

| Decision | Choice |
|---|---|
| Multi-alt behavior | **Follow the foreground alt** — resolve against whichever RoRoRo alt is in front each tick, so the same relative region is watched on each alt (round-robin). Skip when the foreground isn't an alt. |
| Default for new triggers | **Window-anchored (`client`)** when a region is picked over / against an alt; `screen` only when no alt is involved. Existing triggers load as `screen` (non-breaking). |
| Size mismatch | **Size-aware** — scale the region by `current client size / recorded client size`. |

## Family surface (shared with Ur Task, by name)

- **Vocabulary:** `Trigger.CoordSpace` = `"screen"` | `"client"`; constants `Trigger.CoordSpaceScreen` / `Trigger.CoordSpaceClient`; `Trigger.IsClientSpace` — identical shape to `Macro.CoordSpace` / `IsClientSpace` in Ur Task.
- **Stored fields:** the region as client-relative `X, Y, Width, Height` **plus `RecordedClientW` / `RecordedClientH`** — the same fields Ur Task's macro carries. Ur Task uses them to resize; Ur-OCR uses them to scale. Same data, different verb.
- **Modules:** a pure `WindowSpaceMath` (mirrors Ur Task's) and an `IWindowMetrics` + `WindowMetrics` seam (a subset — `HwndForPid`, `ClientOrigin`, `ClientSize`), same thin-Win32-wrapper convention.
- **Migration:** schema-version bump, legacy loads as `screen`, sticky on save — mirrors `MacroV1Migrator`.
- **Gating:** `client`-space + anchor-resolved, else skip — mirrors the Ur Task player/recorder gates.

## Approaches considered

- **A. Client-relative region + size-aware scaling (chosen).** Store the region relative to the alt's client origin at the recorded client size; scale to the current client size at eval. Family-consistent (shares fields/vocabulary with Ur Task), size-aware.
- **B. Fixed offset from a corner, re-pick on resize.** Simpler, matches Ur Task's *math* exactly (no scale). Rejected — Este chose size-aware; Roblox's scale-based UI tracks better under scaling.
- **C. Resize the alt window to the recorded size (Ur Task's literal mechanism).** Rejected — Ur-OCR is a passive watcher; silently resizing the user's game window every tick is unacceptable.

## Design

### 1. Schema v2 (`Trigger` + `TriggersFile`)

`Trigger` gains:

- `CoordSpace` (`string?`, `"screen"` | `"client"`; null treated as `"screen"`), constants `CoordSpaceScreen` / `CoordSpaceClient`, and `IsClientSpace`.
- `RecordedClientW` / `RecordedClientH` (`int?`), present only when `client`. `[JsonIgnore(WhenWritingNull)]`.

For `client` triggers, `Region` holds **client-relative** coordinates (offset from the anchor window's client origin) at the recorded client size. For `screen` triggers, `Region` is absolute (unchanged).

`TriggersFile.SchemaVersion` 1 → 2. On load, a trigger with schemaVersion 1 (or missing `coordSpace`) gets `CoordSpace = "screen"`; sticky on next save. JSON stays camelCase + `JsonStringEnumConverter` (existing `TriggerJsonOptions`).

### 2. Pure math — `WindowSpaceMath`

```csharp
public static class WindowSpaceMath
{
    // Pick: screen region → client-relative region (subtract origin).
    public static RegionRect ToClientRegion(RegionRect screen, (int X, int Y) clientOrigin);

    // Eval: client-relative region → absolute screen region, scaled by
    // current/recorded client size, then offset by the current client origin.
    public static RegionRect ToScreenRegion(
        RegionRect client, (int X, int Y) clientOrigin,
        (int W, int H) recordedClient, (int W, int H) currentClient);
}
```

`ToScreenRegion` scale: `sx = currentClient.W / recordedClient.W`, `sy = currentClient.H / recordedClient.H`; `absX = clientOrigin.X + round(client.X·sx)`, likewise Y/W/H. Guards `recordedClient` W/H ≤ 0 (returns the un-scaled offset). Pure, no Win32.

### 3. Window metrics seam — `IWindowMetrics` / `WindowMetrics`

```csharp
public interface IWindowMetrics
{
    IntPtr HwndForPid(int pid);            // IntPtr.Zero when unresolvable
    (int X, int Y)? ClientOrigin(IntPtr hwnd);
    (int W, int H)? ClientSize(IntPtr hwnd);
}
```

`WindowMetrics` = thin Win32 (`Process.GetProcessById(pid).MainWindowHandle`, `ClientToScreen`, `GetClientRect`), same convention as Ur Task's `WindowMetrics` and Ur-OCR's existing `ForegroundWatcher` interop. No unit tests (thin wrapper); consumers fake the interface.

### 4. Evaluation — `TriggerCoordinator`

Add an `IWindowMetrics` collaborator. Per `client`-space trigger, before capture:

1. Resolve the foreground pid (`IForegroundCheck.GetForegroundPid()`) and confirm it's an alt (`IsForegroundAnAlt()`). If not, skip this trigger — a `client` region has no anchor without a foreground alt. This makes `client` triggers implicitly account-aware **regardless of the `AccountAware` flag** (which continues to govern `screen` triggers only). Do this check for `client` triggers even when `AccountAware` is false, so the anchor is never resolved against a non-alt window.
2. `hwnd = metrics.HwndForPid(pid)`; `origin = metrics.ClientOrigin(hwnd)`; `size = metrics.ClientSize(hwnd)`. Any null → log `SkippedNotAlt`-style skip (window gone), continue.
3. `absRegion = WindowSpaceMath.ToScreenRegion(trig.Region, origin, (RecordedClientW, RecordedClientH), size)`.
4. `capture.Capture(absRegion)` — the rest of the tick (color/text match, cooldown, fire) is unchanged.

`screen`-space triggers take today's path byte-for-byte (`capture.Capture(trig.Region)`), metrics untouched.

Note: `client` triggers require an alt in the foreground to resolve the anchor, so they are effectively account-aware regardless of the `AccountAware` flag. Keep `AccountAware` honored for `screen` triggers; for `client` triggers the anchor resolution supersedes it (document in the UI).

### 5. Pick time — anchor detection

When a region is picked (`RegionPickerOverlay` in the add-trigger and re-pick flows):

1. Capture the foreground window handle **before** showing the overlay (the overlay itself becomes foreground).
2. Determine the anchor window: the alt window whose client rect contains the picked region's center — enumerate alt pids (`AccountRegistry`) → `metrics.HwndForPid` → client rect, hit-test; else the pre-overlay foreground alt; else none.
3. If an anchor alt resolved: `CoordSpace = "client"`, `Region = WindowSpaceMath.ToClientRegion(pickedScreenRegion, anchorClientOrigin)`, `RecordedClientW/H = anchorClientSize`.
4. No alt: `CoordSpace = "screen"`, `Region` = the picked absolute rect (today's behavior).

Re-pick region re-runs this, re-anchoring against the current window.

### 6. Preview (live match meter)

`PreviewEvaluator` resolves the anchor the same way (needs `IWindowMetrics` + the foreground pid) so the live meter samples the anchored region and tracks as the window moves. For `client` triggers with no alt foreground, the meter shows the existing no-signal state rather than sampling a stale absolute rect.

### 7. UI

- The REGION line shows the mode: `Window` for `client` (optionally the recorded client size), `Screen: X,Y W×H` for `screen`.
- A small per-trigger toggle to switch modes (switching to `Window` re-anchors via a re-pick prompt; switching to `Screen` freezes the current absolute rect).
- New triggers default to `Window` when an alt anchor resolves at pick time.

### 8. DPI / multi-monitor

Client-relative coordinates + scaling are inherently DPI- and monitor-robust (everything is relative to the alt window). `DpiGuard`'s screen-region drift warnings apply only to `screen` triggers now; `client` triggers are exempt (they self-correct). No app-manifest change required beyond what ships today.

## Testing (mirrors Ur Task)

- **`WindowSpaceMath`** (pure): `ToClientRegion` subtracts origin; `ToScreenRegion` round-trips at same size, scales up/down (window resized), offsets (window moved), and both; zero/negative recorded size guarded.
- **`TriggerCoordinator`** with a fake `IWindowMetrics`: `client` trigger with window moved → captures the correct absolute rect; window resized → scaled rect; window gone (null origin/size) → skips, no capture; `screen` trigger → metrics never touched.
- **Schema v1 → v2 migration**: legacy trigger loads as `screen`, region untouched; sticky on save; `client` trigger round-trips `CoordSpace` + `RecordedClientW/H` with null-omission for `screen`.
- **Pick-time anchor selection** (pure helper over a fake alt-window provider): region centered over an alt → `client` + correct client-relative offset + recorded size; region over no alt → `screen` + absolute rect.
- Win32 (`WindowMetrics`, `RegionPickerOverlay`) stays thin/untested, verified by build + the human checklist.

## Out of scope

- Text (OCR) triggers use the same region path — no OCR-specific work; a region is a region in either mode.
- Per-alt independent anchoring (a trigger pinned to one specific alt) — rejected in favor of follow-the-foreground-alt.
- Corner-aware anchoring (top-left vs bottom-right) — v1 scales from the client origin; revisit only if drift is reported.

## Version & release

Ur-OCR v0.4.0 (schema v2 — parallels Ur Task v0.4.0 window awareness). Batches with the already-merged theming/cooldown/picker/toast fixes into one rc. No new plugin capabilities (window geometry read via the same process-level Win32 the plugin already uses); host requirement stays RoRoRo v1.4.3.0+.

## Human verification (live alts, before final)

1. Pick a color region over an alt's UI; move that alt's window — trigger still matches (no re-pick).
2. Resize the alt window — region scales and still matches.
3. Cycle foreground between two alts — the same relative region is watched on each.
4. A pre-v0.4 (screen) trigger still behaves exactly as before.
5. Pick a region over a non-alt window — it stays screen-anchored.
