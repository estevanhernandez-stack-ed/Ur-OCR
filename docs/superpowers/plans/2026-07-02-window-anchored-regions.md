# Window-Anchored Trigger Regions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Trigger regions anchor to the foreground alt's client area and scale with its size, so color/text detection tracks the Roblox window wherever it moves and watches the same relative spot on whichever alt is in front.

**Architecture:** Mirror Ur Task's v0.4.0 window awareness. `Trigger` gains a `CoordSpace` (`screen`|`client`) + `RecordedClientW/H`; a pure `WindowSpaceMath` maps screen↔client regions with scaling; an `IWindowMetrics`/`WindowMetrics` seam reads window geometry; a pure `TriggerRegionResolver` turns a trigger + anchor pid into the absolute rect to capture; the coordinator and preview both route through it. Pick-time, a pure `TriggerAnchor` decides screen-vs-client from the picked rect and the alt windows.

**Tech Stack:** C# / .NET 10 WPF (`net10.0-windows10.0.19041.0`), Win32 P/Invoke, System.Text.Json, xUnit.

**Spec:** `docs/superpowers/specs/2026-07-02-window-anchored-regions-design.md`

## Global Constraints

- Target version Ur-OCR **v0.4.0**: bump `manifest.json` `"version"` 0.3.0 → 0.4.0 (Task 9). (There is no `<Version>` in the csproj; the build script reads the manifest — verify in Task 9.)
- Coord-space strings are exactly `"screen"` and `"client"`, constants on `Trigger` (`CoordSpaceScreen`/`CoordSpaceClient`), mirroring `Macro.CoordSpace` in Ur Task.
- `TriggersFile.SchemaVersion` 1 → 2. Legacy triggers (schemaVersion 1 or missing `coordSpace`) load as `"screen"`, region untouched; migration is sticky (persisted on next write).
- JSON stays camelCase + `JsonStringEnumConverter` (existing `TriggerJsonOptions.Default`). New nullable fields (`RecordedClientW`/`RecordedClientH`) omitted-when-null via `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`.
- `client` triggers are implicitly account-aware: they resolve against the foreground alt and skip when the foreground isn't an alt, **regardless of `AccountAware`**. `AccountAware` continues to govern `screen` triggers only.
- Coordinates are device pixels in the same space the existing picker/capture use (`RegionPickerOverlay` multiplies by DPI scale; `CaptureEngine` captures at those coords). `IWindowMetrics` must return `ClientOrigin`/`ClientSize` in that same device-pixel space — verify against the capture path during the human checklist.
- **Preview refinement (extends spec §6):** the live match meter anchors `client` triggers to the **first running alt** (not the current foreground, which is the Ur-OCR editor while you're editing), so the meter works during setup. Eval still follows the *foreground* alt. This is the one deliberate spec extension; called out for the plan-review gate.
- All work on branch `feat/window-anchored-regions` off `main`. TDD: failing test first, watch it fail, minimal implementation, watch it pass. No test may capture the real screen or touch real windows — pure math and fake `IWindowMetrics` only.
- Test command: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --nologo` (the unit project; NOT `RoRoRo.UrOcr.IntegrationTests`, which couples to the ROROROblox sibling repo and doesn't build standalone).
- No new plugin capabilities — window geometry uses the same process-level Win32 the plugin already calls.

## File Structure

| File | Role |
|---|---|
| Modify `Storage/Trigger.cs` | `CoordSpace` + constants + `IsClientSpace`, `RecordedClientW/H` |
| Modify `Storage/TriggerStore.cs` | v1→v2 migration on load, sticky |
| Create `Engine/WindowSpaceMath.cs` | Pure screen↔client region math + scaling |
| Create `PluginHost/IWindowMetrics.cs` | Window geometry seam |
| Create `PluginHost/WindowMetrics.cs` | Thin Win32 implementation |
| Modify `PluginHost/AccountRegistry.cs` | Expose alt pids |
| Create `Engine/TriggerRegionResolver.cs` | Pure: trigger + anchor pid → absolute region |
| Create `Engine/TriggerAnchor.cs` | Pure: picked screen region + alts → (CoordSpace, Region, RecordedClientW/H) |
| Modify `Engine/TriggerCoordinator.cs` | Route capture through the resolver |
| Modify `Engine/PreviewEvaluator.cs` | Resolve anchor for the live meter |
| Modify `UI/TriggerRowViewModel.cs` | Preview calls `EvaluateTrigger`; region-mode display |
| Modify `UI/MainViewModel.cs` | Pick-time anchoring on add |
| Modify `UI/TriggerEditView.xaml` (+ `.cs` if needed) | Re-pick anchoring; region-mode line + toggle |
| Modify `PluginRuntime.cs` | Wire `WindowMetrics` into coordinator + preview |
| Modify `manifest.json`, `CHANGELOG.md`, `README.md` | v0.4.0 + docs |
| Test files | `TriggerMigrationV2Tests.cs`, `WindowSpaceMathTests.cs`, `TriggerRegionResolverTests.cs`, `TriggerAnchorTests.cs`, `TriggerCoordinatorWindowTests.cs` |

---

### Task 1: Schema v2 — `CoordSpace` + `RecordedClientW/H` + migration

**Files:**
- Modify: `Storage/Trigger.cs`
- Modify: `Storage/TriggerStore.cs`
- Test: `tests/RoRoRo.UrOcr.Tests/Storage/TriggerMigrationV2Tests.cs`

**Interfaces:**
- Produces: `Trigger.CoordSpace` (`string?`), `Trigger.CoordSpaceScreen`/`CoordSpaceClient` (const), `Trigger.IsClientSpace` (bool), `Trigger.RecordedClientW`/`RecordedClientH` (`int?`). `TriggersFile.SchemaVersion == 2` after load-migration.

- [ ] **Step 1: Create the branch**

```bash
cd /c/Users/estev/Projects/Ur-OCR && git checkout main && git pull --ff-only origin main && git checkout -b feat/window-anchored-regions
```

- [ ] **Step 2: Write the failing tests**

Create `tests/RoRoRo.UrOcr.Tests/Storage/TriggerMigrationV2Tests.cs`:

```csharp
using System.IO;
using System.Text.Json;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Tests.Storage;

public class TriggerMigrationV2Tests
{
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "urocr-tests", System.Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void LegacyV1Triggers_LoadAsScreen_AndBumpSchema()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // A v1 file: no schemaVersion default is 1, triggers have no coordSpace.
        File.WriteAllText(path, """
        {
          "schemaVersion": 1,
          "triggers": [
            { "id": "11111111-1111-1111-1111-111111111111", "name": "t",
              "enabled": true, "region": { "x": 10, "y": 20, "width": 30, "height": 40 },
              "mode": "color", "accountAware": true,
              "keybind": { "key": "F", "modifiers": [] } }
          ]
        }
        """);
        var store = new TriggerStore(path);
        var t = Assert.Single(store.All);
        Assert.Equal(Trigger.CoordSpaceScreen, t.CoordSpace);
        Assert.False(t.IsClientSpace);
        Assert.Null(t.RecordedClientW);
        Assert.Equal(10, t.Region.X); // region untouched

        // Sticky: the file now serializes at schemaVersion 2 with coordSpace.
        var reread = File.ReadAllText(path);
        Assert.Contains("\"schemaVersion\": 2", reread);
        Assert.Contains("\"coordSpace\": \"screen\"", reread);
    }

    [Fact]
    public void ClientTrigger_RoundTrips_WithRecordedClientSize()
    {
        var path = TempFile();
        var store = new TriggerStore(path);
        store.Add(new Trigger
        {
            Id = System.Guid.NewGuid(),
            Name = "c",
            Region = new RegionRect(5, 6, 7, 8),
            Mode = TriggerMode.Color,
            Keybind = new KeyCombo("F", System.Array.Empty<string>()),
            CoordSpace = Trigger.CoordSpaceClient,
            RecordedClientW = 816,
            RecordedClientH = 638,
        });

        var reloaded = new TriggerStore(path);
        var t = Assert.Single(reloaded.All);
        Assert.True(t.IsClientSpace);
        Assert.Equal(816, t.RecordedClientW);
        Assert.Equal(638, t.RecordedClientH);
    }

    [Fact]
    public void ScreenTrigger_OmitsRecordedClientSize_InJson()
    {
        var t = new Trigger
        {
            Id = System.Guid.NewGuid(), Name = "s",
            Region = new RegionRect(1, 2, 3, 4), Mode = TriggerMode.Color,
            Keybind = new KeyCombo("F", System.Array.Empty<string>()),
            CoordSpace = Trigger.CoordSpaceScreen,
        };
        var json = JsonSerializer.Serialize(t, TriggerJsonOptions.Default);
        Assert.DoesNotContain("recordedClientW", json);
        Assert.DoesNotContain("recordedClientH", json);
    }
}
```

Note: `TriggerJsonOptions` is `internal` — the test project already sees internals (existing tests use `TriggerStore`/`Trigger`); if `TriggerJsonOptions` isn't visible, make the assertion via `store` round-trip instead of direct serialize, or confirm `InternalsVisibleTo` is set (it is for the unit project — check `rororo-ur-ocr.csproj`).

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~TriggerMigrationV2Tests" --nologo`
Expected: compile FAILURE — `Trigger` has no `CoordSpace`/`CoordSpaceScreen`/`IsClientSpace`/`RecordedClientW`.

