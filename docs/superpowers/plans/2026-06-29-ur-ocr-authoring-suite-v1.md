# Ur-OCR Authoring Suite v1 — Implementation Plan (live match meter + dry-run)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Two verification tools so a user can confirm a color trigger is right before arming it — a **live match meter** (real-time sampled-vs-target color + distance + MATCH while editing) and a **global dry-run** toggle (arm everything in log-only mode, no keypresses).

**Architecture:** The engine already captures the region and computes Euclidean color distance every 200ms (5 Hz) and throws it away. v1 surfaces that: `ColorMatcher` gains a structured `Evaluate` returning `(sampled, distance, matched)`; a small `PreviewEvaluator` runs it on demand for the trigger being edited; the editor binds a live panel. Dry-run is a `bool` on `TriggerCoordinator` that, on a would-fire, logs a new `WouldFire` activity and skips the keypress.

**Tech Stack:** C#/.NET 10 WPF (`net10.0-windows10.0.19041.0`), `System.Drawing`, xUnit. Design spec: `rororo-ur-task/docs/superpowers/specs/2026-06-29-ur-ocr-authoring-suite-design.md`.

## Global Constraints

- **Decided:** dry-run is a **global toggle** (not per-trigger), per the spec §9 resolution.
- Namespaces: engine code `RoRoRo.UrOcr.Engine`, storage `RoRoRo.UrOcr.Storage`, UI `RoRoRo.UrOcr.UI`.
- `ColorMatcher.Matches` MUST keep returning the same bool it does today (refactor it to delegate to `Evaluate` — behavior identical).
- Dry-run, on a would-fire: log `ActivityKind.WouldFire`, and **skip** both `keys.Press(...)` and `store.MarkFired(...)` (a test must not press keys or burn cooldown/hit-count). Normal mode is unchanged.
- The preview path must evaluate the draft trigger **regardless of account-gating** (you author while looking at the game); only the armed coordinator applies the foreground-is-alt gate.
- **Build-lock:** if an Ur-OCR instance is running it may lock `bin/`; redirect test output with `-o "C:/Users/estev/AppData/Local/Temp/urocr-sdd/tN"`.
- Test command shape: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~<Name>" -o "<temp>"`. The unit-test project references only the main project (no ROROROblox sibling) — it builds standalone.
- Tasks 1-3 are TDD + unit-verified. Tasks 4-5 are WPF UI: build-verified (compiles, follows existing patterns); **visual/interactive correctness needs a human run** — call that out, don't claim it verified.

---

### Task 1: ColorMatcher structured result

**Files:**
- Modify: `Engine/ColorMatcher.cs`
- Modify: `Engine/CaptureEngine.cs` (the `IColorMatchEngine` interface lives here, lines 8)
- Create: `Engine/ColorMatchResult.cs`
- Test: `tests/RoRoRo.UrOcr.Tests/Engine/ColorMatcherEvaluateTests.cs`

**Interfaces:**
- Produces: `record ColorMatchResult(Rgb Sampled, double Distance, bool Matched)`; `IColorMatchEngine` gains `ColorMatchResult Evaluate(Bitmap b, ColorCriteria c)`; `ColorMatcher.Matches` delegates to `Evaluate(...).Matched`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/RoRoRo.UrOcr.Tests/Engine/ColorMatcherEvaluateTests.cs
using System.Drawing;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Tests.Engine;

public class ColorMatcherEvaluateTests
{
    private static Bitmap Solid(int r, int g, int b)
    {
        var bmp = new Bitmap(4, 4);
        using var gfx = Graphics.FromImage(bmp);
        gfx.Clear(Color.FromArgb(r, g, b));
        return bmp;
    }

    [Fact]
    public void Evaluate_ExactColor_ZeroDistance_Matches()
    {
        var matcher = new ColorMatcher();
        using var bmp = Solid(255, 17, 95); // 0xFF115F
        var crit = new ColorCriteria(new Rgb(255, 17, 95), ToleranceRgb: 10, ColorSamplingMode.SinglePixel);

        var r = matcher.Evaluate(bmp, crit);

        Assert.Equal(new Rgb(255, 17, 95), r.Sampled);
        Assert.True(r.Distance < 0.001);
        Assert.True(r.Matched);
    }