- [ ] **Step 4: Add the schema fields to `Trigger`**

In `Storage/Trigger.cs`, inside the `Trigger` class add (after the `Keybind` property, before `MacroId`):

```csharp
    // Window-anchoring (schema v2). "screen" = absolute pixels (legacy);
    // "client" = Region is relative to the foreground alt's client area at the
    // recorded client size, scaled to the live size at eval. Mirrors
    // Macro.CoordSpace in Ur Task.
    public string? CoordSpace { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? RecordedClientW { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? RecordedClientH { get; set; }

    public const string CoordSpaceScreen = "screen";
    public const string CoordSpaceClient = "client";
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsClientSpace =>
        string.Equals(CoordSpace, CoordSpaceClient, System.StringComparison.OrdinalIgnoreCase);
```

- [ ] **Step 5: Migrate on load in `TriggerStore`**

In `Storage/TriggerStore.cs`, replace the `Load()` method body's success path so that after deserialize it migrates and re-persists when needed. Replace:

```csharp
            _state = JsonSerializer.Deserialize<TriggersFile>(json, TriggerJsonOptions.Default)
                     ?? new TriggersFile();
```

with:

```csharp
            _state = JsonSerializer.Deserialize<TriggersFile>(json, TriggerJsonOptions.Default)
                     ?? new TriggersFile();
            if (MigrateToV2()) WriteNow(); // sticky
```

Then add the method (below `Load()`):

```csharp
    // v1 → v2: triggers with no coordSpace are absolute-screen. Returns true if
    // anything changed (so the caller persists the migration).
    private bool MigrateToV2()
    {
        var changed = false;
        if (_state.SchemaVersion < 2) { _state.SchemaVersion = 2; changed = true; }
        foreach (var t in _state.Triggers)
        {
            if (string.IsNullOrEmpty(t.CoordSpace)) { t.CoordSpace = Trigger.CoordSpaceScreen; changed = true; }
        }
        return changed;
    }
```

`TriggersFile.SchemaVersion` already exists and defaults to 1; new files written by `WriteNow` will carry whatever `_state.SchemaVersion` holds — bump the default to 2 for fresh stores: in `Storage/Trigger.cs` change `public int SchemaVersion { get; set; } = 1;` to `= 2;`.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~TriggerMigrationV2Tests" --nologo`
Expected: PASS (3 tests).

- [ ] **Step 7: Commit**

```bash
git add Storage/Trigger.cs Storage/TriggerStore.cs tests/RoRoRo.UrOcr.Tests/Storage/TriggerMigrationV2Tests.cs
git commit -m "feat(schema): trigger v2 — CoordSpace + recorded client size + migration"
```

---

### Task 2: Pure `WindowSpaceMath`

**Files:**
- Create: `Engine/WindowSpaceMath.cs`
- Test: `tests/RoRoRo.UrOcr.Tests/Engine/WindowSpaceMathTests.cs`

**Interfaces:**
- Consumes: `RegionRect` (Storage).
- Produces: `WindowSpaceMath.ToClientRegion(RegionRect screen, (int X, int Y) clientOrigin) → RegionRect`; `WindowSpaceMath.ToScreenRegion(RegionRect client, (int X, int Y) clientOrigin, (int W, int H) recordedClient, (int W, int H) currentClient) → RegionRect`.

- [ ] **Step 1: Write the failing tests**

Create `tests/RoRoRo.UrOcr.Tests/Engine/WindowSpaceMathTests.cs`:

```csharp
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Tests.Engine;

public class WindowSpaceMathTests
{
    [Fact]
    public void ToClientRegion_SubtractsOrigin()
    {
        var r = WindowSpaceMath.ToClientRegion(new RegionRect(150, 260, 30, 40), (100, 200));
        Assert.Equal(new RegionRect(50, 60, 30, 40), r);
    }

    [Fact]
    public void ToScreenRegion_SameSize_AddsOrigin()
    {
        var r = WindowSpaceMath.ToScreenRegion(new RegionRect(50, 60, 30, 40), (100, 200), (800, 600), (800, 600));
        Assert.Equal(new RegionRect(150, 260, 30, 40), r);
    }

    [Fact]
    public void ToScreenRegion_ScalesUp_WhenWindowLarger()
    {
        // Recorded 800x600, now 1600x1200 (2x). Client region (50,60,30,40) doubles.
        var r = WindowSpaceMath.ToScreenRegion(new RegionRect(50, 60, 30, 40), (0, 0), (800, 600), (1600, 1200));
        Assert.Equal(new RegionRect(100, 120, 60, 80), r);
    }

    [Fact]
    public void ToScreenRegion_ScalesDown_AndOffsets()
    {
        // Recorded 800x600, now 400x300 (0.5x), origin (10,20).
        var r = WindowSpaceMath.ToScreenRegion(new RegionRect(50, 60, 30, 40), (10, 20), (800, 600), (400, 300));
        Assert.Equal(new RegionRect(10 + 25, 20 + 30, 15, 20), r);
    }

    [Fact]
    public void ToScreenRegion_ZeroRecordedSize_FallsBackToOffsetOnly()
    {
        // Guard div-by-zero: no scale, just offset.
        var r = WindowSpaceMath.ToScreenRegion(new RegionRect(50, 60, 30, 40), (10, 20), (0, 0), (400, 300));
        Assert.Equal(new RegionRect(60, 80, 30, 40), r);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~WindowSpaceMathTests" --nologo`
Expected: compile FAILURE — `WindowSpaceMath` does not exist.

- [ ] **Step 3: Implement**

Create `Engine/WindowSpaceMath.cs`:

```csharp
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

/// <summary>
/// Pure screen↔client region mapping for window-anchored triggers. Mirrors Ur
/// Task's WindowSpaceMath. No Win32 — callers supply origins/sizes from
/// IWindowMetrics so the math is unit-testable.
/// </summary>
public static class WindowSpaceMath
{
    /// <summary>Absolute screen region → client-relative region (subtract origin).</summary>
    public static RegionRect ToClientRegion(RegionRect screen, (int X, int Y) clientOrigin)
        => new(screen.X - clientOrigin.X, screen.Y - clientOrigin.Y, screen.Width, screen.Height);

    /// <summary>
    /// Client-relative region → absolute screen region, scaled by
    /// current/recorded client size then offset by the current client origin.
    /// Ur Task resizes the window to fit; Ur-OCR (a watcher) scales the region.
    /// </summary>
    public static RegionRect ToScreenRegion(
        RegionRect client, (int X, int Y) clientOrigin,
        (int W, int H) recordedClient, (int W, int H) currentClient)
    {
        double sx = recordedClient.W > 0 ? (double)currentClient.W / recordedClient.W : 1.0;
        double sy = recordedClient.H > 0 ? (double)currentClient.H / recordedClient.H : 1.0;
        return new RegionRect(
            clientOrigin.X + (int)System.Math.Round(client.X * sx),
            clientOrigin.Y + (int)System.Math.Round(client.Y * sy),
            (int)System.Math.Round(client.Width * sx),
            (int)System.Math.Round(client.Height * sy));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~WindowSpaceMathTests" --nologo`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add Engine/WindowSpaceMath.cs tests/RoRoRo.UrOcr.Tests/Engine/WindowSpaceMathTests.cs
git commit -m "feat(engine): pure WindowSpaceMath — screen<->client region + scaling"
```

---

### Task 3: `IWindowMetrics` + `WindowMetrics` (Win32 seam)

**Files:**
- Create: `PluginHost/IWindowMetrics.cs`
- Create: `PluginHost/WindowMetrics.cs`

**Interfaces:**
- Produces (Tasks 4-7 depend on these exactly):

```csharp
namespace RoRoRo.UrOcr.PluginHost;
public interface IWindowMetrics
{
    System.IntPtr HwndForPid(int pid);              // IntPtr.Zero when unresolvable
    (int X, int Y)? ClientOrigin(System.IntPtr hwnd);
    (int W, int H)? ClientSize(System.IntPtr hwnd);
}
```

No unit tests (thin Win32 wrapper, same convention as `ForegroundWatcher`); build is the gate.

- [ ] **Step 1: Create the interface**

Create `PluginHost/IWindowMetrics.cs`:

```csharp
namespace RoRoRo.UrOcr.PluginHost;

/// <summary>
/// Window geometry seam for window-anchored trigger regions. Device pixels,
/// matching the capture/picker coordinate space. Null returns = window gone /
/// call failed; callers skip, never crash. Mirrors Ur Task's IWindowMetrics
/// (subset — Ur-OCR only reads, never resizes).
/// </summary>
public interface IWindowMetrics
{
    System.IntPtr HwndForPid(int pid);
    (int X, int Y)? ClientOrigin(System.IntPtr hwnd);
    (int W, int H)? ClientSize(System.IntPtr hwnd);
}
```

- [ ] **Step 2: Create the Win32 implementation**

Create `PluginHost/WindowMetrics.cs`:

```csharp
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RoRoRo.UrOcr.PluginHost;

/// <summary>Thin Win32 IWindowMetrics — marshalling only, no logic.</summary>
public sealed class WindowMetrics : IWindowMetrics
{
    public IntPtr HwndForPid(int pid)
    {
        try { return Process.GetProcessById(pid).MainWindowHandle; }
        catch { return IntPtr.Zero; }
    }

    public (int X, int Y)? ClientOrigin(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        var pt = new POINT { x = 0, y = 0 };
        return ClientToScreen(hwnd, ref pt) ? (pt.x, pt.y) : null;
    }