    [Fact]
    public void Evaluate_OutsideTolerance_DoesNotMatch_ButReportsDistance()
    {
        var matcher = new ColorMatcher();
        using var bmp = Solid(255, 17, 95);
        var crit = new ColorCriteria(new Rgb(0, 0, 0), ToleranceRgb: 10, ColorSamplingMode.SinglePixel);

        var r = matcher.Evaluate(bmp, crit);

        Assert.False(r.Matched);
        Assert.True(r.Distance > 10);
    }

    [Fact]
    public void Matches_StillAgreesWithEvaluate()
    {
        var matcher = new ColorMatcher();
        using var bmp = Solid(100, 100, 100);
        var crit = new ColorCriteria(new Rgb(100, 100, 100), 5, ColorSamplingMode.RegionAverage);
        Assert.Equal(matcher.Evaluate(bmp, crit).Matched, matcher.Matches(bmp, crit));
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test ... --filter "FullyQualifiedName~ColorMatcherEvaluateTests" -o "C:/Users/estev/AppData/Local/Temp/urocr-sdd/t1"` → FAIL (`Evaluate` not defined).

- [ ] **Step 3: Implement**

```csharp
// Engine/ColorMatchResult.cs
using RoRoRo.UrOcr.Storage;
namespace RoRoRo.UrOcr.Engine;

public sealed record ColorMatchResult(Rgb Sampled, double Distance, bool Matched);
```

In `Engine/CaptureEngine.cs`, add to the `IColorMatchEngine` interface (currently `public interface IColorMatchEngine { bool Matches(Bitmap b, ColorCriteria c); }`):

```csharp
public interface IColorMatchEngine
{
    bool Matches(Bitmap b, ColorCriteria c);
    ColorMatchResult Evaluate(Bitmap b, ColorCriteria c);
}
```

In `Engine/ColorMatcher.cs`, replace the body of `Matches` with `Evaluate` + a delegating `Matches`:

```csharp
public ColorMatchResult Evaluate(Bitmap bmp, ColorCriteria c)
{
    var (r, g, b) = c.SamplingMode switch
    {
        ColorSamplingMode.SinglePixel => SamplePixel(bmp, bmp.Width / 2, bmp.Height / 2),
        ColorSamplingMode.RegionAverage => SampleAverage(bmp),
        _ => throw new ArgumentOutOfRangeException()
    };

    var dr = r - c.TargetRgb.R;
    var dg = g - c.TargetRgb.G;
    var db = b - c.TargetRgb.B;
    var distance = Math.Sqrt(dr * dr + dg * dg + db * db);
    return new ColorMatchResult(new Rgb(r, g, b), distance, distance <= c.ToleranceRgb);
}

public bool Matches(Bitmap bmp, ColorCriteria c) => Evaluate(bmp, c).Matched;
```

(Keep `SamplePixel` / `SampleAverage` as-is.)

- [ ] **Step 4: Run to verify pass** — same filter → 3/3 PASS.

- [ ] **Step 5: Commit**

```bash
git add Engine/ColorMatchResult.cs Engine/ColorMatcher.cs Engine/CaptureEngine.cs tests/RoRoRo.UrOcr.Tests/Engine/ColorMatcherEvaluateTests.cs
git commit -m "feat(engine): ColorMatcher.Evaluate — structured (sampled, distance, matched)"
```

---

### Task 2: Global dry-run + WouldFire

**Files:**
- Modify: `Engine/ActivityLog.cs` (the `ActivityKind` enum, line 3)
- Modify: `Engine/TriggerCoordinator.cs` (add `DryRun`; gate the fire branch at lines 128-136)
- Test: `tests/RoRoRo.UrOcr.Tests/Engine/DryRunTests.cs`

**Interfaces:**
- Consumes: the existing `TriggerCoordinator` test harness/fakes in `tests/RoRoRo.UrOcr.Tests/Engine/TriggerCoordinatorTests.cs` (reuse its fake `IKeyPress`, `ICaptureSource`, `IColorMatchEngine`, etc. — read that file and mirror how it constructs a coordinator + a color trigger; do NOT invent a new harness).
- Produces: `TriggerCoordinator.DryRun` (bool); `ActivityKind.WouldFire`.

- [ ] **Step 1: Write the failing test** — reuse the existing fakes from `TriggerCoordinatorTests.cs`. The test builds a coordinator with a fake `IKeyPress` that records calls and a fake `IColorMatchEngine` that returns matched=true, plus one enabled color trigger; then:

```csharp
// tests/RoRoRo.UrOcr.Tests/Engine/DryRunTests.cs
// Mirror the construction pattern + fakes from TriggerCoordinatorTests.cs.
// Two facts:
//  - DryRun = true:  after TickOnceAsync, the fake IKeyPress.Press was NOT called,
//                    and the ActivityLog contains an entry with Kind == ActivityKind.WouldFire.
//  - DryRun = false: after TickOnceAsync, IKeyPress.Press WAS called and the log has Kind == Fired.
// Use trig.AccountAware = false (or the fake foreground returning an alt) so the account gate
// doesn't short-circuit. Assert against the real ActivityLog passed to the coordinator.
```

(Write the two `[Fact]`s concretely using the exact fake types found in `TriggerCoordinatorTests.cs`.)

- [ ] **Step 2: Run to verify it fails** — `--filter "FullyQualifiedName~DryRunTests"` → FAIL (`DryRun` / `WouldFire` not defined).

- [ ] **Step 3: Implement**

`Engine/ActivityLog.cs` — add `WouldFire`:

```csharp
public enum ActivityKind { Fired, WouldFire, NoMatch, SkippedCooldown, SkippedNotAlt, BlockedElevated, Error }
```

`Engine/TriggerCoordinator.cs` — add the property near `Paused` (line 35):

```csharp
public bool DryRun { get; set; }
```

Replace the fire branch (lines 128-136) with:

```csharp
if (matched && !was && cooldownReady)
{
    if (DryRun)
    {
        // Log-only: prove detection without pressing keys or burning cooldown/hit-count.
        log.Record(trig.Id, trig.Name, ActivityKind.WouldFire,
            detail.Length > 0 ? $"OCR: {detail}" : null);
    }
    else
    {
        keys.Press(trig.Keybind);
        store.MarkFired(trig.Id, now);
        log.Record(trig.Id, trig.Name, ActivityKind.Fired,
            detail.Length > 0 ? $"OCR: {detail}" : null);
        if (!trig.FirstFireConfirmed)
            onFirstFire?.Invoke(trig);
    }
}
```

- [ ] **Step 4: Run to verify pass** — `--filter "FullyQualifiedName~DryRunTests"` → 2/2 PASS. Then run the full `TriggerCoordinatorTests` to confirm no regression: `--filter "FullyQualifiedName~TriggerCoordinatorTests"`.

- [ ] **Step 5: Commit**

```bash
git add Engine/ActivityLog.cs Engine/TriggerCoordinator.cs tests/RoRoRo.UrOcr.Tests/Engine/DryRunTests.cs
git commit -m "feat(engine): global dry-run — log WouldFire, skip keypress + MarkFired"
```

---

### Task 3: PreviewEvaluator

**Files:**
- Create: `Engine/PreviewEvaluator.cs`
- Test: `tests/RoRoRo.UrOcr.Tests/Engine/PreviewEvaluatorTests.cs`

**Interfaces:**
- Consumes: `ICaptureSource` (`Capture(RegionRect)`), `IColorMatchEngine` (`Evaluate`).
- Produces: `PreviewEvaluator(ICaptureSource capture, IColorMatchEngine color)` with `ColorMatchResult? EvaluateOnce(RegionRect region, ColorCriteria criteria)` — returns null for a degenerate region (<1px), else captures + evaluates. Never fires anything.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/RoRoRo.UrOcr.Tests/Engine/PreviewEvaluatorTests.cs
using System.Drawing;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Tests.Engine;

public class PreviewEvaluatorTests
{
    private sealed class FakeCapture : ICaptureSource
    {
        private readonly Color _fill;
        public FakeCapture(int r, int g, int b) => _fill = Color.FromArgb(r, g, b);
        public Bitmap Capture(RegionRect region)
        {
            var bmp = new Bitmap(Math.Max(1, region.Width), Math.Max(1, region.Height));
            using var gfx = Graphics.FromImage(bmp);
            gfx.Clear(_fill);
            return bmp;
        }
    }

    [Fact]
    public void EvaluateOnce_ReturnsMatchResult_ForCapturedColor()
    {
        var pe = new PreviewEvaluator(new FakeCapture(255, 17, 95), new ColorMatcher());
        var region = new RegionRect(0, 0, 4, 4);
        var crit = new ColorCriteria(new Rgb(255, 17, 95), 10, ColorSamplingMode.SinglePixel);

        var r = pe.EvaluateOnce(region, crit);

        Assert.NotNull(r);
        Assert.True(r!.Matched);
        Assert.True(r.Distance < 0.001);
    }

    [Fact]
    public void EvaluateOnce_DegenerateRegion_ReturnsNull()
    {
        var pe = new PreviewEvaluator(new FakeCapture(0, 0, 0), new ColorMatcher());
        Assert.Null(pe.EvaluateOnce(new RegionRect(0, 0, 0, 0), new ColorCriteria(new Rgb(0, 0, 0), 5, ColorSamplingMode.SinglePixel)));
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `--filter "FullyQualifiedName~PreviewEvaluatorTests"` → FAIL.

- [ ] **Step 3: Implement**

```csharp
// Engine/PreviewEvaluator.cs
using RoRoRo.UrOcr.Storage;
namespace RoRoRo.UrOcr.Engine;

/// <summary>
/// On-demand "what does this region look like right now?" evaluator for the
/// trigger editor's live match meter. Captures the draft region and runs the
/// color matcher's structured Evaluate. Never fires anything — pure read.
/// </summary>
public sealed class PreviewEvaluator(ICaptureSource capture, IColorMatchEngine color)
{
    public ColorMatchResult? EvaluateOnce(RegionRect region, ColorCriteria criteria)
    {
        if (region.Width < 1 || region.Height < 1) return null;
        using var bmp = capture.Capture(region);
        return color.Evaluate(bmp, criteria);
    }
}
```

- [ ] **Step 4: Run to verify pass** — 2/2 PASS.

- [ ] **Step 5: Commit**

```bash
git add Engine/PreviewEvaluator.cs tests/RoRoRo.UrOcr.Tests/Engine/PreviewEvaluatorTests.cs
git commit -m "feat(engine): PreviewEvaluator — on-demand region match for the live meter"
```

---

### Task 4: Live match meter UI (TriggerEditView)

**Files:**
- Modify: `UI/TriggerEditView.xaml` (+ `.xaml.cs`)
- Modify: the edit view model / `UI/TriggerRowViewModel.cs` and/or `UI/MainViewModel.cs` (whatever owns the selected-trigger edit state — read them first)
- Modify: `PluginRuntime.cs` (construct a `PreviewEvaluator` and make it reachable by the editor)

**No unit test — WPF UI.** Build must compile; correctness is visual and needs a human run (note it).

- [ ] **Step 1: Read the UI wiring.** Open `UI/TriggerEditView.xaml` + its code-behind, `UI/MainViewModel.cs` (how the selected trigger / edit panel is bound), and `UI/MainWindow.xaml` (master-detail). Identify: the DataContext of the edit panel, how `Region`/`Color` edits are bound, and how to start/stop a `DispatcherTimer` when the editor is shown/hidden.

- [ ] **Step 2: Construct `PreviewEvaluator` in `PluginRuntime.cs`** next to where `Coordinator` is built (it already has `Capture` and `Color` — reuse those instances): `Preview = new Engine.PreviewEvaluator(Capture, Color);` and expose it so the edit VM can reach it (mirror how the VM reaches other runtime services).

- [ ] **Step 3: Add a live-preview panel to `TriggerEditView.xaml`** below the region/color fields: a **target color swatch**, a **live sampled swatch**, a **distance** number, and a **MATCH dot** (green when matched, red otherwise). Bind to new VM properties: `PreviewSampledBrush`, `PreviewTargetBrush`, `PreviewDistance` (formatted, e.g. `"d = 12.4"`), `PreviewIsMatch` (bool → green/red).

- [ ] **Step 4: Drive the preview at ~5 Hz** from the edit VM with a `DispatcherTimer` (Interval 200ms). On each tick, if the editor is open and the draft is a color trigger: build a `ColorCriteria` from the current edit state (target Rgb + tolerance + sampling mode), call `Preview.EvaluateOnce(draftRegion, criteria)`, and push the result into the bound properties (null → blank/"—"). Start the timer when a color trigger is selected/edited; stop it on deselect/close so it never runs in the background.

- [ ] **Step 5: Build to verify it compiles.**

Run: `dotnet build rororo-ur-ocr.csproj -o "C:/Users/estev/AppData/Local/Temp/urocr-sdd/t4"`
Expected: build succeeds, 0 errors. (Pre-existing CA1416 platform warnings in `TextMatcher.cs` are not yours.)

- [ ] **Step 6: Commit** — `git add UI/TriggerEditView.xaml UI/TriggerEditView.xaml.cs <edit-vm files> PluginRuntime.cs && git commit -m "feat(ui): live match meter in the trigger editor"`. In the report, state clearly that this is compile-verified only and the visual/live behavior needs a human run.

---

### Task 5: Dry-run toggle UI (MainWindow)

**Files:**
- Modify: `UI/MainWindow.xaml` (+ the VM that owns the toggle — read `UI/MainViewModel.cs` and how the existing `Paused`/F9 button binds)
- Modify: `PluginRuntime.cs` if needed to expose the coordinator's `DryRun` for binding

**No unit test — WPF UI.** Compile-verified; behavior is observable via the activity log on a human run.

- [ ] **Step 1: Read** how the existing "Pause all (F9)" control binds to `Coordinator.Paused` (`MainWindow.xaml` ~line 78 + `MainViewModel.cs`). Mirror that exactly.

- [ ] **Step 2: Add a "Dry run (don't press keys)" toggle** next to the Pause control, two-way bound through to `Coordinator.DryRun`. When on, matches log `WouldFire` instead of pressing — the existing activity panel shows them live. Use the same binding/command shape as `Paused`.

- [ ] **Step 3: Build to verify it compiles** — `dotnet build rororo-ur-ocr.csproj -o "C:/Users/estev/AppData/Local/Temp/urocr-sdd/t5"` → 0 errors.

- [ ] **Step 4: Commit** — `git add UI/MainWindow.xaml UI/MainViewModel.cs PluginRuntime.cs && git commit -m "feat(ui): global dry-run toggle (log-only, no keypress)"`. Report: compile-verified; live behavior needs a human run.

---

## Out of scope (this plan)

- Per-trigger "Test" button (we chose the global toggle).
- Expose `RegionAverage` in the color dialog; `DpiGuard` warn→disable (slice 2).
- Window-anchored coordinates (phase 2).
- The bridge client + `SequencePlayer` re-entry guard (separate efforts; the guard waits for rororo-ur-task PR #6 to merge).

## Self-Review

- **Spec coverage:** §4a match meter → Tasks 1, 3, 4; §4b dry-run → Task 2 (global, per §9 decision) + Task 5. Slice 2 / phase 2 correctly out of scope.
- **Placeholders:** Tasks 1-3 carry full code. Tasks 2/4/5 intentionally direct the implementer to read the existing test harness / UI files rather than guess their internals — the exact in-repo source is named in each case.
- **Type consistency:** `ColorMatchResult(Rgb Sampled, double Distance, bool Matched)` and `IColorMatchEngine.Evaluate` are produced in Task 1 and consumed in Task 3 (PreviewEvaluator) and Task 4 (UI); `TriggerCoordinator.DryRun` + `ActivityKind.WouldFire` produced in Task 2, consumed in Task 5.