    public (int W, int H)? ClientSize(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        return GetClientRect(hwnd, out var r) ? (r.right - r.left, r.bottom - r.top) : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left; public int top; public int right; public int bottom; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
}
```

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build rororo-ur-ocr.csproj --nologo`
Expected: Build succeeded (pre-existing CA1416 warnings in TextMatcher.cs are OK).

- [ ] **Step 4: Commit**

```bash
git add PluginHost/IWindowMetrics.cs PluginHost/WindowMetrics.cs
git commit -m "feat(pluginhost): IWindowMetrics seam + thin Win32 implementation"
```

---

### Task 4: `TriggerRegionResolver` (pure) + expose alt pids

**Files:**
- Create: `Engine/TriggerRegionResolver.cs`
- Modify: `PluginHost/AccountRegistry.cs`
- Test: `tests/RoRoRo.UrOcr.Tests/Engine/TriggerRegionResolverTests.cs`

**Interfaces:**
- Consumes: `WindowSpaceMath` (Task 2), `IWindowMetrics` (Task 3), `Trigger` (Task 1).
- Produces: `TriggerRegionResolver.Resolve(Trigger trig, int anchorPid, IWindowMetrics metrics) → RegionRect?` (null = client trigger whose anchor can't be resolved). `AccountRegistry.Pids` → `IReadOnlyCollection<int>`.

- [ ] **Step 1: Write the failing tests**

Create `tests/RoRoRo.UrOcr.Tests/Engine/TriggerRegionResolverTests.cs`:

```csharp
using System;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Tests.Engine;

public class TriggerRegionResolverTests
{
    private sealed class FakeMetrics : IWindowMetrics
    {
        public IntPtr Hwnd = new(0x10);
        public (int X, int Y)? Origin = (0, 0);
        public (int W, int H)? Size = (800, 600);
        public IntPtr HwndForPid(int pid) => pid == 0 ? IntPtr.Zero : Hwnd;
        public (int X, int Y)? ClientOrigin(IntPtr h) => h == IntPtr.Zero ? null : Origin;
        public (int W, int H)? ClientSize(IntPtr h) => h == IntPtr.Zero ? null : Size;
    }

    private static Trigger Screen() => new()
    {
        Id = Guid.NewGuid(), Name = "s", Region = new RegionRect(10, 20, 30, 40),
        Mode = TriggerMode.Color, Keybind = new KeyCombo("F", Array.Empty<string>()),
        CoordSpace = Trigger.CoordSpaceScreen,
    };

    private static Trigger Client() => new()
    {
        Id = Guid.NewGuid(), Name = "c", Region = new RegionRect(50, 60, 30, 40),
        Mode = TriggerMode.Color, Keybind = new KeyCombo("F", Array.Empty<string>()),
        CoordSpace = Trigger.CoordSpaceClient, RecordedClientW = 800, RecordedClientH = 600,
    };

    [Fact]
    public void ScreenTrigger_ReturnsAbsoluteRegion_IgnoringMetrics()
    {
        var r = TriggerRegionResolver.Resolve(Screen(), anchorPid: 111, new FakeMetrics());
        Assert.Equal(new RegionRect(10, 20, 30, 40), r);
    }

    [Fact]
    public void ClientTrigger_WindowMoved_OffsetsRegion()
    {
        var m = new FakeMetrics { Origin = (100, 200), Size = (800, 600) };
        var r = TriggerRegionResolver.Resolve(Client(), anchorPid: 111, m);
        Assert.Equal(new RegionRect(150, 260, 30, 40), r);
    }

    [Fact]
    public void ClientTrigger_WindowResized_ScalesRegion()
    {
        var m = new FakeMetrics { Origin = (0, 0), Size = (1600, 1200) };
        var r = TriggerRegionResolver.Resolve(Client(), anchorPid: 111, m);
        Assert.Equal(new RegionRect(100, 120, 60, 80), r);
    }

    [Fact]
    public void ClientTrigger_NoAnchorPid_ReturnsNull()
    {
        var r = TriggerRegionResolver.Resolve(Client(), anchorPid: 0, new FakeMetrics());
        Assert.Null(r);
    }

    [Fact]
    public void ClientTrigger_WindowGone_ReturnsNull()
    {
        var m = new FakeMetrics { Origin = null, Size = null };
        var r = TriggerRegionResolver.Resolve(Client(), anchorPid: 111, m);
        Assert.Null(r);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~TriggerRegionResolverTests" --nologo`
Expected: compile FAILURE — `TriggerRegionResolver` does not exist.

- [ ] **Step 3: Implement the resolver**

Create `Engine/TriggerRegionResolver.cs`:

```csharp
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

/// <summary>
/// Pure: the absolute screen region to capture for a trigger. screen triggers
/// return their stored rect; client triggers resolve against the anchor pid's
/// window (origin + size) and scale. Null = a client trigger whose anchor
/// window can't be resolved (no alt / window gone) — callers skip.
/// </summary>
public static class TriggerRegionResolver
{
    public static RegionRect? Resolve(Trigger trig, int anchorPid, IWindowMetrics metrics)
    {
        if (!trig.IsClientSpace) return trig.Region;
        if (anchorPid == 0) return null;
        if (trig.RecordedClientW is not int rw || trig.RecordedClientH is not int rh) return null;

        var hwnd = metrics.HwndForPid(anchorPid);
        if (hwnd == System.IntPtr.Zero) return null;
        var origin = metrics.ClientOrigin(hwnd);
        var size = metrics.ClientSize(hwnd);
        if (origin is null || size is null) return null;

        return WindowSpaceMath.ToScreenRegion(trig.Region, origin.Value, (rw, rh), size.Value);
    }
}
```

- [ ] **Step 4: Expose alt pids on `AccountRegistry`**

In `PluginHost/AccountRegistry.cs`, add after the `Count` property:

```csharp
    public IReadOnlyCollection<int> Pids => _pidToUserId.Keys.ToArray();
```

Add `using System.Linq;` at the top if not present.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~TriggerRegionResolverTests" --nologo`
Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

```bash
git add Engine/TriggerRegionResolver.cs PluginHost/AccountRegistry.cs tests/RoRoRo.UrOcr.Tests/Engine/TriggerRegionResolverTests.cs
git commit -m "feat(engine): TriggerRegionResolver + AccountRegistry.Pids"
```

---

### Task 5: Wire evaluation — `TriggerCoordinator` routes through the resolver

**Files:**
- Modify: `Engine/TriggerCoordinator.cs`
- Modify: `PluginRuntime.cs`
- Test: `tests/RoRoRo.UrOcr.Tests/Engine/TriggerCoordinatorWindowTests.cs`

**Interfaces:**
- Consumes: `TriggerRegionResolver` (Task 4), `IWindowMetrics` (Task 3).
- Produces: `TriggerCoordinator` ctor gains a trailing `IWindowMetrics metrics` parameter (before the optional `onFirstFire`/`macroClient` — see step 3 for exact placement).

- [ ] **Step 1: Write the failing test**

Create `tests/RoRoRo.UrOcr.Tests/Engine/TriggerCoordinatorWindowTests.cs`. Copy the fakes from the existing `TriggerCoordinatorTests.cs` (same folder) for `ICaptureSource`, `IColorMatchEngine`, `ITextMatchEngine`, `IForegroundCheck`, `IElevationCheck`, `IKeyPress`, `IClock` — match their shapes exactly. Then:

```csharp
using System;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Tests.Engine;

public class TriggerCoordinatorWindowTests
{
    // --- reuse the fake shapes from TriggerCoordinatorTests.cs ---
    private sealed class FakeCapture : ICaptureSource
    {
        public RegionRect? LastRegion;
        public System.Drawing.Bitmap Capture(RegionRect region)
        { LastRegion = region; return new System.Drawing.Bitmap(1, 1); }
    }
    private sealed class MatchAll : IColorMatchEngine
    {
        public bool Matches(System.Drawing.Bitmap b, ColorCriteria c) => true;
        public ColorMatchResult Evaluate(System.Drawing.Bitmap b, ColorCriteria c) => new(new Rgb(0,0,0), 0, true);
    }
    private sealed class NoText : ITextMatchEngine
    {
        public System.Threading.Tasks.Task<(bool matched, string text)> RunAsync(System.Drawing.Bitmap b, TextCriteria c) => System.Threading.Tasks.Task.FromResult((false, ""));
        public System.Threading.Tasks.Task<(bool matched, string text)> RunWithPreprocessAsync(System.Drawing.Bitmap b, TextCriteria c) => System.Threading.Tasks.Task.FromResult((false, ""));
    }
    private sealed class Fg : IForegroundCheck { public bool IsAlt = true; public int Pid = 111; public bool IsForegroundAnAlt() => IsAlt; public int GetForegroundPid() => Pid; }
    private sealed class NotElevated : IElevationCheck { public bool IsForegroundProcessLikelyElevated(int pid) => false; }
    private sealed class NoKeys : IKeyPress { public void Press(KeyCombo c) { } }
    private sealed class FixedClock : IClock { public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch; }
    private sealed class FakeMetrics : IWindowMetrics
    {
        public (int X, int Y)? Origin = (100, 200);
        public (int W, int H)? Size = (800, 600);
        public IntPtr HwndForPid(int pid) => new(0x10);
        public (int X, int Y)? ClientOrigin(IntPtr h) => Origin;
        public (int W, int H)? ClientSize(IntPtr h) => Size;
    }

    private static Trigger ClientColorTrigger() => new()
    {
        Id = Guid.NewGuid(), Name = "c", Enabled = true,
        Region = new RegionRect(50, 60, 30, 40), Mode = TriggerMode.Color,
        Color = new ColorCriteria(new Rgb(0,0,0), 10, ColorSamplingMode.SinglePixel),
        Keybind = new KeyCombo("F", Array.Empty<string>()),
        CoordSpace = Trigger.CoordSpaceClient, RecordedClientW = 800, RecordedClientH = 600,
        AccountAware = false, // proves client anchoring is independent of the flag
    };

    private static (TriggerCoordinator, FakeCapture, TriggerStore) Build(Fg fg, FakeMetrics m, Trigger t)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "urocr-tests", Guid.NewGuid().ToString("N") + ".json");
        var store = new TriggerStore(path);
        store.Add(t);
        var cap = new FakeCapture();
        var coord = new TriggerCoordinator(store, cap, new MatchAll(), new NoText(), fg, new NotElevated(), new NoKeys(),
            new ActivityLog(), new FixedClock(), m);
        return (coord, cap, store);
    }

    [Fact]
    public async Task ClientTrigger_CapturesAnchoredRegion_WindowMoved()
    {
        var (coord, cap, _) = Build(new Fg { IsAlt = true, Pid = 111 }, new FakeMetrics { Origin = (100, 200), Size = (800, 600) }, ClientColorTrigger());
        await coord.TickOnceAsync(System.Threading.CancellationToken.None);
        Assert.Equal(new RegionRect(150, 260, 30, 40), cap.LastRegion);
    }

    [Fact]
    public async Task ClientTrigger_Skips_WhenForegroundNotAlt()
    {
        var (coord, cap, _) = Build(new Fg { IsAlt = false, Pid = 999 }, new FakeMetrics(), ClientColorTrigger());
        await coord.TickOnceAsync(System.Threading.CancellationToken.None);
        Assert.Null(cap.LastRegion); // never captured
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~TriggerCoordinatorWindowTests" --nologo`
Expected: compile FAILURE — `TriggerCoordinator` has no ctor overload taking `IWindowMetrics`.

- [ ] **Step 3: Add `IWindowMetrics` to the coordinator + anchor the capture**

In `Engine/TriggerCoordinator.cs`, add `IWindowMetrics metrics` to the primary constructor parameter list, immediately after `IClock clock` and before `Action<Trigger>? onFirstFire = null`:

```csharp
    IKeyPress keys,
    ActivityLog log,
    IClock clock,
    IWindowMetrics metrics,
    Action<Trigger>? onFirstFire = null,
    IMacroRunClient? macroClient = null)
```

Add `using RoRoRo.UrOcr.PluginHost;` at the top (for `IWindowMetrics`).

In `TickOnceAsync`, the client-anchor gate must run for `client` triggers even when `AccountAware` is false. Replace the capture line and the block just above it. Currently:

```csharp
            if (trig.AccountAware)
            {
                if (!foreground.IsForegroundAnAlt())
                { ... skip ... }
                var pid = foreground.GetForegroundPid();
                if (elevation.IsForegroundProcessLikelyElevated(pid))
                { ... skip ... }
            }

            using var bmp = capture.Capture(trig.Region);
```

Replace with:

```csharp
            if (trig.AccountAware || trig.IsClientSpace)
            {
                if (!foreground.IsForegroundAnAlt())
                {
                    log.Record(trig.Id, trig.Name, ActivityKind.SkippedNotAlt);
                    _wasMatched[trig.Id] = false;
                    continue;
                }
                var pid = foreground.GetForegroundPid();
                if (elevation.IsForegroundProcessLikelyElevated(pid))
                {
                    log.Record(trig.Id, trig.Name, ActivityKind.BlockedElevated);
                    _wasMatched[trig.Id] = false;
                    continue;
                }
            }

            var captureRegion = TriggerRegionResolver.Resolve(trig, foreground.GetForegroundPid(), metrics);
            if (captureRegion is null)
            {
                // client trigger whose anchor window vanished mid-tick
                log.Record(trig.Id, trig.Name, ActivityKind.SkippedNotAlt, "anchor window unavailable");
                _wasMatched[trig.Id] = false;
                continue;
            }
            using var bmp = capture.Capture(captureRegion);
```

Note: `Resolve` returns the screen trigger's own `Region` unchanged, so screen-trigger behavior is byte-identical. `RegionRect` is a reference type (`record`); `captureRegion` is `RegionRect?` (nullable reference) — pass it directly to `Capture` after the null check (no `.Value`).

- [ ] **Step 4: Wire `WindowMetrics` in `PluginRuntime`**

In `PluginRuntime.cs`, add a field near the other collaborators:

```csharp
    public IWindowMetrics WindowMetrics { get; } = new WindowMetrics();
```

Add `using RoRoRo.UrOcr.PluginHost;` if the file isn't already in/using that namespace (it already references `AccountRegistry` etc. from `PluginHost`). Then in `StartAsync`, add `WindowMetrics` to the `TriggerCoordinator` constructor call, in the same position as the ctor (after `new SystemClock()`):

```csharp
        Coordinator = new TriggerCoordinator(
            Triggers, Capture, Color, Text, Foreground, Elevation, Keys, Activity,
            new SystemClock(), WindowMetrics,
            onFirstFire: t => Toasts.Show(t.Action == Storage.TriggerAction.RunMacro
                ? $"✓ \"{t.Name}\" ran a macro"
                : $"✓ \"{t.Name}\" fired ({t.Keybind.Key})"),
            macroClient: MacroClient)
        { TickRateHz = Settings.Current.TickRateHz };
```

- [ ] **Step 5: Run test + full suite**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --nologo`
Expected: PASS (all, including the existing `TriggerCoordinatorTests` — their ctor calls now need the `metrics` arg; update those existing tests to pass a fake `IWindowMetrics` in the same position, mirroring `FakeMetrics` above. This is expected fallout, fix it here.)

- [ ] **Step 6: Commit**

```bash
git add Engine/TriggerCoordinator.cs PluginRuntime.cs tests/RoRoRo.UrOcr.Tests/Engine/TriggerCoordinatorWindowTests.cs tests/RoRoRo.UrOcr.Tests/Engine/TriggerCoordinatorTests.cs
git commit -m "feat(engine): coordinator anchors client-space regions to the foreground alt"
```

---

### Task 6: Wire preview — meter anchors to the first running alt

**Files:**
- Modify: `Engine/PreviewEvaluator.cs`
- Modify: `UI/TriggerRowViewModel.cs`
- Modify: `PluginRuntime.cs`

**Interfaces:**
- Consumes: `TriggerRegionResolver` (Task 4), `IWindowMetrics` (Task 3), `AccountRegistry.Pids` (Task 4).
- Produces: `PreviewEvaluator.EvaluateTrigger(Trigger trig) → ColorMatchResult?`.

No new unit test file — `PreviewEvaluator` composes already-tested pure parts (`TriggerRegionResolver`); the change is wiring + a first-alt pick. Build + the human checklist gate it. (If a test is trivial to add over a fake metrics + fake capture, add one; otherwise rely on the resolver tests.)

- [ ] **Step 1: Rework `PreviewEvaluator` to resolve the anchor**

Replace `Engine/PreviewEvaluator.cs` with:

```csharp
// Engine/PreviewEvaluator.cs
using System.Linq;
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

/// <summary>
/// Live match meter for the editor. For screen triggers, samples the absolute
/// region. For client triggers, anchors to the FIRST running alt (the editor
/// window is foreground while you edit, so we can't use the foreground alt) so
/// the meter works during setup. Pure read — never fires.
/// </summary>
public sealed class PreviewEvaluator(
    ICaptureSource capture, IColorMatchEngine color,
    IWindowMetrics metrics, AccountRegistry accounts)
{
    public ColorMatchResult? EvaluateTrigger(Trigger trig)
    {
        if (trig.Mode != TriggerMode.Color || trig.Color is null) return null;
        var anchorPid = trig.IsClientSpace ? accounts.Pids.FirstOrDefault() : 0;
        var region = TriggerRegionResolver.Resolve(trig, anchorPid, metrics);
        if (region is null || region.Width < 1 || region.Height < 1) return null;
        using var bmp = capture.Capture(region);
        return color.Evaluate(bmp, trig.Color);
    }
}
```

- [ ] **Step 2: Update `PluginRuntime` preview construction**

In `PluginRuntime.cs` constructor, change:

```csharp
        Preview = new Engine.PreviewEvaluator(Capture, Color);
```

to:

```csharp
        Preview = new Engine.PreviewEvaluator(Capture, Color, WindowMetrics, Accounts);
```

(`WindowMetrics` field added in Task 5. Ensure the field initializer for `WindowMetrics` precedes this — reorder the property so it's declared before `Preview` is set, or move the `Preview = ...` line into `StartAsync` if ordering fights you. Simplest: `WindowMetrics` is an auto-property with initializer, which runs before the constructor body, so this ordering is safe.)

- [ ] **Step 3: Call `EvaluateTrigger` from the VM**

In `UI/TriggerRowViewModel.cs`, in `OnPreviewTick`, replace:

```csharp
        try { result = preview.EvaluateOnce(region, criteria); }
```

with:

```csharp
        try { result = preview.EvaluateTrigger(source); }
```

Remove the now-unused local `region`/`criteria` extraction in that method if they become unused (keep whatever the target-swatch seed still needs — `source.Color.TargetRgb` at the end stays).

- [ ] **Step 4: Build + full suite**

Run: `dotnet build rororo-ur-ocr.csproj --nologo && dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --nologo`
Expected: Build succeeded; all tests PASS. (If any existing test constructed `PreviewEvaluator` directly, update it to the new ctor.)

- [ ] **Step 5: Commit**

```bash
git add Engine/PreviewEvaluator.cs PluginRuntime.cs UI/TriggerRowViewModel.cs
git commit -m "feat(preview): meter anchors client regions to the first running alt"
```

---

### Task 7: Pick-time anchoring — `TriggerAnchor` + add/re-pick wiring

**Files:**
- Create: `Engine/TriggerAnchor.cs`
- Modify: `UI/MainViewModel.cs`
- Modify: `UI/TriggerEditView.xaml.cs` (or wherever "Re-pick region" is handled)
- Test: `tests/RoRoRo.UrOcr.Tests/Engine/TriggerAnchorTests.cs`

**Interfaces:**
- Consumes: `WindowSpaceMath` (Task 2), `IWindowMetrics` (Task 3).
- Produces: `TriggerAnchor.ForPickedRegion(RegionRect pickedScreen, IReadOnlyCollection<int> altPids, IWindowMetrics metrics) → AnchorResult`; `AnchorResult(string CoordSpace, RegionRect Region, int? RecordedClientW, int? RecordedClientH)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/RoRoRo.UrOcr.Tests/Engine/TriggerAnchorTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Tests.Engine;

public class TriggerAnchorTests
{
    // Alt pid 111 → window at origin (100,200), client 800x600 (covers 100..900, 200..800).
    private sealed class FakeMetrics : IWindowMetrics
    {
        public IntPtr HwndForPid(int pid) => pid == 111 ? new(0x11) : IntPtr.Zero;
        public (int X, int Y)? ClientOrigin(IntPtr h) => h == new IntPtr(0x11) ? (100, 200) : null;
        public (int W, int H)? ClientSize(IntPtr h) => h == new IntPtr(0x11) ? (800, 600) : null;
    }

    [Fact]
    public void RegionOverAlt_ProducesClientSpace_WithOffsetAndRecordedSize()
    {
        // Region centered at (250, 300) — inside 100..900 / 200..800 → anchors to pid 111.
        var res = TriggerAnchor.ForPickedRegion(new RegionRect(200, 280, 100, 40), new[] { 111 }, new FakeMetrics());
        Assert.Equal(Trigger.CoordSpaceClient, res.CoordSpace);
        Assert.Equal(new RegionRect(100, 80, 100, 40), res.Region); // (200-100, 280-200)
        Assert.Equal(800, res.RecordedClientW);
        Assert.Equal(600, res.RecordedClientH);
    }

    [Fact]
    public void RegionOverNoAlt_StaysScreen()
    {
        // Region far outside the alt window → screen.
        var res = TriggerAnchor.ForPickedRegion(new RegionRect(2000, 2000, 20, 20), new[] { 111 }, new FakeMetrics());
        Assert.Equal(Trigger.CoordSpaceScreen, res.CoordSpace);
        Assert.Equal(new RegionRect(2000, 2000, 20, 20), res.Region);
        Assert.Null(res.RecordedClientW);
    }

    [Fact]
    public void NoAltsRunning_StaysScreen()
    {
        var res = TriggerAnchor.ForPickedRegion(new RegionRect(200, 280, 10, 10), Array.Empty<int>(), new FakeMetrics());
        Assert.Equal(Trigger.CoordSpaceScreen, res.CoordSpace);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~TriggerAnchorTests" --nologo`
Expected: compile FAILURE — `TriggerAnchor` does not exist.

- [ ] **Step 3: Implement**

Create `Engine/TriggerAnchor.cs`:

```csharp
using System.Collections.Generic;
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

public sealed record AnchorResult(string CoordSpace, RegionRect Region, int? RecordedClientW, int? RecordedClientH);

/// <summary>
/// Pick-time: decide whether a picked screen region is window-anchored. If the
/// region's center falls inside a running alt's client rect, store it
/// client-relative to that window (+ recorded client size); else keep it screen.
/// Pure over IWindowMetrics + the alt pid list.
/// </summary>
public static class TriggerAnchor
{
    public static AnchorResult ForPickedRegion(RegionRect picked, IReadOnlyCollection<int> altPids, IWindowMetrics metrics)
    {
        int cx = picked.X + picked.Width / 2;
        int cy = picked.Y + picked.Height / 2;

        foreach (var pid in altPids)
        {
            var hwnd = metrics.HwndForPid(pid);
            if (hwnd == System.IntPtr.Zero) continue;
            var origin = metrics.ClientOrigin(hwnd);
            var size = metrics.ClientSize(hwnd);
            if (origin is null || size is null) continue;
            var (ox, oy) = origin.Value;
            var (w, h) = size.Value;
            if (cx >= ox && cx < ox + w && cy >= oy && cy < oy + h)
            {
                return new AnchorResult(
                    Trigger.CoordSpaceClient,
                    WindowSpaceMath.ToClientRegion(picked, origin.Value),
                    w, h);
            }
        }
        return new AnchorResult(Trigger.CoordSpaceScreen, picked, null, null);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~TriggerAnchorTests" --nologo`
Expected: PASS (3 tests).

- [ ] **Step 5: Wire the add-trigger flow**

In `UI/MainViewModel.cs` `AddTriggerCommand`, after `pick.Picked` is confirmed and before building the `Trigger`, compute the anchor and apply it. Replace the `Trigger` construction so `Region`/`CoordSpace`/`RecordedClientW/H` come from `TriggerAnchor`:

```csharp
            var anchor = Engine.TriggerAnchor.ForPickedRegion(pick.Picked, runtime.Accounts.Pids, runtime.WindowMetrics);
            var t = new Trigger
            {
                Id = Guid.NewGuid(),
                Name = "New trigger",
                Region = anchor.Region,
                CoordSpace = anchor.CoordSpace,
                RecordedClientW = anchor.RecordedClientW,
                RecordedClientH = anchor.RecordedClientH,
                Mode = TriggerMode.Color,
                Color = new ColorCriteria(colorPicker.SelectedColor, colorPicker.Tolerance, ColorSamplingMode.SinglePixel),
                Keybind = new KeyCombo("F", Array.Empty<string>()),
            };
```

(`runtime.WindowMetrics` + `runtime.Accounts.Pids` from Tasks 4-5. `MainViewModel` holds `_runtime`/`runtime` — use the field it already references.)

- [ ] **Step 6: Wire the (currently dead) re-pick button**

The `Re-pick region` button at `UI/TriggerEditView.xaml:29` has **no `Command` or `Click`** today — it does nothing. Wire it through the row VM so it re-picks and re-anchors.

6a. `UI/TriggerRowViewModel.cs` — give the VM the runtime and add a command. Change the primary constructor from `(Trigger source, PreviewEvaluator preview)` to `(Trigger source, PreviewEvaluator preview, PluginRuntime runtime)`. Add (near the other command/property members):

```csharp
    public System.Windows.Input.ICommand RepickRegionCommand => _repick ??= new RelayCommand(_ =>
    {
        var pick = new RegionPickerOverlay();
        if (pick.ShowDialog() != true || pick.Picked is null) return;
        var anchor = Engine.TriggerAnchor.ForPickedRegion(pick.Picked, runtime.Accounts.Pids, runtime.WindowMetrics);
        source.Region = anchor.Region;
        source.CoordSpace = anchor.CoordSpace;
        source.RecordedClientW = anchor.RecordedClientW;
        source.RecordedClientH = anchor.RecordedClientH;
        runtime.Triggers.Update(source);
        OnChanged(nameof(RegionModeText));   // added in Task 8
    });
    private System.Windows.Input.ICommand? _repick;
```

(Confirm `RelayCommand`'s ctor shape from `UI/MainViewModel.cs` — it is `new RelayCommand(Action<object?>)`; match it. `RegionModeText` lands in Task 8 — if implementing Task 7 before Task 8, add a temporary `OnChanged("RegionModeText")` string or fold the two tasks; the subagent controller will sequence them.)

6b. `UI/MainViewModel.cs` — pass the runtime at **both** `TriggerRowViewModel` construction sites (the initial `foreach` load and the `AddTriggerCommand` `new TriggerRowViewModel(t, _runtime.Preview)`): change each to `new TriggerRowViewModel(t, runtime.Preview, runtime)` (use the constructor's `runtime` param / the `_runtime` field consistently).

6c. `UI/TriggerEditView.xaml` — bind the button:

```xml
            <Button Content="Re-pick region"
                    Style="{StaticResource SecondaryButton}"
                    Command="{Binding RepickRegionCommand}"
                    Padding="10,6"
                    HorizontalAlignment="Left"
                    Margin="0,0,0,16" />
```

6d. If any test constructs `TriggerRowViewModel` directly, update it to the 3-arg ctor (grep `new TriggerRowViewModel(` under `tests/`). None is expected (it's a UI VM).

- [ ] **Step 7: Build + full suite**

Run: `dotnet build rororo-ur-ocr.csproj --nologo && dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --nologo`
Expected: Build succeeded; all tests PASS.

- [ ] **Step 8: Commit**

```bash
git add Engine/TriggerAnchor.cs UI/MainViewModel.cs UI/TriggerEditView.xaml.cs tests/RoRoRo.UrOcr.Tests/Engine/TriggerAnchorTests.cs
git commit -m "feat(ui): pick-time window anchoring on add + re-pick"
```

---

### Task 8: UI — region-mode display + toggle

**Files:**
- Modify: `UI/TriggerRowViewModel.cs`
- Modify: `UI/TriggerEditView.xaml`

**Interfaces:**
- Consumes: `Trigger.IsClientSpace`/`CoordSpace` (Task 1).
- Produces: `TriggerRowViewModel.RegionModeText` (string), `TriggerRowViewModel.IsWindowAnchored` (bool, TwoWay for the toggle).

No unit tests (UI binding); build + human checklist gate.

- [ ] **Step 1: Add VM surface**

In `UI/TriggerRowViewModel.cs`, add:

```csharp
    /// <summary>Human-readable region anchor mode for the REGION line.</summary>
    public string RegionModeText => source.IsClientSpace
        ? $"Window-anchored (recorded {source.RecordedClientW}×{source.RecordedClientH})"
        : $"Screen: {source.Region.X}, {source.Region.Y}  {source.Region.Width}×{source.Region.Height}";

    /// <summary>True when the trigger is window-anchored. Read-only surface for
    /// the mode indicator; switching modes is done via Re-pick (window) — a plain
    /// setter can't re-derive the client offset without a fresh pick.</summary>
    public bool IsWindowAnchored => source.IsClientSpace;
```

Call `OnChanged(nameof(RegionModeText)); OnChanged(nameof(IsWindowAnchored));` wherever the region changes (in the re-pick handler path from Task 7, after assigning the anchor).

- [ ] **Step 2: Show the mode on the REGION line**

In `UI/TriggerEditView.xaml`, near the existing REGION label/`Re-pick region` button (around line 25-31), add a bound `TextBlock` under the region info showing the mode:

```xml
            <TextBlock Text="{Binding RegionModeText}"
                       FontSize="11"
                       Foreground="{StaticResource MutedTextBrush}"
                       TextWrapping="Wrap"
                       Margin="0,0,0,6" />
```

(Place it so it reads with the existing `RegionRect { … }` display. If that raw display exists, this line supplements it; leaving the raw line is fine.)

- [ ] **Step 3: Build**

Run: `dotnet build rororo-ur-ocr.csproj --nologo`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add UI/TriggerRowViewModel.cs UI/TriggerEditView.xaml
git commit -m "feat(ui): show window/screen region mode in the trigger editor"
```

---

### Task 9: v0.4.0 — manifest, CHANGELOG, README

**Files:**
- Modify: `manifest.json`
- Modify: `CHANGELOG.md` (create if absent)
- Modify: `README.md`

- [ ] **Step 1: Bump the version**

`manifest.json`: `"version": "0.3.0"` → `"version": "0.4.0"`. Confirm there is no other version string to change (the build script reads the manifest; there is no `<Version>` in `rororo-ur-ocr.csproj` — verify with `grep -n "0.3.0" rororo-ur-ocr.csproj manifest.json`).

- [ ] **Step 2: CHANGELOG entry**

Add (create `CHANGELOG.md` with a `# Changelog` header if it doesn't exist) at the top:

```markdown
## 0.4.0 — 2026-07-02

### Added

- **Window-anchored trigger regions.** A trigger's watch region can now anchor to the alt window's client area instead of a fixed screen spot. It follows whichever alt is in the foreground (watching the same relative UI on each alt) and scales with the window's size, so moving or resizing the Roblox window no longer breaks detection — no re-pick needed. New triggers picked over an alt default to window-anchored; regions picked over a non-alt window stay screen-absolute, as do all pre-0.4 triggers (they migrate to schema v2 as `screen`, non-breaking). The Ur-OCR counterpart to Ur Task v0.4.0's window awareness.

### Fixed

- Macro picker refreshes on open (no restart needed to see a newly-recorded macro); themed the dropdown, column headers, and toasts; clarified the cooldown field.
```

- [ ] **Step 3: README**

In `README.md`, add a short "Window-anchored regions" subsection near the trigger docs describing: regions follow the foreground alt and scale with its size; new triggers over an alt are window-anchored automatically; screen-absolute is used over non-alt windows and for pre-0.4 triggers. Keep it a few sentences, matching the file's tone.

- [ ] **Step 4: Build + full suite, then commit**

Run: `dotnet build rororo-ur-ocr.csproj --nologo && dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --nologo`
Expected: Build succeeded; all tests PASS.

```bash
git add manifest.json CHANGELOG.md README.md
git commit -m "chore: v0.4.0 — window-anchored regions (docs + manifest)"
```

---

## Human verification (after all tasks, live alts)

1. Pick a color region over an alt's UI; move that alt's window — trigger still matches (no re-pick).
2. Resize the alt window — region scales and still matches.
3. Cycle foreground between two alts — the same relative region is watched on each.
4. A pre-0.4 (screen) trigger still behaves exactly as before.
5. Pick a region over a non-alt window — it stays screen-anchored (the REGION line says Screen).
6. Live match meter shows a live sample while editing a window-anchored trigger (anchored to the first running alt).
