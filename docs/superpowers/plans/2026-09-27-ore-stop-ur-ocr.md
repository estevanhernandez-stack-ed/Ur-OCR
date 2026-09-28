# Ore Stop (Ur OCR half) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ur OCR watches an eight-spot ring around the character, knows which mine layer it is on, and fires Ur Task's "Mine spot" macro for any spot that is not that layer's rock, plus a rock cap (Go to Top after 5 minutes on one layer) and a camera rule (Camera top-down after 10 s with no layer).

**Architecture:** Four additions to the existing colour-trigger engine, no new bridge surface. (1) `ColorCriteria.NoneOf`, a "near none of these colours" check. (2) A ring: a `RingDefinition` (layers and their rock colours) stored next to the triggers, eight colour triggers marked with `RingSpot`, and a `RingTracker` that votes the current layer each tick; ring spots are judged against that layer's rock and fire in ring order, one per tick. (3) `Trigger.HoldForMs` (dwell) and a new `TriggerMode.Layer` whose match is read from the tracker. (4) Busy re-arm: a `busy` refusal from Ur Task keeps the trigger armed and retries after its cooldown. The ring is not hand-authored: a capture sweep (tools in `tools/`) produces a measured-values JSON, and `RoRoRo.UrOcr.exe --import-ring` turns it into triggers.

**Tech Stack:** C# / .NET 10 (`net10.0-windows10.0.19041.0`), WPF, System.Drawing, System.Text.Json, xUnit 2.9.2. PowerShell 7 (`pwsh`) for the sweep tools.

**Spec:** `../rororo-ur-task/docs/superpowers/specs/2026-09-27-ore-stop-loop-design.md` (sibling repo), sections "Ur OCR changes", "The ring", "Failure and edge cases", "Testing".

## Global Constraints

- **Two measured files, one source for the ring (controller ruling, 2026-09-27):** the Ur Task plan keeps its own `../rororo-ur-task/docs/reference/events/macros/space-mine-ore-stop/measured.json` (Auto Mine, Go to Top, camera) in a different shape. The file here is the source for the ring: `spots` (order, name, x, y) and `box`. Task 9 copies those into the Ur Task file's `ring.spots` and `ring.box`, and also measures live and writes there the `camera` group and the Go to Top colour check (`goToTop.check`), then runs its `generate.ps1`, commits there, and installs the macros, so the hold points in the `Mine spot` macros and the watched spots are the same pixels.
- **One client size for both files (controller ruling):** Ur OCR's `recordedClientW` x `recordedClientH` and `scalePercent` must equal the Ur Task file's `client.w` x `client.h` and `client.displayScale` (800x599 at 100%). Every Ur Task macro point (pickaxe, dot box, Go to Top) was measured at that size. Task 9 checks it live before the sweep and stops if it differs.
- **Paths:** every path in this plan is relative to the Ur-OCR repo root; the sibling repo is `..\rororo-ur-task`. Run every command from the Ur-OCR root and check with `git rev-parse --show-toplevel` after any `cd`. No absolute user-profile path goes into any committed file, this plan included.
- **Branch:** `feat/ore-stop` is already created from `fix/color-pick-box` (local, unpushed, commit `dd4f041`), so its pick-point box, `Other` colour and `ColorNamer` work is included. This plan's code depends on that commit (`SampleBox`, `PickPoint`, `ColorCriteria.Other`, `ColorMatcher.AverageBox`, `ColorNamer`). If Este later drops or rewrites `fix/color-pick-box`, rebase `feat/ore-stop` onto whatever replaces it.
- **Build:** `dotnet build rororo-ur-ocr.csproj` from the repo root. There is no tracked `.sln`; do not create one.
- **Tests:** `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj` (CI runs exactly this). The IntegrationTests project is not run. Baseline: **120 passed** (confirmed on `feat/ore-stop` at `dd4f041`, 2026-09-27). Every task ends with the full suite green.
- **No new NuGet packages.**
- **Commits:** conventional commits (`feat(scope): ...`, `fix(...)`, `docs(...)`, `chore(...)`), sentence case after the colon, no emoji. Every commit message ends with a blank line then `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **Never `git add`** the untracked `AGENTS.md`, `CLAUDE.md` (GitNexus output) or `.gitnexus/`. Add files by path, never `git add -A` / `git add .`.
- **Bridge contract unchanged:** Ur OCR calls `RunMacro` with a macro **id** (Ur Task's `MacroRunInvoker` matches `request.MacroId` against `Macro.Id` only), contract `1.0`, targets `foreground`. The importer resolves the macro names in the measured file to ids.
- **Ur Task's busy refusal** is `RunMacroResponse { ok: false, reason: "busy" }` ("A sequence is already running."). Ur Task acks `RunMacro` as soon as it accepts or refuses; it does not wait for the macro to finish.
- **Foreground only:** Ur OCR evaluates the foreground alt only, as today. Nothing here reads a background window.
- **Ring:** eight spots, ring order N, NE, E, SE, S, SW, W, NW = order 0 to 7. Each spot is a 5x5 sample box (`SampleBox()` defaults: offset -2, -2, size 5x5).
- **Rock cap default:** 300000 ms, per loop (per measured file / ring). **Camera rule:** 10000 ms with no layer.
- **Colours are measured in the sweep, never guessed.** Only test fixtures use made-up colours.
- **No notifications.** Output goes to the activity panel and `%LOCALAPPDATA%\626Labs\rororo-ur-ocr\logs\ur-ocr.log`.
- **Copy:** sentence case, second person where it addresses the user, no emoji, em-dashes minimal.
- **Storage is additive:** `triggers.json` stays schema 2. New keys (`rings`, `ring`, `layer`, `holdForMs`, `noneOf`) are omitted when null/zero (except `rings`, written as `[]`). Legacy files load unchanged. Downgrading to 0.4.0 after a layer trigger is saved is not supported (0.4.0 cannot parse `"mode": "layer"` and would back the file up as corrupted).
- **Versioning:** `rororo-ur-ocr.csproj` `<Version>` and `manifest.json` `"version"` agree. The last code task (Task 8) bumps both 0.4.0 to 0.5.0.
- **Measured-values file shape** (schema 1), shared with the Ur Task plan's macro generator. Canonical copy committed at `docs/reference/ore-stop/mine8.measured.json` (Task 9). Values below are the synthetic demo geometry, not Mine #8:

```json
{
  "schema": 1,
  "ringId": "mine8",
  "name": "Mine #8",
  "recordedClientW": 800,
  "recordedClientH": 599,
  "scalePercent": 100,
  "toleranceRgb": 30,
  "minLayerSpots": 3,
  "box": { "offsetX": -2, "offsetY": -2, "w": 5, "h": 5 },
  "spots": [
    { "order": 0, "name": "N",  "x": 400, "y": 260, "macro": "Mine spot N" },
    { "order": 1, "name": "NE", "x": 440, "y": 260, "macro": "Mine spot NE" },
    { "order": 2, "name": "E",  "x": 440, "y": 300, "macro": "Mine spot E" },
    { "order": 3, "name": "SE", "x": 440, "y": 340, "macro": "Mine spot SE" },
    { "order": 4, "name": "S",  "x": 400, "y": 340, "macro": "Mine spot S" },
    { "order": 5, "name": "SW", "x": 360, "y": 340, "macro": "Mine spot SW" },
    { "order": 6, "name": "W",  "x": 360, "y": 300, "macro": "Mine spot W" },
    { "order": 7, "name": "NW", "x": 360, "y": 260, "macro": "Mine spot NW" }
  ],
  "ignore": [ { "r": 135, "g": 206, "b": 235 } ],
  "layers": [
    { "name": "navy", "rock": [ { "r": 30, "g": 30, "b": 90 } ] },
    { "name": "grey", "rock": [ { "r": 120, "g": 120, "b": 120 } ] }
  ],
  "rockCap": { "holdForMs": 300000, "macro": "Go to Top", "cooldownMs": 5000 },
  "camera": { "holdForMs": 10000, "macro": "Camera top-down", "cooldownMs": 5000 },
  "spotCooldownMs": 1000
}
```

  Spot `x`, `y` are game-area pixels at `recordedClientW` x `recordedClientH`, recorded at 100% display scale. `ignore` holds sky/empty and the character's own colours; `layers[].rock` each layer's plain rock. Macro names are Ur Task macro **names** (the importer maps them to ids).

## Review Focus

1. **Tabbing away from Roblox:** the ring reads "not visible" (Unknown), never "no layer", so Camera top-down does not fire and every hold resets. Pinned by `Tabbing_away_is_not_no_layer` (Task 6).
2. **No layer current** (camera knocked, a ring full of ore, a loading screen): no ring spot fires at all, instead of all eight. Pinned by `No_layer_means_no_ore` (Task 5).
3. **Ur Task not running, or an unknown macro id:** the edge is spent as today; only `busy` retries, so a closed Ur Task does not get a pipe call every cooldown forever. Pinned by `Other_refusals_spend_the_edge` (Task 4).
4. **A broken ring in `triggers.json`** (hand edit, missing ring, empty layer): one error line per trigger, not one per tick, and nothing fires. Pinned by `A_spot_on_a_missing_ring_logs_one_error` (Task 5).
5. **Importing while Ur OCR is running, or before Ur Task has the macros:** the import refuses and writes nothing. Pinned by `Refuses_while_Ur_OCR_is_running` and `Missing_macros_exit_2_and_write_nothing` (Task 8).

## Decisions this plan makes where the spec is open

- **NoneOf and TargetRgb:** `NoneOf != null` switches the check to none-of mode and `TargetRgb` is ignored (it is a required positional value; the importer writes black). `NoneOf` with `Other` is invalid. An empty `NoneOf` is valid only on a ring spot (the layer supplies colours). "Within tolerance" is inclusive (`d <= tol` is near), matching the existing target check, so none-of matches only when the nearest listed colour is `> tol`. An empty combined list never matches.
- **Where the layer sets live:** on the ring, not copied into each of the 8 triggers. `RingDefinition.Layers[]` holds each layer's rock; each spot trigger's own `NoneOf` holds its ignore colours (sky, empty, the character). A spot's effective list is `spot.NoneOf + currentLayer.Rock`. Runtime layer state lives in `RingTracker`, owned by `TriggerCoordinator` and exposed as `TriggerCoordinator.Rings`.
- **"Most ring spots":** plurality with a floor. The current layer is the one with the most spots within tolerance of its rock, provided at least `MinLayerSpots` (default 3, measured file can change it) agree. Ties keep the previous layer if it is among the tied, else the first in list order. A ring full of ore therefore still reads its layer from three rock spots.
- **Layer boundary:** at a boundary, rock of the neighbouring layer is "not current rock" and reads as ore. That costs one short mine (the hold ends when the block breaks), never a stall. Pinned by a test so the behaviour is deliberate.
- **Dwell repeats:** a trigger with `HoldForMs` restarts its hold when it fires, so it fires again after another full hold if the condition still holds (Go to Top again after another 5 minutes stuck on the top layer; Camera top-down again every 10 s while no layer reads). The hold is keyed: for "same layer" a layer change restarts it.
- **Rock cap and camera rule are triggers** (`TriggerMode.Layer`, conditions `SameLayer` / `NoLayer`) with `HoldForMs`, not special cases.
- **The rock cap's reason (spec decision 7, amended by controller ruling):** Ur OCR logs why it sent the account up, `Went to top: 5 minutes on the grey layer`, to `ur-ocr.log` when the rock cap fires; Ur Task logs the Go to Top playback's ending. `RunMacro` carries no reason, so Ur Task cannot say why.
- **A held trigger never spends its edge on the cooldown:** if a hold completes while the trigger is still cooling down (a hold shorter than the cooldown), it stays armed and fires the tick the cooldown ends.
- **The editor's live preview skips ring spots.** A ring spot's colour check only means something against the ring's current layer, which the editor does not have, so the preview shows nothing for it. Named in the README's Known limitations.
- **Busy re-arm:** a busy refusal is logged as `Busy`, does not count as a fire (`HitCount`, `LastFiredAt` untouched), and sets a per-trigger retry time of `now + CooldownMs`. Any other refusal is spent as before.
- **Priority:** per tick, at most one spot per ring calls `RunMacro`. A spot waiting on its busy retry also holds the ring's turn, so a higher-order spot does not jump the queue. Deferred spots stay armed and go on the next tick.
- **The measured file:** one JSON per mine, schema above, committed in this repo at `docs/reference/ore-stop/`. The Ur Task generator reads the same file. Macro names default to `Mine spot N`, `Mine spot NE`, ... (compass names), `Go to Top`, `Camera top-down`; the Ur Task plan must generate macros with exactly these names.
- **Import path:** a headless CLI switch on the plugin exe (`--import-ring`), because `TriggerStore` loads once and a running Ur OCR rewrites `triggers.json` from memory. Re-import replaces by stable ids, it never duplicates.

## File map

| File | Status | Responsibility |
| --- | --- | --- |
| `Storage/Trigger.cs` | modify | `ColorCriteria.NoneOf` + `Validate`; `TriggerMode.Layer`; `Trigger.Ring`, `Trigger.Layer`, `Trigger.HoldForMs`; `TriggersFile.Rings` |
| `Storage/Ring.cs` | create | `LayerDefinition`, `RingDefinition`, `RingSpot`, `LayerCondition`, `LayerCriteria` |
| `Storage/TriggerValidation.cs` | create | Per-trigger and per-ring validation, one message per problem |
| `Storage/TriggerStore.cs` | modify | `Rings`, `Upsert`, `UpsertRing`, null-safe `rings` on load |
| `Storage/MeasuredRing.cs` | create | Measured-values file model, loader, validation |
| `Storage/RingImporter.cs` | create | Measured file to ring + 10 triggers, stable ids, macro name resolution |
| `RingImportCommand.cs` | create | `--import-ring` headless command |
| `Program.cs` | modify | Route `--import-ring` before the WPF app starts |
| `Engine/ColorMatcher.cs` | modify | `Sample`, `Judge` (none-of), public `Distance` |
| `Engine/ColorMatchResult.cs` | modify | `Nearest` |
| `Engine/RingTracker.cs` | create | Layer vote, current layer per ring, `RingState` |
| `Engine/ActivityLog.cs` | modify | `ActivityKind.Busy`, `Deferred`, `LayerChanged`, `Holding` |
| `Engine/TriggerCoordinator.cs` | modify / rewrite | Busy re-arm; read, ring, decide phases; ring order; dwell; layer triggers; `Rings`; `diag` sink |
| `Ipc/MacroRunClient.cs` | modify | Correct the stale "acks after the macro finishes" comment |
| `PluginRuntime.cs` | modify | Pass `DiagLog.Write` to the coordinator |
| `Engine/PreviewEvaluator.cs` | modify | No live preview for ring spots |
| `tools/ring-sweep.ps1`, `tools/ring-sample.ps1`, `tools/ring-fit.ps1` | create | Capture sweep, per-spot sampling and contact sheets, colour fit |
| `rororo-ur-ocr.csproj`, `manifest.json`, `CHANGELOG.md`, `README.md` | modify | 0.5.0 |
| `docs/reference/ore-stop/mine8.measured.json` | create (Task 9) | Measured Mine #8 ring |
| `tests/RoRoRo.UrOcr.Tests/...` | create / modify | Tests named in each task |

---

### Task 1: None-of colour checks

**Files:**
- Modify: `Storage/Trigger.cs` (the `ColorCriteria` record)
- Modify: `Engine/ColorMatcher.cs` (whole file)
- Modify: `Engine/ColorMatchResult.cs` (whole file)
- Test: `tests/RoRoRo.UrOcr.Tests/Engine/ColorMatcherNoneOfTests.cs`

**Interfaces:**
- Consumes: `Rgb`, `PickPoint`, `SampleBox` (`IsValid`, `MaxSide`), `ColorCriteria` (from `fix/color-pick-box`), `TriggerJsonOptions.Default` (internal, visible to tests).
- Produces:
  - `ColorCriteria(... , Rgb? Other = null, IReadOnlyList<Rgb>? NoneOf = null)`, `ColorCriteria.MaxTolerance = 442`, `bool ColorCriteria.IsNoneOf`, `string? ColorCriteria.Validate(bool layerSupplied = false)`, `static bool ColorCriteria.InRange(Rgb c)`.
  - `static Rgb ColorMatcher.Sample(Bitmap bmp, ColorCriteria c, RegionRect? recordedRegion)`.
  - `static ColorMatchResult ColorMatcher.Judge(Rgb sampled, ColorCriteria c, IReadOnlyList<Rgb>? extraNoneOf = null)`.
  - `static double ColorMatcher.Distance(Rgb a, Rgb b)` (was private).
  - `ColorMatchResult(Rgb Sampled, double Distance, bool Matched, double? DistanceToOther = null, Rgb? Nearest = null)`.

- [x] **Step 1: Create the branch** (done 2026-09-27: `feat/ore-stop` was created from `fix/color-pick-box` at `dd4f041`, and this plan is committed on it as `docs(plan): ore stop, Ur OCR half`). Do not run `git switch -c` again; it fails because the branch exists. Only confirm where you are:

```powershell
git switch feat/ore-stop
git log --oneline -2
```

Expected: the log shows `docs(plan): ore stop, Ur OCR half` on top of `dd4f041 fix(color): check the picked point with the same box the picker averaged`. `git status --short` shows only the untracked `AGENTS.md`, `CLAUDE.md` and `.gitnexus/` (leave them).

- [ ] **Step 2: Write the failing tests**

Create `tests/RoRoRo.UrOcr.Tests/Engine/ColorMatcherNoneOfTests.cs`:

```csharp
using System.Drawing;
using System.Text.Json;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

/// <summary>
/// "None of" checks: match when the sample is more than tolerance from every
/// listed colour. The ore-stop ring lists a layer's rock plus sky and the
/// character, so anything else (ore) matches.
/// </summary>
public class ColorMatcherNoneOfTests
{
    private static readonly Rgb Navy = new(30, 30, 90);
    private static readonly Rgb Grey = new(120, 120, 120);
    private static readonly Rgb Sky = new(135, 206, 235);
    private static readonly Rgb Orange = new(240, 160, 40);

    private static ColorCriteria NoneOf(int tolerance, params Rgb[] colours) =>
        new(new Rgb(0, 0, 0), tolerance, ColorSamplingMode.SinglePixel, NoneOf: colours);

    private static Bitmap Solid(Rgb c)
    {
        var bmp = new Bitmap(9, 9);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(c.R, c.G, c.B));
        return bmp;
    }

    [Fact]
    public void Matches_when_far_from_every_listed_colour()
    {
        var r = ColorMatcher.Judge(Orange, NoneOf(30, Navy, Grey, Sky));

        Assert.True(r.Matched);
        Assert.Equal(Grey, r.Nearest);           // orange to grey is about 149.7, the nearest of the three
        Assert.Equal(149.7, r.Distance, 1);
    }

    [Fact]
    public void No_match_when_within_tolerance_of_any_listed_colour()
    {
        var nearGrey = new Rgb(125, 118, 122);

        var r = ColorMatcher.Judge(nearGrey, NoneOf(30, Navy, Grey, Sky));

        Assert.False(r.Matched);
        Assert.Equal(Grey, r.Nearest);
        Assert.True(r.Distance < 30);
    }

    [Fact]
    public void Exactly_at_tolerance_counts_as_near()
    {
        // (120,120,150) is exactly 30 from grey.
        var r = ColorMatcher.Judge(new Rgb(120, 120, 150), NoneOf(30, Grey));

        Assert.False(r.Matched);
        Assert.Equal(30, r.Distance, 3);
    }

    [Fact]
    public void Target_is_ignored_in_none_of_mode()
    {
        var targetInList = new ColorCriteria(Orange, 30, ColorSamplingMode.SinglePixel, NoneOf: new[] { Orange });
        var targetElsewhere = new ColorCriteria(Navy, 30, ColorSamplingMode.SinglePixel, NoneOf: new[] { Grey });

        Assert.False(ColorMatcher.Judge(Orange, targetInList).Matched);
        Assert.True(ColorMatcher.Judge(Orange, targetElsewhere).Matched);
    }

    [Fact]
    public void Extra_colours_join_the_list()
    {
        var crit = NoneOf(30, Sky);

        Assert.True(ColorMatcher.Judge(Grey, crit).Matched);
        Assert.False(ColorMatcher.Judge(Grey, crit, extraNoneOf: new[] { Grey }).Matched);
    }

    [Fact]
    public void Extra_colours_are_ignored_by_a_target_check()
    {
        var crit = new ColorCriteria(Grey, 30, ColorSamplingMode.SinglePixel);

        Assert.True(ColorMatcher.Judge(Grey, crit, extraNoneOf: new[] { Grey }).Matched);
    }

    [Fact]
    public void An_empty_list_never_matches()
    {
        var crit = new ColorCriteria(new Rgb(0, 0, 0), 30, ColorSamplingMode.SinglePixel, NoneOf: Array.Empty<Rgb>());

        var r = ColorMatcher.Judge(Orange, crit);

        Assert.False(r.Matched);
        Assert.Null(r.Nearest);
    }

    [Fact]
    public void Evaluate_samples_the_box_then_applies_none_of()
    {
        using var bmp = Solid(Orange);
        var crit = new ColorCriteria(new Rgb(0, 0, 0), 30, ColorSamplingMode.SinglePixel,
            Point: new PickPoint(4, 4), Box: new SampleBox(), NoneOf: new[] { Navy, Grey });

        var r = new ColorMatcher().Evaluate(bmp, crit);

        Assert.True(r.Matched);
        Assert.Equal(Orange, r.Sampled);
    }

    [Fact]
    public void Sample_matches_what_Evaluate_saw()
    {
        using var bmp = Solid(Navy);
        var crit = NoneOf(30, Grey) with { Point = new PickPoint(4, 4), Box = new SampleBox() };

        Assert.Equal(Navy, ColorMatcher.Sample(bmp, crit, null));
    }

    [Fact]
    public void Legacy_target_criteria_are_valid()
    {
        Assert.Null(new ColorCriteria(Grey, 30, ColorSamplingMode.SinglePixel).Validate());
    }

    [Fact]
    public void None_of_with_an_other_colour_is_invalid()
    {
        var crit = new ColorCriteria(Grey, 30, ColorSamplingMode.SinglePixel, Other: Navy, NoneOf: new[] { Sky });

        Assert.Contains("other", crit.Validate(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_empty_none_of_list_is_invalid_unless_a_layer_supplies_colours()
    {
        var crit = NoneOf(30);

        Assert.NotNull(crit.Validate());
        Assert.Null(crit.Validate(layerSupplied: true));
    }

    [Fact]
    public void A_listed_colour_outside_0_to_255_is_invalid()
    {
        Assert.NotNull(NoneOf(30, new Rgb(256, 0, 0)).Validate());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(443)]
    public void Tolerance_outside_0_to_442_is_invalid(int tolerance)
    {
        Assert.NotNull(NoneOf(tolerance, Grey).Validate());
    }

    [Fact]
    public void An_oversized_box_is_invalid()
    {
        var crit = NoneOf(30, Grey) with { Point = new PickPoint(4, 4), Box = new SampleBox(W: 10) };

        Assert.NotNull(crit.Validate());
    }

    [Fact]
    public void None_of_round_trips_through_json()
    {
        var json = JsonSerializer.Serialize(NoneOf(30, Navy, Sky), TriggerJsonOptions.Default);

        Assert.Contains("\"noneOf\"", json);
        var back = JsonSerializer.Deserialize<ColorCriteria>(json, TriggerJsonOptions.Default)!;
        Assert.Equal(new[] { Navy, Sky }, back.NoneOf);
    }

    [Fact]
    public void Target_criteria_json_has_no_none_of_key()
    {
        var json = JsonSerializer.Serialize(new ColorCriteria(Grey, 30, ColorSamplingMode.SinglePixel), TriggerJsonOptions.Default);

        Assert.DoesNotContain("noneOf", json, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~ColorMatcherNoneOfTests"`
Expected: build FAILS with errors such as `'ColorCriteria' does not contain a definition for 'NoneOf'` and `'ColorMatcher' does not contain a definition for 'Judge'`.

- [ ] **Step 4: Add `NoneOf` and validation to `ColorCriteria`**

In `Storage/Trigger.cs`, replace the `ColorCriteria` doc comment and record (from `/// <summary>` above `public sealed record ColorCriteria(` down to its closing `Rgb? Other = null);`) with:

```csharp
/// <summary>
/// Colour trigger criteria. When <see cref="Box"/> is set, the check averages
/// that box around <see cref="Point"/>, the same box the picker averaged, and
/// <see cref="SamplingMode"/> is ignored (new triggers write SinglePixel so an
/// older build still loads them). Without a box, legacy behaviour: SinglePixel
/// reads the region centre, RegionAverage the whole region. <see cref="Other"/>
/// is the opposite state's colour: matched means within tolerance of the
/// target AND closer to it than to Other.
/// <para>
/// <see cref="NoneOf"/> turns the check around: when set, the trigger matches
/// when the sample is more than <see cref="ToleranceRgb"/> from EVERY listed
/// colour, and <see cref="TargetRgb"/> is ignored (any in-range value; the ring
/// importer writes black). NoneOf cannot be combined with Other. An empty list
/// is valid only on a ring spot, where the ring's current layer adds its rock.
/// </para>
/// </summary>
public sealed record ColorCriteria(
    Rgb TargetRgb, int ToleranceRgb, ColorSamplingMode SamplingMode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PickPoint? Point = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SampleBox? Box = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Rgb? Other = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Rgb>? NoneOf = null)
{
    /// <summary>The largest possible RGB distance, sqrt(3 x 255^2) = 441.67, rounded up.</summary>
    public const int MaxTolerance = 442;

    [JsonIgnore]
    public bool IsNoneOf => NoneOf is not null;

    /// <summary>Null when valid, else one sentence naming the problem.</summary>
    public string? Validate(bool layerSupplied = false)
    {
        if (ToleranceRgb < 0 || ToleranceRgb > MaxTolerance)
            return $"Tolerance must be 0 to {MaxTolerance}, not {ToleranceRgb}.";
        if (TargetRgb is null || !InRange(TargetRgb)) return "The target colour has a channel outside 0 to 255.";
        if (Other is not null && !InRange(Other)) return "The other colour has a channel outside 0 to 255.";
        if (Box is not null && !Box.IsValid) return $"The sample box must be 1 to {SampleBox.MaxSide} pixels a side.";
        if (NoneOf is null) return null;
        if (Other is not null) return "A none-of check cannot also have an other colour.";
        if (NoneOf.Count == 0 && !layerSupplied) return "A none-of check needs at least one colour.";
        foreach (var c in NoneOf)
            if (c is null || !InRange(c)) return "A none-of colour has a channel outside 0 to 255.";
        return null;
    }

    public static bool InRange(Rgb c) =>
        c.R is >= 0 and <= 255 && c.G is >= 0 and <= 255 && c.B is >= 0 and <= 255;
}
```

- [ ] **Step 5: Add `Nearest` to the result**

Replace the whole of `Engine/ColorMatchResult.cs` with:

```csharp
// Engine/ColorMatchResult.cs
using RoRoRo.UrOcr.Storage;
namespace RoRoRo.UrOcr.Engine;

/// <summary>
/// One colour check. Target check: Distance is to the target. None-of check:
/// Distance is to the nearest listed colour, which is <see cref="Nearest"/>
/// (null when the list was empty).
/// </summary>
public sealed record ColorMatchResult(Rgb Sampled, double Distance, bool Matched,
    double? DistanceToOther = null, Rgb? Nearest = null);
```

- [ ] **Step 6: Split sampling from judging in `ColorMatcher`**

Replace the whole of `Engine/ColorMatcher.cs` with:

```csharp
using System.Drawing;
using System.Drawing.Imaging;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

public sealed class ColorMatcher : IColorMatchEngine
{
    public ColorMatchResult Evaluate(Bitmap bmp, ColorCriteria c) => Judge(Sample(bmp, c, null), c);

    public ColorMatchResult Evaluate(Bitmap bmp, ColorCriteria c, RegionRect recordedRegion)
        => Judge(Sample(bmp, c, recordedRegion), c);

    public bool Matches(Bitmap bmp, ColorCriteria c) => Evaluate(bmp, c).Matched;

    /// <summary>
    /// The colour a check sees: the box around the pick point when the criteria
    /// have one, else legacy SinglePixel (region centre) or RegionAverage (whole
    /// region). recordedRegion scales the pick point for client-anchored captures.
    /// </summary>
    public static Rgb Sample(Bitmap bmp, ColorCriteria c, RegionRect? recordedRegion)
    {
        if (c.Box is not null && c.Point is not null)
            return AverageBox(bmp, ScalePoint(c.Point, bmp, recordedRegion), c.Box);

        var (r, g, b) = c.SamplingMode switch
        {
            ColorSamplingMode.SinglePixel => SamplePixel(bmp, bmp.Width / 2, bmp.Height / 2),
            ColorSamplingMode.RegionAverage => AverageRect(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height)),
            _ => throw new ArgumentOutOfRangeException()
        };
        return new Rgb(r, g, b);
    }

    /// <summary>
    /// Judges a sampled colour. Target check: within tolerance of the target (and
    /// closer to it than to Other, when set). None-of check (NoneOf set): more than
    /// tolerance from every listed colour and every colour in
    /// <paramref name="extraNoneOf"/>; Distance is to the nearest of them and
    /// Nearest names it. An empty combined list never matches: with nothing to
    /// compare against, "none of them" means nothing. extraNoneOf is ignored by a
    /// target check.
    /// </summary>
    public static ColorMatchResult Judge(Rgb sampled, ColorCriteria c, IReadOnlyList<Rgb>? extraNoneOf = null)
    {
        if (c.NoneOf is not null)
        {
            var nearest = double.PositiveInfinity;
            Rgb? nearestColour = null;
            foreach (var listed in c.NoneOf.Concat(extraNoneOf ?? Array.Empty<Rgb>()))
            {
                var d = Distance(sampled, listed);
                if (d < nearest) { nearest = d; nearestColour = listed; }
            }
            var noneNear = nearestColour is not null && nearest > c.ToleranceRgb;
            return new ColorMatchResult(sampled, nearest, noneNear, Nearest: nearestColour);
        }

        var distance = Distance(sampled, c.TargetRgb);
        double? toOther = c.Other is null ? null : Distance(sampled, c.Other);
        var matched = distance <= c.ToleranceRgb && (toOther is null || distance < toOther.Value);
        return new ColorMatchResult(sampled, distance, matched, toOther);
    }

    /// <summary>
    /// Average of <paramref name="box"/> around <paramref name="point"/>, clamped
    /// to the bitmap. The picker and the check both call this, so a trigger
    /// checks exactly the pixels it was picked from.
    /// </summary>
    public static Rgb AverageBox(Bitmap bmp, PickPoint point, SampleBox box)
    {
        var x0 = Math.Clamp(point.X + box.OffsetX, 0, bmp.Width - 1);
        var y0 = Math.Clamp(point.Y + box.OffsetY, 0, bmp.Height - 1);
        var x1 = Math.Clamp(point.X + box.OffsetX + box.W, x0 + 1, bmp.Width);
        var y1 = Math.Clamp(point.Y + box.OffsetY + box.H, y0 + 1, bmp.Height);
        var (r, g, b) = AverageRect(bmp, Rectangle.FromLTRB(x0, y0, x1, y1));
        return new Rgb(r, g, b);
    }

    /// <summary>Euclidean RGB distance, 0 to about 441.7.</summary>
    public static double Distance(Rgb a, Rgb b)
    {
        var dr = a.R - b.R;
        var dg = a.G - b.G;
        var db = a.B - b.B;
        return Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    // Client-anchored captures come back scaled to the live client size; the
    // pick point was recorded against the stored region, so scale it too.
    private static PickPoint ScalePoint(PickPoint p, Bitmap bmp, RegionRect? recorded)
    {
        if (recorded is null || recorded.Width < 1 || recorded.Height < 1) return p;
        if (recorded.Width == bmp.Width && recorded.Height == bmp.Height) return p;
        return new PickPoint(
            (int)Math.Round(p.X * (double)bmp.Width / recorded.Width),
            (int)Math.Round(p.Y * (double)bmp.Height / recorded.Height));
    }

    private static (int r, int g, int b) SamplePixel(Bitmap bmp, int x, int y)
    {
        var px = bmp.GetPixel(x, y);
        return (px.R, px.G, px.B);
    }

    private static (int r, int g, int b) AverageRect(Bitmap bmp, Rectangle rect)
    {
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            long sumR = 0, sumG = 0, sumB = 0;
            int stride = data.Stride;
            int bytes = stride * rect.Height;
            var buffer = new byte[bytes];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buffer, 0, bytes);
            for (int y = 0; y < rect.Height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < rect.Width; x++)
                {
                    int i = row + x * 4;
                    sumB += buffer[i];
                    sumG += buffer[i + 1];
                    sumR += buffer[i + 2];
                }
            }
            long pixels = rect.Width * (long)rect.Height;
            return ((int)(sumR / pixels), (int)(sumG / pixels), (int)(sumB / pixels));
        }
        finally { bmp.UnlockBits(data); }
    }
}
```

- [ ] **Step 7: Run the new tests, then the full suite**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~ColorMatcherNoneOfTests"`
Expected: PASS, 18 tests.

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj`
Expected: PASS, 138 tests (120 + 18), 0 failed.

- [ ] **Step 8: Commit**

```powershell
git add Storage/Trigger.cs Engine/ColorMatcher.cs Engine/ColorMatchResult.cs tests/RoRoRo.UrOcr.Tests/Engine/ColorMatcherNoneOfTests.cs
git commit -m "feat(color): none-of colour checks" -m "A colour trigger can list colours and match when the sample is more than tolerance from every one of them (noneOf), in place of one target. Target is ignored in that mode, and noneOf cannot be combined with other. ColorMatcher splits Sample from Judge so the ore-stop ring can judge one sample against its current layer's rock." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Ring storage and validation

**Files:**
- Create: `Storage/Ring.cs`
- Create: `Storage/TriggerValidation.cs`
- Modify: `Storage/Trigger.cs` (`TriggerMode`, `Trigger`, `TriggersFile`)
- Modify: `Storage/TriggerStore.cs`
- Test: `tests/RoRoRo.UrOcr.Tests/Storage/RingStorageTests.cs`
- Test: `tests/RoRoRo.UrOcr.Tests/Storage/TriggerValidationTests.cs`

**Interfaces:**
- Consumes: `ColorCriteria.Validate(bool layerSupplied)`, `ColorCriteria.InRange(Rgb)` (Task 1).
- Produces:
  - `record LayerDefinition(string Name, IReadOnlyList<Rgb> Rock)`
  - `record RingDefinition(string Id, string Name, IReadOnlyList<LayerDefinition> Layers, int MinLayerSpots = 3)`
  - `record RingSpot(string RingId, int Order)`
  - `enum LayerCondition { SameLayer, NoLayer }`, `record LayerCriteria(string RingId, LayerCondition Condition)`
  - `enum TriggerMode { Text, Color, Layer }`
  - `Trigger.Ring` (`RingSpot?`), `Trigger.Layer` (`LayerCriteria?`), `Trigger.HoldForMs` (`int`, default 0)
  - `TriggersFile.Rings` (`List<RingDefinition>`)
  - `TriggerStore.Rings` (`IReadOnlyList<RingDefinition>`), `void TriggerStore.Upsert(Trigger t)`, `void TriggerStore.UpsertRing(RingDefinition ring)`
  - `static class TriggerValidation`: `const int MaxHoldForMs = 3_600_000`, `string? Validate(Trigger t, IReadOnlyList<RingDefinition> rings)`, `string? Validate(RingDefinition ring)`, `RingDefinition? Find(IReadOnlyList<RingDefinition> rings, string id)`

- [ ] **Step 1: Write the failing tests**

Create `tests/RoRoRo.UrOcr.Tests/Storage/RingStorageTests.cs`:

```csharp
using System.IO;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Storage;

public class RingStorageTests
{
    private static string TempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "urocr-tests", Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private static RingDefinition Ring() => new("mine8", "Mine #8", new[]
    {
        new LayerDefinition("navy", new[] { new Rgb(30, 30, 90) }),
        new LayerDefinition("grey", new[] { new Rgb(120, 120, 120), new Rgb(100, 100, 100) }),
    }, MinLayerSpots: 3);

    private static Trigger Spot(int order, Guid? id = null, string name = "spot") => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = name,
        Region = new RegionRect(0, 0, 9, 9),
        Mode = TriggerMode.Color,
        Color = new ColorCriteria(new Rgb(0, 0, 0), 30, ColorSamplingMode.SinglePixel,
            Point: new PickPoint(4, 4), Box: new SampleBox(), NoneOf: new[] { new Rgb(135, 206, 235) }),
        Keybind = new KeyCombo("F13", Array.Empty<string>()),
        Ring = new RingSpot("mine8", order),
    };

    [Fact]
    public void Rings_and_ring_spots_survive_a_reload()
    {
        var path = TempFile();
        var s = new TriggerStore(path);
        s.UpsertRing(Ring());
        s.Add(Spot(2));

        var s2 = new TriggerStore(path);

        var ring = Assert.Single(s2.Rings);
        Assert.Equal("mine8", ring.Id);
        Assert.Equal(3, ring.MinLayerSpots);
        Assert.Equal(2, ring.Layers.Count);
        Assert.Equal(new[] { new Rgb(120, 120, 120), new Rgb(100, 100, 100) }, ring.Layers[1].Rock);
        var t = Assert.Single(s2.All);
        Assert.Equal(new RingSpot("mine8", 2), t.Ring);
        Assert.Equal(new[] { new Rgb(135, 206, 235) }, t.Color!.NoneOf);
    }

    [Fact]
    public void Layer_trigger_and_hold_survive_a_reload()
    {
        var path = TempFile();
        var s = new TriggerStore(path);
        s.Add(new Trigger
        {
            Id = Guid.NewGuid(), Name = "rock cap",
            Region = new RegionRect(0, 0, 800, 599), Mode = TriggerMode.Layer,
            Layer = new LayerCriteria("mine8", LayerCondition.SameLayer), HoldForMs = 300_000,
            Keybind = new KeyCombo("F13", Array.Empty<string>()),
        });

        var t = Assert.Single(new TriggerStore(path).All);

        Assert.Equal(TriggerMode.Layer, t.Mode);
        Assert.Equal(new LayerCriteria("mine8", LayerCondition.SameLayer), t.Layer);
        Assert.Equal(300_000, t.HoldForMs);
        Assert.Null(t.Color);
    }

    [Fact]
    public void A_file_without_rings_loads_with_none()
    {
        var path = TempFile();
        File.WriteAllText(path, """
        {
          "schemaVersion": 2,
          "triggers": [
            { "id": "11111111-1111-1111-1111-111111111111", "name": "t",
              "enabled": true, "region": { "x": 10, "y": 20, "width": 30, "height": 40 },
              "mode": "color", "accountAware": true, "coordSpace": "screen",
              "color": { "targetRgb": { "r": 1, "g": 2, "b": 3 }, "toleranceRgb": 10, "samplingMode": "singlePixel" },
              "keybind": { "key": "F", "modifiers": [] } }
          ]
        }
        """);

        var s = new TriggerStore(path);

        Assert.Empty(s.Rings);
        var t = Assert.Single(s.All);
        Assert.Null(t.Ring);
        Assert.Null(t.Layer);
        Assert.Equal(0, t.HoldForMs);
        Assert.Null(t.Color!.NoneOf);
    }

    [Fact]
    public void Zero_hold_and_no_ring_are_not_written()
    {
        var path = TempFile();
        var s = new TriggerStore(path);
        s.Add(new Trigger
        {
            Id = Guid.NewGuid(), Name = "plain",
            Region = new RegionRect(0, 0, 10, 10), Mode = TriggerMode.Color,
            Color = new ColorCriteria(new Rgb(0, 0, 0), 5, ColorSamplingMode.SinglePixel),
            Keybind = new KeyCombo("A", Array.Empty<string>()),
        });

        var json = File.ReadAllText(path);

        Assert.DoesNotContain("holdForMs", json);
        Assert.DoesNotContain("\"ring\"", json);
        Assert.DoesNotContain("\"layer\"", json);
    }

    [Fact]
    public void UpsertRing_replaces_by_id_ignoring_case()
    {
        var s = new TriggerStore(TempFile());
        s.UpsertRing(Ring());
        s.UpsertRing(Ring() with { Id = "MINE8", Name = "renamed" });

        var r = Assert.Single(s.Rings);
        Assert.Equal("renamed", r.Name);
    }

    [Fact]
    public void Upsert_adds_then_replaces_by_id()
    {
        var s = new TriggerStore(TempFile());
        var id = Guid.NewGuid();
        s.Upsert(Spot(0, id, "first"));
        s.Upsert(Spot(0, id, "second"));

        var t = Assert.Single(s.All);
        Assert.Equal("second", t.Name);
    }
}
```

Create `tests/RoRoRo.UrOcr.Tests/Storage/TriggerValidationTests.cs`:

```csharp
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Storage;

public class TriggerValidationTests
{
    private static readonly Rgb Sky = new(135, 206, 235);

    private static RingDefinition Ring(params LayerDefinition[] layers) => new("mine8", "Mine #8",
        layers.Length > 0 ? layers : new[] { new LayerDefinition("navy", new[] { new Rgb(30, 30, 90) }) });

    private static IReadOnlyList<RingDefinition> Rings(params RingDefinition[] rings) => rings;

    private static Trigger Colour(ColorCriteria? c = null, RingSpot? ring = null) => new()
    {
        Id = Guid.NewGuid(), Name = "t",
        Region = new RegionRect(0, 0, 9, 9), Mode = TriggerMode.Color,
        Color = c ?? new ColorCriteria(new Rgb(0, 0, 0), 30, ColorSamplingMode.SinglePixel),
        Keybind = new KeyCombo("F13", Array.Empty<string>()),
        Ring = ring,
    };

    private static ColorCriteria NoneOf(params Rgb[] colours) =>
        new(new Rgb(0, 0, 0), 30, ColorSamplingMode.SinglePixel, NoneOf: colours);

    private static Trigger LayerTrigger(LayerCriteria? criteria, int hold = 0) => new()
    {
        Id = Guid.NewGuid(), Name = "layer",
        Region = new RegionRect(0, 0, 1, 1), Mode = TriggerMode.Layer,
        Layer = criteria, HoldForMs = hold,
        Keybind = new KeyCombo("F13", Array.Empty<string>()),
    };

    [Fact]
    public void A_plain_colour_trigger_is_valid()
    {
        Assert.Null(TriggerValidation.Validate(Colour(), Rings()));
    }

    [Fact]
    public void A_colour_trigger_without_criteria_is_invalid()
    {
        var t = Colour();
        t.Color = null;

        Assert.NotNull(TriggerValidation.Validate(t, Rings()));
    }

    [Fact]
    public void A_ring_spot_on_an_unknown_ring_is_invalid()
    {
        var t = Colour(NoneOf(Sky), new RingSpot("nope", 0));

        Assert.Contains("nope", TriggerValidation.Validate(t, Rings(Ring())));
    }

    [Fact]
    public void A_ring_spot_must_be_a_none_of_check()
    {
        var t = Colour(ring: new RingSpot("mine8", 0));

        Assert.NotNull(TriggerValidation.Validate(t, Rings(Ring())));
    }

    [Fact]
    public void A_ring_spot_may_have_an_empty_none_of_list()
    {
        var t = Colour(NoneOf(), new RingSpot("mine8", 0));

        Assert.Null(TriggerValidation.Validate(t, Rings(Ring())));
    }

    [Fact]
    public void A_negative_ring_order_is_invalid()
    {
        var t = Colour(NoneOf(Sky), new RingSpot("mine8", -1));

        Assert.NotNull(TriggerValidation.Validate(t, Rings(Ring())));
    }

    [Fact]
    public void A_spot_on_an_invalid_ring_reports_the_ring_problem()
    {
        var broken = Ring(new LayerDefinition("navy", Array.Empty<Rgb>()));
        var t = Colour(NoneOf(Sky), new RingSpot("mine8", 0));

        Assert.Contains("rock", TriggerValidation.Validate(t, Rings(broken)));
    }

    [Fact]
    public void A_layer_trigger_needs_its_ring()
    {
        Assert.Null(TriggerValidation.Validate(LayerTrigger(new LayerCriteria("mine8", LayerCondition.NoLayer)), Rings(Ring())));
        Assert.NotNull(TriggerValidation.Validate(LayerTrigger(new LayerCriteria("mine8", LayerCondition.NoLayer)), Rings()));
    }

    [Fact]
    public void A_layer_trigger_without_criteria_is_invalid()
    {
        Assert.NotNull(TriggerValidation.Validate(LayerTrigger(null), Rings(Ring())));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3_600_001)]
    public void A_hold_outside_0_to_an_hour_is_invalid(int hold)
    {
        var t = LayerTrigger(new LayerCriteria("mine8", LayerCondition.SameLayer), hold);

        Assert.NotNull(TriggerValidation.Validate(t, Rings(Ring())));
    }

    [Fact]
    public void A_text_trigger_cannot_be_a_ring_spot()
    {
        var t = new Trigger
        {
            Id = Guid.NewGuid(), Name = "text", Region = new RegionRect(0, 0, 9, 9),
            Mode = TriggerMode.Text, Text = new TextCriteria("ore", false, TextMatchType.Contains),
            Keybind = new KeyCombo("F13", Array.Empty<string>()), Ring = new RingSpot("mine8", 0),
        };

        Assert.NotNull(TriggerValidation.Validate(t, Rings(Ring())));
    }

    [Fact]
    public void A_ring_without_layers_is_invalid()
    {
        Assert.NotNull(TriggerValidation.Validate(new RingDefinition("mine8", "Mine #8", Array.Empty<LayerDefinition>())));
    }

    [Fact]
    public void Duplicate_layer_names_are_invalid()
    {
        var ring = Ring(new LayerDefinition("navy", new[] { new Rgb(1, 1, 1) }), new LayerDefinition("NAVY", new[] { new Rgb(2, 2, 2) }));

        Assert.NotNull(TriggerValidation.Validate(ring));
    }

    [Fact]
    public void A_min_layer_spots_below_1_is_invalid()
    {
        Assert.NotNull(TriggerValidation.Validate(Ring() with { MinLayerSpots = 0 }));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~RingStorageTests|FullyQualifiedName~TriggerValidationTests"`
Expected: build FAILS: `The type or namespace name 'RingDefinition' could not be found`, `'TriggerMode' does not contain a definition for 'Layer'`.

- [ ] **Step 3: Create `Storage/Ring.cs`**

```csharp
namespace RoRoRo.UrOcr.Storage;

/// <summary>One layer of a mine: the colours its plain rock shows at a ring spot.</summary>
public sealed record LayerDefinition(string Name, IReadOnlyList<Rgb> Rock);

/// <summary>
/// A ring of spot triggers around the character that share one layer reading.
/// The current layer is the one whose rock the most spots are within tolerance
/// of, provided at least <see cref="MinLayerSpots"/> spots agree. Stored in
/// triggers.json next to the triggers; spots point at it by <see cref="Id"/>.
/// </summary>
public sealed record RingDefinition(string Id, string Name, IReadOnlyList<LayerDefinition> Layers, int MinLayerSpots = 3);

/// <summary>Marks a colour trigger as a spot of a ring. Order is the ring order: lower goes first.</summary>
public sealed record RingSpot(string RingId, int Order);

public enum LayerCondition { SameLayer, NoLayer }

/// <summary>
/// Criteria of a <see cref="TriggerMode.Layer"/> trigger. SameLayer matches
/// while the ring is on a layer; with <see cref="Trigger.HoldForMs"/> that reads
/// "on the same layer for N ms" (a layer change restarts the hold). NoLayer
/// matches while the ring is visible but no layer is current.
/// </summary>
public sealed record LayerCriteria(string RingId, LayerCondition Condition);
```

- [ ] **Step 4: Extend `Trigger`, `TriggerMode` and `TriggersFile`**

In `Storage/Trigger.cs`, replace

```csharp
public enum TriggerMode { Text, Color }
```

with

```csharp
public enum TriggerMode { Text, Color, Layer }
```

In the same file, replace

```csharp
    public int CooldownMs { get; set; } = 2000;
```

with

```csharp
    public int CooldownMs { get; set; } = 2000;
    // Ore stop (0.5.0), all additive: absent keys load as null / 0.
    /// <summary>Set on the eight colour triggers of a ring.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public RingSpot? Ring { get; set; }
    /// <summary>Criteria of a <see cref="TriggerMode.Layer"/> trigger.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public LayerCriteria? Layer { get; set; }
    /// <summary>The match must hold unbroken this long before the trigger fires, and a fire
    /// starts a fresh hold. 0 = fire on the no-match to match edge, as before.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int HoldForMs { get; set; }
```

In the same file, replace

```csharp
public sealed class TriggersFile
{
    public int SchemaVersion { get; set; } = 2;
    public List<Trigger> Triggers { get; set; } = new();
}
```

with

```csharp
public sealed class TriggersFile
{
    public int SchemaVersion { get; set; } = 2;
    public List<RingDefinition> Rings { get; set; } = new();
    public List<Trigger> Triggers { get; set; } = new();
}
```

- [ ] **Step 5: Create `Storage/TriggerValidation.cs`**

```csharp
namespace RoRoRo.UrOcr.Storage;

/// <summary>
/// Whether a trigger or ring can run. Null means valid; otherwise one sentence
/// naming the first problem, fit for the activity log.
/// </summary>
public static class TriggerValidation
{
    public const int MaxHoldForMs = 3_600_000;

    public static string? Validate(Trigger t, IReadOnlyList<RingDefinition> rings)
    {
        if (t.HoldForMs < 0 || t.HoldForMs > MaxHoldForMs)
            return $"Hold must be 0 to {MaxHoldForMs} ms, not {t.HoldForMs}.";
        if (t.CooldownMs < 0) return "Cooldown cannot be negative.";

        switch (t.Mode)
        {
            case TriggerMode.Color:
                if (t.Color is null) return "A colour trigger needs colour criteria.";
                if (t.Ring is null) return t.Color.Validate();
                if (t.Ring.Order < 0) return "Ring order cannot be negative.";
                if (RingProblem(t.Ring.RingId, rings) is { } spotRing) return spotRing;
                if (t.Color.NoneOf is null) return "A ring spot must be a none-of check.";
                return t.Color.Validate(layerSupplied: true);

            case TriggerMode.Layer:
                if (t.Layer is null) return "A layer trigger needs layer criteria.";
                if (t.Ring is not null) return "A layer trigger cannot also be a ring spot.";
                return RingProblem(t.Layer.RingId, rings);

            case TriggerMode.Text:
                if (t.Text is null) return "A text trigger needs text criteria.";
                return t.Ring is null ? null : "Only colour triggers can be ring spots.";

            default:
                return "Unknown trigger mode.";
        }
    }

    public static string? Validate(RingDefinition ring)
    {
        if (string.IsNullOrWhiteSpace(ring.Id)) return "A ring needs an id.";
        if (ring.MinLayerSpots < 1) return "A ring needs minLayerSpots of at least 1.";
        if (ring.Layers is null || ring.Layers.Count == 0) return "A ring needs at least one layer.";
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in ring.Layers)
        {
            if (layer is null || string.IsNullOrWhiteSpace(layer.Name)) return "Every layer needs a name.";
            if (!names.Add(layer.Name)) return $"Two layers are named {layer.Name}.";
            if (layer.Rock is null || layer.Rock.Count == 0) return $"Layer {layer.Name} lists no rock colours.";
            if (layer.Rock.Any(c => c is null || !ColorCriteria.InRange(c)))
                return $"Layer {layer.Name} has a rock colour outside 0 to 255.";
        }
        return null;
    }

    public static RingDefinition? Find(IReadOnlyList<RingDefinition> rings, string id) =>
        rings.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));

    private static string? RingProblem(string ringId, IReadOnlyList<RingDefinition> rings)
    {
        var ring = Find(rings, ringId);
        if (ring is null) return $"Ring {ringId} is not defined.";
        return Validate(ring) is { } problem ? $"Ring {ringId}: {problem}" : null;
    }
}
```

- [ ] **Step 6: Give `TriggerStore` rings and upserts**

In `Storage/TriggerStore.cs`, replace

```csharp
    public IReadOnlyList<Trigger> All
    {
        get { lock (_lock) return _state.Triggers.ToArray(); }
    }
```

with

```csharp
    public IReadOnlyList<Trigger> All
    {
        get { lock (_lock) return _state.Triggers.ToArray(); }
    }

    public IReadOnlyList<RingDefinition> Rings
    {
        get { lock (_lock) return _state.Rings.ToArray(); }
    }

    /// <summary>Adds the trigger, or replaces the one with the same id.</summary>
    public void Upsert(Trigger t)
    {
        lock (_lock)
        {
            var idx = _state.Triggers.FindIndex(x => x.Id == t.Id);
            if (idx < 0) _state.Triggers.Add(t); else _state.Triggers[idx] = t;
            WriteNow();
        }
    }

    /// <summary>Adds the ring, or replaces the one with the same id (compared ignoring case).</summary>
    public void UpsertRing(RingDefinition ring)
    {
        lock (_lock)
        {
            var idx = _state.Rings.FindIndex(x => string.Equals(x.Id, ring.Id, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) _state.Rings.Add(ring); else _state.Rings[idx] = ring;
            WriteNow();
        }
    }
```

In the same file, replace

```csharp
            _state = JsonSerializer.Deserialize<TriggersFile>(json, TriggerJsonOptions.Default)
                     ?? new TriggersFile();
```

with

```csharp
            _state = JsonSerializer.Deserialize<TriggersFile>(json, TriggerJsonOptions.Default)
                     ?? new TriggersFile();
            _state.Rings ??= new();   // "rings": null in a hand-edited file
```

- [ ] **Step 7: Run the new tests, then the full suite**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~RingStorageTests|FullyQualifiedName~TriggerValidationTests"`
Expected: PASS, 21 tests.

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj`
Expected: PASS, 159 tests, 0 failed.

- [ ] **Step 8: Commit**

```powershell
git add Storage/Ring.cs Storage/TriggerValidation.cs Storage/Trigger.cs Storage/TriggerStore.cs tests/RoRoRo.UrOcr.Tests/Storage/RingStorageTests.cs tests/RoRoRo.UrOcr.Tests/Storage/TriggerValidationTests.cs
git commit -m "feat(storage): rings, layer triggers and holds in triggers.json" -m "A ring stores each mine layer's rock colours once; its eight spot triggers point at it with ring and order. Layer triggers (same layer, no layer) and holdForMs are stored for the rock cap and the camera rule. All keys are additive; a file without them loads as before. TriggerValidation names the first problem of a trigger or ring in one sentence." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Ring tracker

**Files:**
- Create: `Engine/RingTracker.cs`
- Test: `tests/RoRoRo.UrOcr.Tests/Engine/RingTrackerTests.cs`

**Interfaces:**
- Consumes: `RingDefinition`, `LayerDefinition` (Task 2), `ColorMatcher.Distance(Rgb, Rgb)` (Task 1).
- Produces:
  - `enum RingStatus { Unknown, NoLayer, OnLayer }`
  - `record RingState(RingStatus Status, string? Layer, DateTimeOffset Since, int Votes, int Spots)` with `static RingState Initial` and `string Describe()`
  - `record SpotSample(int Order, Rgb Sampled, int ToleranceRgb)`
  - `class RingTracker`: `RingState Get(string ringId)`, `IReadOnlyDictionary<string, RingState> Snapshot()`, `(RingState State, bool Changed) Update(RingDefinition ring, IReadOnlyList<SpotSample> samples, DateTimeOffset now)`, `(RingState State, bool Changed) MarkUnknown(string ringId, DateTimeOffset now)`, `static IReadOnlyDictionary<string, int> Vote(RingDefinition ring, IReadOnlyList<SpotSample> samples)`, `static string? Pick(RingDefinition ring, IReadOnlyDictionary<string, int> votes, string? previous)`

- [ ] **Step 1: Write the failing tests**

Create `tests/RoRoRo.UrOcr.Tests/Engine/RingTrackerTests.cs`:

```csharp
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

public class RingTrackerTests
{
    private static readonly Rgb Navy = new(30, 30, 90);
    private static readonly Rgb Grey = new(120, 120, 120);
    private static readonly Rgb Orange = new(240, 160, 40);
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private static RingDefinition Ring(int minSpots = 3) => new("mine8", "Mine #8", new[]
    {
        new LayerDefinition("navy", new[] { Navy }),
        new LayerDefinition("grey", new[] { Grey }),
    }, minSpots);

    /// <summary>Eight samples: the given colours in ring order, tolerance 20.</summary>
    private static IReadOnlyList<SpotSample> Samples(params Rgb[] colours) =>
        colours.Select((c, i) => new SpotSample(i, c, 20)).ToList();

    [Fact]
    public void Vote_counts_spots_within_tolerance_of_each_layers_rock()
    {
        var votes = RingTracker.Vote(Ring(), Samples(Navy, Navy, Navy, Grey, Grey, Orange, Orange, new Rgb(35, 32, 95)));

        Assert.Equal(4, votes["navy"]);   // the last sample is about 7.3 from navy
        Assert.Equal(2, votes["grey"]);
    }

    [Fact]
    public void The_layer_with_the_most_spots_is_current()
    {
        var (s, _) = new RingTracker().Update(Ring(), Samples(Grey, Grey, Grey, Grey, Navy, Navy, Orange, Orange), T0);

        Assert.Equal(RingStatus.OnLayer, s.Status);
        Assert.Equal("grey", s.Layer);
        Assert.Equal(4, s.Votes);
        Assert.Equal(8, s.Spots);
    }

    [Fact]
    public void Fewer_than_min_spots_is_no_layer()
    {
        var (s, _) = new RingTracker().Update(Ring(minSpots: 3), Samples(Navy, Navy, Orange, Orange, Orange, Orange, Orange, Orange), T0);

        Assert.Equal(RingStatus.NoLayer, s.Status);
        Assert.Null(s.Layer);
        Assert.Equal(2, s.Votes);
    }

    [Fact]
    public void Three_rock_spots_still_read_the_layer_in_a_ring_full_of_ore()
    {
        var (s, _) = new RingTracker().Update(Ring(minSpots: 3), Samples(Navy, Navy, Navy, Orange, Orange, Orange, Orange, Orange), T0);

        Assert.Equal("navy", s.Layer);
    }

    [Fact]
    public void A_tie_keeps_the_previous_layer()
    {
        var tracker = new RingTracker();
        tracker.Update(Ring(), Samples(Grey, Grey, Grey, Grey, Grey, Grey, Grey, Grey), T0);

        var (s, changed) = tracker.Update(Ring(), Samples(Navy, Navy, Navy, Navy, Grey, Grey, Grey, Grey), T0.AddSeconds(1));

        Assert.Equal("grey", s.Layer);
        Assert.False(changed);
    }

    [Fact]
    public void A_tie_with_no_previous_layer_takes_the_first_in_list_order()
    {
        var (s, _) = new RingTracker().Update(Ring(), Samples(Grey, Grey, Grey, Grey, Navy, Navy, Navy, Navy), T0);

        Assert.Equal("navy", s.Layer);
    }

    [Fact]
    public void Since_holds_while_the_layer_holds_and_resets_on_a_change()
    {
        var tracker = new RingTracker();
        var (a, changedA) = tracker.Update(Ring(), Samples(Navy, Navy, Navy, Navy, Navy, Navy, Navy, Navy), T0);
        var (b, changedB) = tracker.Update(Ring(), Samples(Navy, Navy, Navy, Navy, Navy, Navy, Orange, Orange), T0.AddSeconds(5));
        var (c, changedC) = tracker.Update(Ring(), Samples(Grey, Grey, Grey, Grey, Grey, Grey, Grey, Grey), T0.AddSeconds(9));

        Assert.True(changedA);
        Assert.Equal(T0, a.Since);
        Assert.False(changedB);
        Assert.Equal(T0, b.Since);
        Assert.Equal(6, b.Votes);
        Assert.True(changedC);
        Assert.Equal(T0.AddSeconds(9), c.Since);
        Assert.Equal("grey", tracker.Get("MINE8").Layer);   // ring ids ignore case
    }

    [Fact]
    public void No_samples_is_unknown_not_no_layer()
    {
        var tracker = new RingTracker();
        tracker.Update(Ring(), Samples(Navy, Navy, Navy, Navy, Navy, Navy, Navy, Navy), T0);

        var (s, changed) = tracker.Update(Ring(), Array.Empty<SpotSample>(), T0.AddSeconds(1));

        Assert.Equal(RingStatus.Unknown, s.Status);
        Assert.True(changed);
        Assert.Equal("ring not visible", s.Describe());
    }

    [Fact]
    public void An_unseen_ring_is_unknown_and_marking_it_unknown_changes_nothing()
    {
        var tracker = new RingTracker();

        Assert.Equal(RingState.Initial, tracker.Get("mine8"));
        var (_, changed) = tracker.MarkUnknown("mine8", T0);
        Assert.False(changed);
    }

    [Fact]
    public void Describe_says_the_layer_and_the_votes()
    {
        var (s, _) = new RingTracker().Update(Ring(), Samples(Navy, Navy, Navy, Navy, Navy, Navy, Navy, Orange), T0);

        Assert.Equal("layer navy (7 of 8 spots)", s.Describe());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~RingTrackerTests"`
Expected: build FAILS: `The type or namespace name 'RingTracker' could not be found`.

- [ ] **Step 3: Create `Engine/RingTracker.cs`**

```csharp
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

public enum RingStatus { Unknown, NoLayer, OnLayer }

/// <summary>
/// What a ring reads now. Unknown: no spot could be sampled (not the foreground
/// alt, window gone). NoLayer: spots were sampled but fewer than MinLayerSpots
/// agree on any layer. OnLayer: <see cref="Layer"/> is current. Since is when
/// this status and layer began. Votes is how many spots matched the best layer,
/// out of Spots sampled.
/// </summary>
public sealed record RingState(RingStatus Status, string? Layer, DateTimeOffset Since, int Votes, int Spots)
{
    public static readonly RingState Initial = new(RingStatus.Unknown, null, DateTimeOffset.MinValue, 0, 0);

    public string Describe() => Status switch
    {
        RingStatus.OnLayer => $"layer {Layer} ({Votes} of {Spots} spots)",
        RingStatus.NoLayer => $"no layer ({Votes} of {Spots} spots at best)",
        _ => "ring not visible",
    };
}

/// <summary>One ring spot's sampled colour this tick, with that spot's tolerance.</summary>
public sealed record SpotSample(int Order, Rgb Sampled, int ToleranceRgb);

/// <summary>
/// The current layer of every ring. The coordinator feeds it each tick; the
/// activity log, ur-ocr.log and layer triggers read it. Thread-safe: the UI may
/// read while the coordinator writes.
/// </summary>
public sealed class RingTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<string, RingState> _states = new(StringComparer.OrdinalIgnoreCase);

    public RingState Get(string ringId)
    {
        lock (_gate) return GetLocked(ringId);
    }

    public IReadOnlyDictionary<string, RingState> Snapshot()
    {
        lock (_gate) return new Dictionary<string, RingState>(_states, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Votes the layer from this tick's samples. Changed is true when status or layer changed.</summary>
    public (RingState State, bool Changed) Update(RingDefinition ring, IReadOnlyList<SpotSample> samples, DateTimeOffset now)
    {
        if (samples.Count == 0) return MarkUnknown(ring.Id, now);
        var votes = Vote(ring, samples);
        var top = votes.Count == 0 ? 0 : votes.Values.Max();
        lock (_gate)
        {
            var prev = GetLocked(ring.Id);
            var layer = Pick(ring, votes, prev.Layer);
            var status = layer is null ? RingStatus.NoLayer : RingStatus.OnLayer;
            return SetLocked(ring.Id, prev, status, layer, top, samples.Count, now);
        }
    }

    public (RingState State, bool Changed) MarkUnknown(string ringId, DateTimeOffset now)
    {
        lock (_gate)
        {
            var prev = GetLocked(ringId);
            return SetLocked(ringId, prev, RingStatus.Unknown, null, 0, 0, now);
        }
    }

    /// <summary>For each layer, how many samples are within their tolerance of any of its rock colours.</summary>
    public static IReadOnlyDictionary<string, int> Vote(RingDefinition ring, IReadOnlyList<SpotSample> samples)
    {
        var votes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in ring.Layers)
        {
            var n = 0;
            foreach (var s in samples)
                if (layer.Rock.Any(rock => ColorMatcher.Distance(s.Sampled, rock) <= s.ToleranceRgb)) n++;
            votes[layer.Name] = n;
        }
        return votes;
    }

    /// <summary>
    /// The layer with the most votes, if it has at least MinLayerSpots. A tie keeps
    /// <paramref name="previous"/> when it is among the tied, else the first in list order.
    /// </summary>
    public static string? Pick(RingDefinition ring, IReadOnlyDictionary<string, int> votes, string? previous)
    {
        var top = votes.Count == 0 ? 0 : votes.Values.Max();
        if (top < ring.MinLayerSpots) return null;
        if (previous is not null && votes.TryGetValue(previous, out var p) && p == top)
            return ring.Layers.First(l => string.Equals(l.Name, previous, StringComparison.OrdinalIgnoreCase)).Name;
        return ring.Layers.First(l => votes[l.Name] == top).Name;
    }

    private RingState GetLocked(string ringId) =>
        _states.TryGetValue(ringId, out var s) ? s : RingState.Initial;

    private (RingState, bool) SetLocked(string ringId, RingState prev, RingStatus status, string? layer,
        int votes, int spots, DateTimeOffset now)
    {
        var same = prev.Status == status && string.Equals(prev.Layer, layer, StringComparison.OrdinalIgnoreCase);
        var next = new RingState(status, layer, same ? prev.Since : now, votes, spots);
        _states[ringId] = next;
        return (next, !same);
    }
}
```

- [ ] **Step 4: Run the new tests, then the full suite**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~RingTrackerTests"`
Expected: PASS, 10 tests.

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj`
Expected: PASS, 169 tests, 0 failed.

- [ ] **Step 5: Commit**

```powershell
git add Engine/RingTracker.cs tests/RoRoRo.UrOcr.Tests/Engine/RingTrackerTests.cs
git commit -m "feat(engine): ring tracker votes the current layer" -m "Each layer gets one vote per ring spot within tolerance of its rock; the most votes wins if at least minLayerSpots agree, a tie keeps the previous layer. No samples reads as unknown, never as no layer, so tabbing away cannot look like a knocked camera." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Re-arm on a busy refusal

**Files:**
- Modify: `Engine/TriggerCoordinator.cs` (fields and the fire block of `TickOnceAsync`)
- Modify: `Engine/ActivityLog.cs` (`ActivityKind`)
- Modify: `Ipc/MacroRunClient.cs` (one comment)
- Test: `tests/RoRoRo.UrOcr.Tests/Engine/FireMacroTests.cs`

**Interfaces:**
- Consumes: `IMacroRunClient.RunAsync(string macroId, IReadOnlyList<string>? targets, CancellationToken ct)`, `RunMacroResponse(bool Ok, string? PlaybackId, bool Queued, string? Reason, string? Detail)`.
- Produces: `TriggerCoordinator.BusyReason = "busy"`; `ActivityKind.Busy`. Behaviour: a `busy` refusal does not call `store.MarkFired`, logs `Busy`, keeps the trigger armed, and retries once `now >= refusal time + CooldownMs` while it still matches. Task 5 rewrites this file and keeps this behaviour.

- [ ] **Step 1: Write the failing tests**

In `tests/RoRoRo.UrOcr.Tests/Engine/FireMacroTests.cs`, change the existing refusal test to a non-busy reason (busy is no longer a fire). Replace

```csharp
        macroClient.Response = new RunMacroResponse(false, null, false, "busy", "Ur Task is busy.");
```

with

```csharp
        macroClient.Response = new RunMacroResponse(false, null, false, "unknown-macro", "No macro with id 'macro-refused'.");
```

Then add, inside the class after the last test (before the class's closing `}`):

```csharp
    private static readonly RunMacroResponse BusyResponse = new(false, null, false, "busy", "A sequence is already running.");
    private static readonly RunMacroResponse AcceptedResponse = new(true, "pb-1", false, null, null);

    private (TriggerCoordinator C, FakeColor Color, TriggerStore Store, FakeMacroClient Macros, FakeClock Clock, ActivityLog Log) MakeTimed()
    {
        var store = new TriggerStore(Path.Combine(Path.GetTempPath(), $"fm-{Guid.NewGuid()}.json"));
        var clock = new FakeClock { Now = DateTimeOffset.UnixEpoch };
        var color = new FakeColor();
        var log = new ActivityLog();
        var macros = new FakeMacroClient();
        var c = new TriggerCoordinator(store, new FakeCapture(), color, new FakeText(), new FakeFg(), new FakeElev(),
            new FakeKeys(), log, clock, new FakeMetrics(), onFirstFire: null, macroClient: macros);
        return (c, color, store, macros, clock, log);
    }

    [Fact]
    public async Task Busy_refusal_keeps_the_trigger_armed_and_retries_after_cooldown()
    {
        var (c, color, store, macros, clock, log) = MakeTimed();
        store.Add(RunMacroTrigger("mine-e"));   // CooldownMs = 100
        color.Result = true;
        macros.Response = BusyResponse;

        await c.TickOnceAsync(CancellationToken.None);
        Assert.Single(macros.Calls);
        Assert.Contains(log.Snapshot(), e => e.Kind == ActivityKind.Busy);

        clock.Now = clock.Now.AddMilliseconds(50);
        await c.TickOnceAsync(CancellationToken.None);
        Assert.Single(macros.Calls);                     // waiting out the cooldown

        macros.Response = AcceptedResponse;
        clock.Now = clock.Now.AddMilliseconds(60);       // 110 ms after the refusal
        await c.TickOnceAsync(CancellationToken.None);
        Assert.Equal(2, macros.Calls.Count);
        Assert.Contains(log.Snapshot(), e => e.Kind == ActivityKind.Fired);

        clock.Now = clock.Now.AddMilliseconds(500);
        await c.TickOnceAsync(CancellationToken.None);
        Assert.Equal(2, macros.Calls.Count);             // accepted: the edge is spent
    }

    [Fact]
    public async Task Busy_refusal_is_not_counted_as_a_fire()
    {
        var (c, color, store, macros, _, _) = MakeTimed();
        store.Add(RunMacroTrigger());
        color.Result = true;
        macros.Response = BusyResponse;

        await c.TickOnceAsync(CancellationToken.None);

        var t = Assert.Single(store.All);
        Assert.Equal(0, t.HitCount);
        Assert.Null(t.LastFiredAt);
    }

    [Fact]
    public async Task Busy_retry_lapses_when_the_spot_stops_matching()
    {
        var (c, color, store, macros, clock, _) = MakeTimed();
        store.Add(RunMacroTrigger());
        color.Result = true;
        macros.Response = BusyResponse;
        await c.TickOnceAsync(CancellationToken.None);

        color.Result = false;
        clock.Now = clock.Now.AddMilliseconds(200);
        await c.TickOnceAsync(CancellationToken.None);
        Assert.Single(macros.Calls);                     // no retry while it does not match

        macros.Response = AcceptedResponse;
        color.Result = true;
        await c.TickOnceAsync(CancellationToken.None);
        Assert.Equal(2, macros.Calls.Count);             // a fresh edge fires at once
    }

    [Fact]
    public async Task Other_refusals_spend_the_edge()
    {
        var (c, color, store, macros, clock, _) = MakeTimed();
        store.Add(RunMacroTrigger());
        color.Result = true;
        macros.Response = new RunMacroResponse(false, null, false, "ur-task-not-running", "Ur Task is not running.");

        await c.TickOnceAsync(CancellationToken.None);
        clock.Now = clock.Now.AddMilliseconds(500);
        await c.TickOnceAsync(CancellationToken.None);

        Assert.Single(macros.Calls);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~FireMacroTests"`
Expected: build FAILS: `'ActivityKind' does not contain a definition for 'Busy'`.

- [ ] **Step 3: Add `ActivityKind.Busy`**

In `Engine/ActivityLog.cs`, replace

```csharp
public enum ActivityKind { Fired, WouldFire, NoMatch, SkippedCooldown, SkippedNotAlt, BlockedElevated, Error }
```

with

```csharp
public enum ActivityKind { Fired, WouldFire, NoMatch, SkippedCooldown, SkippedNotAlt, BlockedElevated, Error, Busy }
```

- [ ] **Step 4: Keep busy triggers armed in the coordinator**

In `Engine/TriggerCoordinator.cs`, replace

```csharp
    private readonly Dictionary<Guid, bool> _wasMatched = new();
```

with

```csharp
    /// <summary>The refusal reason Ur Task returns while a sequence is running.</summary>
    public const string BusyReason = "busy";

    private readonly Dictionary<Guid, bool> _wasMatched = new();
    // Trigger id -> earliest retry after a busy refusal. A trigger in here stays armed.
    private readonly Dictionary<Guid, DateTimeOffset> _retryAt = new();
```

In the same file, replace

```csharp
            if (matched && !was && cooldownReady)
            {
```

with

```csharp
            var keepArmed = false;
            if (matched && !was && _retryAt.TryGetValue(trig.Id, out var retryAt) && now < retryAt)
            {
                // Ur Task was busy: stay armed and try again once the cooldown has passed.
                keepArmed = true;
                log.Record(trig.Id, trig.Name, ActivityKind.SkippedCooldown,
                    $"{(retryAt - now).TotalMilliseconds:0}ms (Ur Task was busy)");
            }
            else if (matched && !was && cooldownReady)
            {
```

In the same file, replace

```csharp
                    var resp = await macroClient.RunAsync(trig.MacroId, trig.MacroTargets, ct).ConfigureAwait(false);
                    store.MarkFired(trig.Id, now);
                    log.Record(trig.Id, trig.Name, ActivityKind.Fired,
                        resp.Ok ? $"macro {trig.MacroId}" : $"macro refused: {resp.Reason}");
                    if (!trig.FirstFireConfirmed) onFirstFire?.Invoke(trig);
```

with

```csharp
                    var resp = await macroClient.RunAsync(trig.MacroId, trig.MacroTargets, ct).ConfigureAwait(false);
                    if (!resp.Ok && string.Equals(resp.Reason, BusyReason, StringComparison.OrdinalIgnoreCase))
                    {
                        // Not a fire: nothing ran. Stay armed, retry after the cooldown.
                        keepArmed = true;
                        _retryAt[trig.Id] = now.AddMilliseconds(trig.CooldownMs);
                        log.Record(trig.Id, trig.Name, ActivityKind.Busy, $"Ur Task busy, retry in {trig.CooldownMs}ms");
                    }
                    else
                    {
                        _retryAt.Remove(trig.Id);
                        store.MarkFired(trig.Id, now);
                        log.Record(trig.Id, trig.Name, ActivityKind.Fired,
                            resp.Ok ? $"macro {trig.MacroId}" : $"macro refused: {resp.Reason}");
                        if (!trig.FirstFireConfirmed) onFirstFire?.Invoke(trig);
                    }
```

In the same file, replace

```csharp
            else if (!matched)
            {
                log.Record(trig.Id, trig.Name, ActivityKind.NoMatch,
```

with

```csharp
            else if (!matched)
            {
                _retryAt.Remove(trig.Id);
                log.Record(trig.Id, trig.Name, ActivityKind.NoMatch,
```

In the same file, replace

```csharp
            _wasMatched[trig.Id] = matched;
```

with

```csharp
            _wasMatched[trig.Id] = matched && !keepArmed;
```

- [ ] **Step 5: Correct the stale ack comment**

In `Ipc/MacroRunClient.cs`, replace

```csharp
            // The wait was cancelled (e.g. the coordinator's per-tick watchdog) before Ur Task
            // acked. Ur Task only acks after the macro finishes playing, so a long macro lands
            // here while it IS running — report that honestly, not "not running".
```

with

```csharp
            // The wait was cancelled (e.g. the coordinator's per-tick watchdog) before Ur Task
            // acked. Ur Task acks as soon as it accepts or refuses a run (a running sequence is
            // refused as "busy"), so this means the pipe stalled and the run may or may not have
            // started. Report that honestly, not "not running".
```

- [ ] **Step 6: Run the tests, then the full suite**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~FireMacroTests"`
Expected: PASS, 8 tests.

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj`
Expected: PASS, 173 tests, 0 failed.

- [ ] **Step 7: Commit**

```powershell
git add Engine/TriggerCoordinator.cs Engine/ActivityLog.cs Ipc/MacroRunClient.cs tests/RoRoRo.UrOcr.Tests/Engine/FireMacroTests.cs
git commit -m "feat(engine): a busy refusal keeps the trigger armed" -m "When Ur Task refuses RunMacro because a sequence is already running, the trigger logs Busy, does not count a fire, and tries again after its cooldown while it still matches. Any other refusal spends the edge as before, so a closed Ur Task is not retried forever." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Ring spots in the coordinator, fired in ring order

**Files:**
- Modify: `Engine/TriggerCoordinator.cs` (replace the whole file)
- Modify: `Engine/ActivityLog.cs` (`ActivityKind`)
- Modify: `PluginRuntime.cs` (pass the log sink)
- Modify: `Engine/PreviewEvaluator.cs` (skip ring spots)
- Test: `tests/RoRoRo.UrOcr.Tests/Engine/RingCoordinatorTests.cs`
- Test: `tests/RoRoRo.UrOcr.Tests/Engine/PreviewEvaluatorTests.cs` (one test added)

**Interfaces:**
- Consumes: `ColorMatcher.Judge` (Task 1); `TriggerStore.Rings`, `TriggerValidation.Validate(Trigger, IReadOnlyList<RingDefinition>)`, `RingSpot`, `RingDefinition` (Task 2); `RingTracker`, `RingState`, `RingStatus`, `SpotSample` (Task 3); busy behaviour and `BusyReason` (Task 4).
- Produces:
  - `TriggerCoordinator` constructor gains a last optional parameter `Action<string>? diag = null` (a line sink for `ur-ocr.log`).
  - `RingTracker TriggerCoordinator.Rings { get; }` (current layer per ring).
  - `ActivityKind.Deferred`, `ActivityKind.LayerChanged`.
  - Private structure Task 6 extends: `Reading` class, `ReadAsync`, `JudgeRing`, `InFiringOrder`, `DecideAsync`, `FireAsync`, `Disarm`.

- [ ] **Step 1: Write the failing tests**

Create `tests/RoRoRo.UrOcr.Tests/Engine/RingCoordinatorTests.cs`:

```csharp
using System.Drawing;
using System.IO;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

/// <summary>
/// Ring spots end to end: real ColorMatcher, a capture that paints each spot a
/// colour, the ring tracker picking the layer, and ring order deciding who fires.
/// </summary>
public class RingCoordinatorTests
{
    private static readonly Rgb Navy = new(30, 30, 90);
    private static readonly Rgb Grey = new(120, 120, 120);
    private static readonly Rgb Sky = new(135, 206, 235);
    private static readonly Rgb Orange = new(240, 160, 40);
    private static readonly Rgb Violet = new(170, 60, 220);

    private sealed class FakeClock : IClock { public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch; }

    /// <summary>Paints the whole capture one colour, chosen by the region's X.</summary>
    private sealed class PaintedCapture : ICaptureSource
    {
        public Rgb Default = Navy;
        public Dictionary<int, Rgb> ByX { get; } = new();
        public Bitmap Capture(RegionRect r)
        {
            var c = ByX.TryGetValue(r.X, out var v) ? v : Default;
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
    private sealed class Fg : IForegroundCheck { public bool IsAlt = true; public bool IsForegroundAnAlt() => IsAlt; public int GetForegroundPid() => 1; }
    private sealed class NotElevated : IElevationCheck { public bool IsForegroundProcessLikelyElevated(int pid) => false; }
    private sealed class NoKeys : IKeyPress { public void Press(KeyCombo c) { } }
    private sealed class FakeMetrics : IWindowMetrics
    {
        public IntPtr HwndForPid(int pid) => new(0x10);
        public (int X, int Y)? ClientOrigin(IntPtr h) => (0, 0);
        public (int W, int H)? ClientSize(IntPtr h) => (800, 599);
    }
    private sealed class RecordingMacros : IMacroRunClient
    {
        public List<string> Calls { get; } = new();
        public RunMacroResponse Response { get; set; } = new(true, "pb", false, null, null);
        public Task<RunMacroResponse> RunAsync(string macroId, IReadOnlyList<string>? targets, CancellationToken ct)
        {
            Calls.Add(macroId);
            return Task.FromResult(Response);
        }
    }

    private static readonly RunMacroResponse Busy = new(false, null, false, "busy", "A sequence is already running.");
    private static readonly RunMacroResponse Accepted = new(true, "pb", false, null, null);

    private static RingDefinition Ring() => new("mine8", "Mine #8", new[]
    {
        new LayerDefinition("navy", new[] { Navy }),
        new LayerDefinition("grey", new[] { Grey }),
    }, MinLayerSpots: 3);

    private static int X(int order) => 100 + order * 20;

    private static Trigger Spot(int order, bool accountAware, string ringId = "mine8") => new()
    {
        Id = Guid.NewGuid(),
        Name = $"spot {order}",
        Region = new RegionRect(X(order), 100, 9, 9),
        Mode = TriggerMode.Color,
        Color = new ColorCriteria(new Rgb(0, 0, 0), 20, ColorSamplingMode.SinglePixel,
            Point: new PickPoint(4, 4), Box: new SampleBox(), NoneOf: new[] { Sky }),
        Keybind = new KeyCombo("F13", Array.Empty<string>()),
        AccountAware = accountAware,
        CooldownMs = 100,
        Action = TriggerAction.RunMacro,
        MacroId = $"mine-{order}",
        Ring = new RingSpot(ringId, order),
    };

    private sealed record Rig(TriggerCoordinator C, PaintedCapture Paint, RecordingMacros Macros,
        ActivityLog Log, FakeClock Clock, Fg Fg, List<string> Diag, TriggerStore Store);

    private static Rig Build(bool accountAware = false)
    {
        var store = new TriggerStore(Path.Combine(Path.GetTempPath(), "urocr-tests", Guid.NewGuid().ToString("N") + ".json"));
        store.UpsertRing(Ring());
        // Added out of ring order on purpose: firing order must come from RingSpot.Order.
        foreach (var order in new[] { 7, 3, 0, 5, 1, 6, 2, 4 }) store.Add(Spot(order, accountAware));
        var paint = new PaintedCapture();
        var macros = new RecordingMacros();
        var log = new ActivityLog(capacity: 1000);
        var clock = new FakeClock();
        var fg = new Fg();
        var diag = new List<string>();
        var c = new TriggerCoordinator(store, paint, new ColorMatcher(), new NoText(), fg, new NotElevated(),
            new NoKeys(), log, clock, new FakeMetrics(), macroClient: macros, diag: diag.Add);
        return new Rig(c, paint, macros, log, clock, fg, diag, store);
    }

    private static Task Tick(Rig rig) => rig.C.TickOnceAsync(CancellationToken.None);

    [Fact]
    public async Task Plain_rock_all_round_fires_nothing_and_reads_the_layer()
    {
        var rig = Build();

        await Tick(rig);

        Assert.Empty(rig.Macros.Calls);
        var s = rig.C.Rings.Get("mine8");
        Assert.Equal(RingStatus.OnLayer, s.Status);
        Assert.Equal("navy", s.Layer);
        Assert.Equal(8, s.Votes);
    }

    [Fact]
    public async Task One_ore_spot_fires_its_own_macro()
    {
        var rig = Build();
        rig.Paint.ByX[X(3)] = Orange;

        await Tick(rig);

        Assert.Equal(new[] { "mine-3" }, rig.Macros.Calls);
    }

    [Fact]
    public async Task Several_ore_spots_fire_in_ring_order_one_per_tick()
    {
        var rig = Build();
        rig.Paint.ByX[X(5)] = Orange;
        rig.Paint.ByX[X(2)] = Violet;

        await Tick(rig);
        Assert.Equal(new[] { "mine-2" }, rig.Macros.Calls);
        Assert.Contains(rig.Log.Snapshot(), e => e.Kind == ActivityKind.Deferred && e.TriggerName == "spot 5");

        await Tick(rig);
        Assert.Equal(new[] { "mine-2", "mine-5" }, rig.Macros.Calls);
    }

    [Fact]
    public async Task A_spot_waiting_on_busy_keeps_its_turn()
    {
        var rig = Build();
        rig.Paint.ByX[X(5)] = Orange;
        rig.Paint.ByX[X(2)] = Orange;
        rig.Macros.Response = Busy;

        await Tick(rig);                                  // spot 2 busy, spot 5 waits its turn
        await Tick(rig);                                  // spot 2 still waiting out its cooldown
        Assert.Equal(new[] { "mine-2" }, rig.Macros.Calls);

        rig.Macros.Response = Accepted;
        rig.Clock.Now = rig.Clock.Now.AddMilliseconds(100);
        await Tick(rig);                                  // spot 2 retries and fires
        await Tick(rig);                                  // then spot 5
        Assert.Equal(new[] { "mine-2", "mine-2", "mine-5" }, rig.Macros.Calls);
    }

    [Fact]
    public async Task No_layer_means_no_ore()
    {
        var rig = Build();
        rig.Paint.Default = Orange;                       // six spots orange...
        rig.Paint.ByX[X(0)] = Navy;                       // ...two navy: below minLayerSpots
        rig.Paint.ByX[X(1)] = Navy;

        await Tick(rig);

        Assert.Empty(rig.Macros.Calls);
        Assert.Equal(RingStatus.NoLayer, rig.C.Rings.Get("mine8").Status);
        Assert.Contains(rig.Log.Snapshot(), e => e.Kind == ActivityKind.NoMatch && (e.Detail ?? "").Contains("no layer"));
    }

    [Fact]
    public async Task An_ignore_colour_is_not_ore()
    {
        var rig = Build();
        rig.Paint.ByX[X(4)] = Sky;

        await Tick(rig);

        Assert.Empty(rig.Macros.Calls);
    }

    [Fact]
    public async Task A_neighbouring_layers_rock_reads_as_ore()
    {
        // Deliberate: only the current layer's rock is "rock". At a boundary the other
        // layer's rock costs one short mine, never a stall.
        var rig = Build();
        rig.Paint.Default = Grey;
        rig.Paint.ByX[X(6)] = Navy;
        rig.Paint.ByX[X(7)] = Navy;

        await Tick(rig);

        Assert.Equal("grey", rig.C.Rings.Get("mine8").Layer);
        Assert.Equal(new[] { "mine-6" }, rig.Macros.Calls);
    }

    [Fact]
    public async Task A_layer_change_is_logged_to_the_panel_and_the_file()
    {
        var rig = Build();
        await Tick(rig);
        rig.Paint.Default = Grey;
        rig.Clock.Now = rig.Clock.Now.AddSeconds(30);

        await Tick(rig);

        Assert.Equal("grey", rig.C.Rings.Get("mine8").Layer);
        Assert.Contains(rig.Log.Snapshot(), e => e.Kind == ActivityKind.LayerChanged && e.Detail == "layer grey (8 of 8 spots)");
        Assert.Contains("ring Mine #8: layer grey (8 of 8 spots)", rig.Diag);
    }

    [Fact]
    public async Task A_ring_fire_is_written_to_the_file()
    {
        var rig = Build();
        rig.Paint.ByX[X(3)] = Orange;

        await Tick(rig);

        Assert.Contains(rig.Diag, l => l.StartsWith("trigger \"spot 3\": macro mine-3"));
    }

    [Fact]
    public async Task Not_the_foreground_alt_leaves_the_ring_unknown()
    {
        var rig = Build(accountAware: true);
        rig.Paint.Default = Orange;
        rig.Fg.IsAlt = false;

        await Tick(rig);

        Assert.Empty(rig.Macros.Calls);
        Assert.Equal(RingStatus.Unknown, rig.C.Rings.Get("mine8").Status);
    }

    [Fact]
    public async Task A_spot_on_a_missing_ring_logs_one_error()
    {
        var rig = Build();
        var orphan = Spot(0, accountAware: false, ringId: "nope");
        orphan.Name = "orphan";
        rig.Store.Add(orphan);

        await Tick(rig);
        await Tick(rig);

        Assert.Single(rig.Log.Snapshot(), e => e.Kind == ActivityKind.Error && e.TriggerName == "orphan");
        Assert.Single(rig.Diag, l => l.Contains("orphan"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~RingCoordinatorTests"`
Expected: build FAILS: `The best overload for 'TriggerCoordinator' does not have a parameter named 'diag'`, `'TriggerCoordinator' does not contain a definition for 'Rings'`.

- [ ] **Step 3: Add the two activity kinds**

In `Engine/ActivityLog.cs`, replace

```csharp
public enum ActivityKind { Fired, WouldFire, NoMatch, SkippedCooldown, SkippedNotAlt, BlockedElevated, Error, Busy }
```

with

```csharp
public enum ActivityKind { Fired, WouldFire, NoMatch, SkippedCooldown, SkippedNotAlt, BlockedElevated, Error, Busy, Deferred, LayerChanged }
```

- [ ] **Step 4: Rewrite the coordinator in read, ring, decide phases**

Replace the whole of `Engine/TriggerCoordinator.cs` with:

```csharp
using System.Diagnostics;
using System.Drawing;
using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

public interface ICaptureSource { Bitmap Capture(RegionRect region); }
public interface IColorMatchEngine
{
    bool Matches(Bitmap b, ColorCriteria c);
    ColorMatchResult Evaluate(Bitmap b, ColorCriteria c);
    /// <summary>recordedRegion is the trigger's stored region, so a pick point
    /// scales when a client-anchored capture comes back at a different size.</summary>
    ColorMatchResult Evaluate(Bitmap b, ColorCriteria c, RegionRect recordedRegion) => Evaluate(b, c);
}
public interface ITextMatchEngine
{
    Task<(bool matched, string text)> RunAsync(Bitmap b, TextCriteria c);
    Task<(bool matched, string text)> RunWithPreprocessAsync(Bitmap b, TextCriteria c);
}
public interface IForegroundCheck { bool IsForegroundAnAlt(); int GetForegroundPid(); }
public interface IElevationCheck { bool IsForegroundProcessLikelyElevated(int pid); }
public interface IKeyPress { void Press(KeyCombo combo); }
public interface IClock { DateTimeOffset Now { get; } }

public sealed class SystemClock : IClock { public DateTimeOffset Now => DateTimeOffset.UtcNow; }

/// <summary>
/// Each tick: read every trigger that may run (valid, foreground gate), update
/// every ring's layer from its spots' samples and judge the spots against that
/// layer, then decide and fire. Ring spots fire in ring order, at most one per
/// ring per tick; the rest stay armed for the next tick.
/// </summary>
public sealed class TriggerCoordinator(
    TriggerStore store,
    ICaptureSource capture,
    IColorMatchEngine color,
    ITextMatchEngine text,
    IForegroundCheck foreground,
    IElevationCheck elevation,
    IKeyPress keys,
    ActivityLog log,
    IClock clock,
    IWindowMetrics metrics,
    Action<Trigger>? onFirstFire = null,
    IMacroRunClient? macroClient = null,
    Action<string>? diag = null)
{
    /// <summary>The refusal reason Ur Task returns while a sequence is running.</summary>
    public const string BusyReason = "busy";

    public int TickRateHz { get; set; } = 5;
    public TimeSpan WatchdogTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public bool Paused { get; set; }
    public bool DryRun { get; set; }

    /// <summary>The current layer of every ring, updated each tick.</summary>
    public RingTracker Rings { get; } = new();

    private readonly Dictionary<Guid, bool> _wasMatched = new();
    // Trigger id -> earliest retry after a busy refusal. A trigger in here stays armed.
    private readonly Dictionary<Guid, DateTimeOffset> _retryAt = new();
    // Trigger id -> the validation problem last reported, so each problem is logged once.
    private readonly Dictionary<Guid, string> _lastProblem = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>One trigger's reading this tick. A ring spot carries its sample until its ring is judged.</summary>
    private sealed class Reading(Trigger trigger)
    {
        public Trigger Trigger { get; } = trigger;
        public bool Matched { get; set; }
        public string Detail { get; set; } = "";
        public Rgb? Sampled { get; set; }
    }

    private enum FireOutcome { Fired, Busy }

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_loop is not null) await _loop;
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
                log.Record(Guid.Empty, "(coordinator)", ActivityKind.Error, "watchdog: tick exceeded 5s");
            }
            catch (Exception ex)
            {
                log.Record(Guid.Empty, "(coordinator)", ActivityKind.Error, ex.Message);
            }
            var elapsed = clock.Now - tickStart;
            var remain = period - elapsed;
            if (remain > TimeSpan.Zero) await Task.Delay(remain, ct);
        }
    }

    public async Task TickOnceAsync(CancellationToken ct)
    {
        if (Paused) return;
        var rings = store.Rings;

        var readings = new List<Reading>();
        foreach (var trig in store.All)
        {
            ct.ThrowIfCancellationRequested();
            if (!trig.Enabled) continue;
            if (!IsValid(trig, rings)) continue;
            if (!PassesGate(trig)) continue;
            var reading = await ReadAsync(trig);
            if (reading is not null) readings.Add(reading);
        }

        var now = clock.Now;
        foreach (var ring in rings) JudgeRing(ring, readings, now);

        var claimedRings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reading in InFiringOrder(readings))
        {
            ct.ThrowIfCancellationRequested();
            await DecideAsync(reading, now, claimedRings, ct);
        }
    }

    private bool IsValid(Trigger trig, IReadOnlyList<RingDefinition> rings)
    {
        var problem = TriggerValidation.Validate(trig, rings);
        if (problem is null)
        {
            _lastProblem.Remove(trig.Id);
            return true;
        }
        if (!_lastProblem.TryGetValue(trig.Id, out var last) || last != problem)
        {
            _lastProblem[trig.Id] = problem;
            log.Record(trig.Id, trig.Name, ActivityKind.Error, problem);
            diag?.Invoke($"trigger \"{trig.Name}\" skipped: {problem}");
        }
        Disarm(trig.Id);
        return false;
    }

    private bool PassesGate(Trigger trig)
    {
        if (!trig.AccountAware && !trig.IsClientSpace) return true;
        if (!foreground.IsForegroundAnAlt())
        {
            log.Record(trig.Id, trig.Name, ActivityKind.SkippedNotAlt);
            Disarm(trig.Id);
            return false;
        }
        var pid = foreground.GetForegroundPid();
        if (elevation.IsForegroundProcessLikelyElevated(pid))
        {
            log.Record(trig.Id, trig.Name, ActivityKind.BlockedElevated);
            Disarm(trig.Id);
            return false;
        }
        return true;
    }

    private async Task<Reading?> ReadAsync(Trigger trig)
    {
        // Layer triggers read the ring, not the screen.
        if (trig.Mode == TriggerMode.Layer) return null;

        var captureRegion = TriggerRegionResolver.Resolve(trig, trig.IsClientSpace ? foreground.GetForegroundPid() : 0, metrics);
        if (captureRegion is null || captureRegion.Width < 1 || captureRegion.Height < 1)
        {
            // client trigger whose anchor window vanished mid-tick
            log.Record(trig.Id, trig.Name, ActivityKind.SkippedNotAlt, "anchor window unavailable");
            Disarm(trig.Id);
            return null;
        }
        using var bmp = capture.Capture(captureRegion);
        if (trig.Mode == TriggerMode.Text && trig.Text is not null)
        {
            var (m, t) = trig.OcrPreprocess
                ? await text.RunWithPreprocessAsync(bmp, trig.Text)
                : await text.RunAsync(bmp, trig.Text);
            return new Reading(trig) { Matched = m, Detail = t.Length > 0 ? $"OCR: {t}" : "" };
        }
        if (trig.Mode == TriggerMode.Color && trig.Color is not null)
        {
            var r = color.Evaluate(bmp, trig.Color, trig.Region);
            // A ring spot is judged once its ring's layer is known (JudgeRing).
            if (trig.Ring is not null) return new Reading(trig) { Sampled = r.Sampled };
            // Logged every match so the default tolerance can be tuned from real runs.
            return new Reading(trig) { Matched = r.Matched, Detail = ColorDetail(r) };
        }
        return null;
    }

    private void JudgeRing(RingDefinition ring, List<Reading> readings, DateTimeOffset now)
    {
        var spots = readings
            .Where(r => r.Sampled is not null && r.Trigger.Ring is { } s
                        && string.Equals(s.RingId, ring.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var samples = spots
            .Select(r => new SpotSample(r.Trigger.Ring!.Order, r.Sampled!, r.Trigger.Color!.ToleranceRgb))
            .ToList();

        var (state, changed) = Rings.Update(ring, samples, now);
        if (changed)
        {
            log.Record(Guid.Empty, $"(ring {ring.Name})", ActivityKind.LayerChanged, state.Describe());
            diag?.Invoke($"ring {ring.Name}: {state.Describe()}");
        }

        var layer = state.Status == RingStatus.OnLayer
            ? ring.Layers.First(l => string.Equals(l.Name, state.Layer, StringComparison.OrdinalIgnoreCase))
            : null;
        foreach (var r in spots)
        {
            if (layer is null)
            {
                // Without a layer there is no rock to compare against, so nothing counts as ore.
                r.Matched = false;
                r.Detail = $"{ColorNamer.Describe(r.Sampled!)} {state.Describe()}";
                continue;
            }
            var j = ColorMatcher.Judge(r.Sampled!, r.Trigger.Color!, layer.Rock);
            r.Matched = j.Matched;
            r.Detail = $"{layer.Name}: {ColorDetail(j)}";
        }
    }

    /// <summary>
    /// Store order, except that a ring's spots are taken together, at the place of
    /// the ring's first spot, sorted by ring order.
    /// </summary>
    private static IEnumerable<Reading> InFiringOrder(List<Reading> readings)
    {
        var firstIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < readings.Count; i++)
            if (readings[i].Trigger.Ring is { } s) firstIndex.TryAdd(s.RingId, i);

        return readings
            .Select((r, i) => (r, i))
            .OrderBy(x => x.r.Trigger.Ring is { } s ? firstIndex[s.RingId] : x.i)
            .ThenBy(x => x.r.Trigger.Ring?.Order ?? 0)
            .ThenBy(x => x.i)
            .Select(x => x.r);
    }

    private async Task DecideAsync(Reading r, DateTimeOffset now, HashSet<string> claimedRings, CancellationToken ct)
    {
        var trig = r.Trigger;
        var detail = r.Detail.Length > 0 ? r.Detail : null;

        if (!r.Matched)
        {
            _retryAt.Remove(trig.Id);
            _wasMatched[trig.Id] = false;
            log.Record(trig.Id, trig.Name, ActivityKind.NoMatch, detail);
            return;
        }

        var was = _wasMatched.GetValueOrDefault(trig.Id, false);

        if (!was && _retryAt.TryGetValue(trig.Id, out var retryAt) && now < retryAt)
        {
            // Ur Task was busy: stay armed, and keep this spot's turn in its ring.
            if (trig.Ring is { } waiting) claimedRings.Add(waiting.RingId);
            log.Record(trig.Id, trig.Name, ActivityKind.SkippedCooldown,
                $"{(retryAt - now).TotalMilliseconds:0}ms (Ur Task was busy)");
            return;
        }

        var cooldownReady = trig.LastFiredAt is null
            || (now - trig.LastFiredAt.Value).TotalMilliseconds >= trig.CooldownMs;
        if (!cooldownReady)
        {
            // An edge that lands inside the cooldown is spent, as it always has been.
            var remain = trig.CooldownMs - (now - trig.LastFiredAt!.Value).TotalMilliseconds;
            log.Record(trig.Id, trig.Name, ActivityKind.SkippedCooldown, $"{remain:0}ms");
            _wasMatched[trig.Id] = true;
            return;
        }
        if (was) return;   // edge already spent

        if (trig.Ring is { } spot)
        {
            if (claimedRings.Contains(spot.RingId))
            {
                // A spot earlier in ring order went this tick; this one stays armed for the next.
                log.Record(trig.Id, trig.Name, ActivityKind.Deferred, "another ring spot went first");
                return;
            }
            claimedRings.Add(spot.RingId);
        }

        var outcome = await FireAsync(trig, detail, now, ct);
        if (outcome == FireOutcome.Busy)
        {
            _retryAt[trig.Id] = now.AddMilliseconds(trig.CooldownMs);
            _wasMatched[trig.Id] = false;
            return;
        }
        _retryAt.Remove(trig.Id);
        _wasMatched[trig.Id] = true;
    }

    private async Task<FireOutcome> FireAsync(Trigger trig, string? detail, DateTimeOffset now, CancellationToken ct)
    {
        if (DryRun)
        {
            log.Record(trig.Id, trig.Name, ActivityKind.WouldFire, detail);
            return FireOutcome.Fired;
        }
        if (trig.Action == TriggerAction.RunMacro && macroClient is not null && trig.MacroId is not null)
        {
            var resp = await macroClient.RunAsync(trig.MacroId, trig.MacroTargets, ct).ConfigureAwait(false);
            if (!resp.Ok && string.Equals(resp.Reason, BusyReason, StringComparison.OrdinalIgnoreCase))
            {
                // Not a fire: nothing ran. The caller keeps the trigger armed.
                log.Record(trig.Id, trig.Name, ActivityKind.Busy, $"Ur Task busy, retry in {trig.CooldownMs}ms");
                Diag(trig, $"Ur Task busy, retry in {trig.CooldownMs}ms");
                return FireOutcome.Busy;
            }
            store.MarkFired(trig.Id, now);
            var what = resp.Ok ? $"macro {trig.MacroId}" : $"macro refused: {resp.Reason}";
            log.Record(trig.Id, trig.Name, ActivityKind.Fired, what);
            Diag(trig, detail is null ? what : $"{what} ({detail})");
            if (!trig.FirstFireConfirmed) onFirstFire?.Invoke(trig);
            return FireOutcome.Fired;
        }
        keys.Press(trig.Keybind);
        store.MarkFired(trig.Id, now);
        log.Record(trig.Id, trig.Name, ActivityKind.Fired, detail);
        if (!trig.FirstFireConfirmed) onFirstFire?.Invoke(trig);
        return FireOutcome.Fired;
    }

    private void Disarm(Guid id)
    {
        _wasMatched[id] = false;
        _retryAt.Remove(id);
    }

    // ur-ocr.log carries the ring's decisions so a session can read them after a run.
    private void Diag(Trigger trig, string message)
    {
        if (trig.Ring is null && trig.Mode != TriggerMode.Layer) return;
        diag?.Invoke($"trigger \"{trig.Name}\": {message}");
    }

    private static string ColorDetail(ColorMatchResult r)
    {
        if (r.Nearest is { } n)
            return $"{ColorNamer.Describe(r.Sampled)} nearest {ColorNamer.Hex(n)} d={r.Distance:F1}";
        return $"{ColorNamer.Describe(r.Sampled)} d={r.Distance:F1}"
            + (r.DistanceToOther is { } o ? $" other={o:F1}" : "");
    }
}
```

- [ ] **Step 5: Send ring lines to `ur-ocr.log`**

In `PluginRuntime.cs`, replace

```csharp
            macroClient: MacroClient)
```

with

```csharp
            macroClient: MacroClient,
            diag: Diagnostics.DiagLog.Write)
```

- [ ] **Step 5a: No live preview for ring spots**

The editor's live meter (`PreviewEvaluator`) has no ring layer, so it would judge a ring spot's `noneOf` list without the layer's rock and show plain rock as a match. It shows nothing for a ring spot instead (a known limitation, written into the README in Task 8).

In `tests/RoRoRo.UrOcr.Tests/Engine/PreviewEvaluatorTests.cs`, add inside the class after the last test (before the class's closing `}`):

```csharp
    [Fact]
    public void EvaluateTrigger_RingSpot_ReturnsNull()
    {
        // A ring spot is judged against its ring's current layer, which the editor does not know.
        var pe = new PreviewEvaluator(new FakeCapture(255, 17, 95), new ColorMatcher(), new FakeMetrics(), () => 0);
        var trig = ScreenTrigger(new RegionRect(0, 0, 9, 9), new ColorCriteria(new Rgb(0, 0, 0), 10, ColorSamplingMode.SinglePixel,
            Point: new PickPoint(4, 4), Box: new SampleBox(), NoneOf: new[] { new Rgb(135, 206, 235) }));
        trig.Ring = new RingSpot("mine8", 0);

        Assert.Null(pe.EvaluateTrigger(trig));
    }
```

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~PreviewEvaluatorTests"`
Expected: FAIL, 1 failed (`EvaluateTrigger_RingSpot_ReturnsNull`: `Assert.Null() Failure`), 6 passed.

In `Engine/PreviewEvaluator.cs`, replace

```csharp
        if (trig.Mode != TriggerMode.Color || trig.Color is null) return null;
```

with

```csharp
        if (trig.Mode != TriggerMode.Color || trig.Color is null) return null;
        // A ring spot is judged against its ring's current layer, which the editor does not know.
        if (trig.Ring is not null) return null;
```

Run the same filter again. Expected: PASS, 7 tests.

- [ ] **Step 6: Run the tests, then build the app and the full suite**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~RingCoordinatorTests"`
Expected: PASS, 11 tests.

Run: `dotnet build rororo-ur-ocr.csproj`
Expected: `Build succeeded.` with 0 errors.

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj`
Expected: PASS, 185 tests (173 + 11 + 1), 0 failed. The pre-existing `TriggerCoordinatorTests`, `TriggerCoordinatorWindowTests`, `DryRunTests` and `FireMacroTests` must pass unchanged; if one fails, the rewrite changed a behaviour it must keep (edge on transition, cooldown spends an edge, gate logging, dry run).

- [ ] **Step 7: Commit**

```powershell
git add Engine/TriggerCoordinator.cs Engine/ActivityLog.cs Engine/PreviewEvaluator.cs PluginRuntime.cs tests/RoRoRo.UrOcr.Tests/Engine/RingCoordinatorTests.cs tests/RoRoRo.UrOcr.Tests/Engine/PreviewEvaluatorTests.cs
git commit -m "feat(engine): ring spots judged against the current layer, fired in ring order" -m "Each tick reads the triggers, votes every ring's layer from its spots, and judges each spot against that layer's rock plus its own ignore colours. With no layer, nothing counts as ore. At most one spot per ring runs its macro per tick, in ring order; a spot waiting on a busy retry keeps its turn. Invalid triggers log one error, not one per tick. Layer changes and ring fires go to ur-ocr.log; the coordinator exposes Rings for the log and layer triggers. The editor's live preview skips ring spots, which only mean something against the ring's layer." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Hold before firing, and layer triggers (rock cap, camera rule)

**Files:**
- Modify: `Engine/TriggerCoordinator.cs` (replace the whole file)
- Modify: `Engine/ActivityLog.cs` (`ActivityKind`)
- Test: `tests/RoRoRo.UrOcr.Tests/Engine/DwellTests.cs`
- Test: `tests/RoRoRo.UrOcr.Tests/Engine/LayerTriggerTests.cs`

**Interfaces:**
- Consumes: `Trigger.HoldForMs`, `Trigger.Layer`, `LayerCriteria`, `LayerCondition`, `TriggerMode.Layer` (Task 2); `RingTracker.Get`, `RingState.Describe()`, `RingStatus` (Task 3); the Task 5 coordinator (same constructor, `Rings`, `diag`).
- Produces: `ActivityKind.Holding`. Behaviour: a trigger with `HoldForMs > 0` fires only after its match has held unbroken for `HoldForMs` (keyed: for `SameLayer` the key is the layer name, so a layer change restarts the hold), and starts a fresh hold when it fires. `SameLayer` matches while `RingStatus.OnLayer`; `NoLayer` matches while `RingStatus.NoLayer` (never while `Unknown`). A held trigger whose hold completes inside its cooldown stays armed and fires when the cooldown ends. When a `SameLayer` trigger fires, its detail is `Went to top: <N> minutes on the <layer> layer` (N from `HoldForMs`, invariant culture), written to `ur-ocr.log` through `diag`.

- [ ] **Step 1: Write the failing tests**

Create `tests/RoRoRo.UrOcr.Tests/Engine/DwellTests.cs`:

```csharp
using System.Drawing;
using System.IO;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

public class DwellTests
{
    private sealed class FakeClock : IClock { public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch; }
    private sealed class FakeCapture : ICaptureSource { public Bitmap Capture(RegionRect r) => new(r.Width, r.Height); }
    private sealed class FakeColor : IColorMatchEngine
    {
        public bool Result;
        public bool Matches(Bitmap b, ColorCriteria c) => Result;
        public ColorMatchResult Evaluate(Bitmap b, ColorCriteria c) => new(new Rgb(0, 0, 0), 0, Result);
    }
    private sealed class NoText : ITextMatchEngine
    {
        public Task<(bool matched, string text)> RunAsync(Bitmap b, TextCriteria c) => Task.FromResult((false, ""));
        public Task<(bool matched, string text)> RunWithPreprocessAsync(Bitmap b, TextCriteria c) => Task.FromResult((false, ""));
    }
    private sealed class Fg : IForegroundCheck { public bool IsForegroundAnAlt() => true; public int GetForegroundPid() => 1; }
    private sealed class NotElevated : IElevationCheck { public bool IsForegroundProcessLikelyElevated(int pid) => false; }
    private sealed class NoKeys : IKeyPress { public void Press(KeyCombo c) { } }
    private sealed class FakeMetrics : IWindowMetrics
    {
        public IntPtr HwndForPid(int pid) => new(0x10);
        public (int X, int Y)? ClientOrigin(IntPtr h) => (0, 0);
        public (int W, int H)? ClientSize(IntPtr h) => (800, 600);
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

    private static Trigger Held(int holdMs) => new()
    {
        Id = Guid.NewGuid(), Name = "held",
        Region = new RegionRect(0, 0, 10, 10), Mode = TriggerMode.Color,
        Color = new ColorCriteria(new Rgb(0, 0, 0), 5, ColorSamplingMode.SinglePixel),
        Keybind = new KeyCombo("F13", Array.Empty<string>()),
        AccountAware = false, CooldownMs = 100, HoldForMs = holdMs,
        Action = TriggerAction.RunMacro, MacroId = "held-macro",
    };

    private static (TriggerCoordinator C, FakeColor Color, FakeClock Clock, RecordingMacros Macros, ActivityLog Log) Make(Trigger t)
    {
        var store = new TriggerStore(Path.Combine(Path.GetTempPath(), "urocr-tests", Guid.NewGuid().ToString("N") + ".json"));
        store.Add(t);
        var color = new FakeColor();
        var clock = new FakeClock();
        var macros = new RecordingMacros();
        var log = new ActivityLog();
        var c = new TriggerCoordinator(store, new FakeCapture(), color, new NoText(), new Fg(), new NotElevated(),
            new NoKeys(), log, clock, new FakeMetrics(), macroClient: macros);
        return (c, color, clock, macros, log);
    }

    private static Task Tick(TriggerCoordinator c) => c.TickOnceAsync(CancellationToken.None);

    [Fact]
    public async Task A_held_match_fires_only_after_the_hold()
    {
        var (c, color, clock, macros, log) = Make(Held(1000));
        color.Result = true;

        await Tick(c);
        clock.Now = clock.Now.AddMilliseconds(500);
        await Tick(c);
        Assert.Empty(macros.Calls);
        Assert.Contains(log.Snapshot(), e => e.Kind == ActivityKind.Holding);

        clock.Now = clock.Now.AddMilliseconds(500);
        await Tick(c);
        Assert.Single(macros.Calls);
    }

    [Fact]
    public async Task A_break_in_the_match_restarts_the_hold()
    {
        var (c, color, clock, macros, _) = Make(Held(1000));
        color.Result = true;
        await Tick(c);                                            // t = 0

        color.Result = false;
        clock.Now = clock.Now.AddMilliseconds(600);
        await Tick(c);                                            // t = 600, broken

        color.Result = true;
        clock.Now = clock.Now.AddMilliseconds(100);
        await Tick(c);                                            // t = 700, hold starts again
        clock.Now = clock.Now.AddMilliseconds(900);
        await Tick(c);                                            // t = 1600, held 900
        Assert.Empty(macros.Calls);

        clock.Now = clock.Now.AddMilliseconds(100);
        await Tick(c);                                            // t = 1700, held 1000
        Assert.Single(macros.Calls);
    }

    [Fact]
    public async Task A_held_trigger_fires_again_after_another_full_hold()
    {
        var (c, color, clock, macros, _) = Make(Held(1000));
        color.Result = true;
        await Tick(c);
        clock.Now = clock.Now.AddMilliseconds(1000);
        await Tick(c);                                            // fires at 1000
        clock.Now = clock.Now.AddMilliseconds(500);
        await Tick(c);                                            // 1500: new hold at 500
        Assert.Single(macros.Calls);

        clock.Now = clock.Now.AddMilliseconds(500);
        await Tick(c);                                            // 2000: new hold complete
        Assert.Equal(2, macros.Calls.Count);
    }

    [Fact]
    public async Task Hold_zero_still_fires_on_the_edge()
    {
        var (c, color, _, macros, _) = Make(Held(0));
        color.Result = true;

        await Tick(c);

        Assert.Single(macros.Calls);
    }

    [Fact]
    public async Task A_hold_that_completes_inside_the_cooldown_fires_when_the_cooldown_ends()
    {
        var t = Held(50);
        t.CooldownMs = 100;                                       // the hold is shorter than the cooldown
        var (c, color, clock, macros, _) = Make(t);
        color.Result = true;
        await Tick(c);                                            // t = 0: hold starts
        clock.Now = clock.Now.AddMilliseconds(50);
        await Tick(c);                                            // t = 50: fires, fresh hold from 50
        Assert.Single(macros.Calls);

        clock.Now = clock.Now.AddMilliseconds(50);
        await Tick(c);                                            // t = 100: held again, 50 ms of cooldown left
        Assert.Single(macros.Calls);

        clock.Now = clock.Now.AddMilliseconds(50);
        await Tick(c);                                            // t = 150: cooldown over, still armed
        Assert.Equal(2, macros.Calls.Count);
    }
}
```

Create `tests/RoRoRo.UrOcr.Tests/Engine/LayerTriggerTests.cs`:

```csharp
using System.Drawing;
using System.IO;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

/// <summary>The rock cap (same layer for 5 minutes) and the camera rule (no layer for 10 s).</summary>
public class LayerTriggerTests
{
    private static readonly Rgb Navy = new(30, 30, 90);
    private static readonly Rgb Grey = new(120, 120, 120);
    private static readonly Rgb Sky = new(135, 206, 235);
    private static readonly Rgb Orange = new(240, 160, 40);

    private sealed class FakeClock : IClock { public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch; }
    private sealed class PaintedCapture : ICaptureSource
    {
        public Rgb Default = Navy;
        public Bitmap Capture(RegionRect r)
        {
            var bmp = new Bitmap(Math.Max(1, r.Width), Math.Max(1, r.Height));
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.FromArgb(Default.R, Default.G, Default.B));
            return bmp;
        }
    }
    private sealed class NoText : ITextMatchEngine
    {
        public Task<(bool matched, string text)> RunAsync(Bitmap b, TextCriteria c) => Task.FromResult((false, ""));
        public Task<(bool matched, string text)> RunWithPreprocessAsync(Bitmap b, TextCriteria c) => Task.FromResult((false, ""));
    }
    private sealed class Fg : IForegroundCheck { public bool IsAlt = true; public bool IsForegroundAnAlt() => IsAlt; public int GetForegroundPid() => 1; }
    private sealed class NotElevated : IElevationCheck { public bool IsForegroundProcessLikelyElevated(int pid) => false; }
    private sealed class NoKeys : IKeyPress { public void Press(KeyCombo c) { } }
    private sealed class FakeMetrics : IWindowMetrics
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

    private static RingDefinition Ring() => new("mine8", "Mine #8", new[]
    {
        new LayerDefinition("navy", new[] { Navy }),
        new LayerDefinition("grey", new[] { Grey }),
    }, MinLayerSpots: 3);

    private static Trigger Spot(int order, bool accountAware) => new()
    {
        Id = Guid.NewGuid(), Name = $"spot {order}",
        Region = new RegionRect(100 + order * 20, 100, 9, 9), Mode = TriggerMode.Color,
        Color = new ColorCriteria(new Rgb(0, 0, 0), 20, ColorSamplingMode.SinglePixel,
            Point: new PickPoint(4, 4), Box: new SampleBox(), NoneOf: new[] { Sky }),
        Keybind = new KeyCombo("F13", Array.Empty<string>()),
        AccountAware = accountAware, CooldownMs = 100,
        Action = TriggerAction.RunMacro, MacroId = $"mine-{order}",
        Ring = new RingSpot("mine8", order),
    };

    private static Trigger LayerTrigger(string name, LayerCondition condition, int holdMs, string macro, bool accountAware) => new()
    {
        Id = Guid.NewGuid(), Name = name,
        Region = new RegionRect(0, 0, 1, 1), Mode = TriggerMode.Layer,
        Layer = new LayerCriteria("mine8", condition), HoldForMs = holdMs,
        Keybind = new KeyCombo("F13", Array.Empty<string>()),
        AccountAware = accountAware, CooldownMs = 100,
        Action = TriggerAction.RunMacro, MacroId = macro,
    };

    private sealed record Rig(TriggerCoordinator C, PaintedCapture Paint, RecordingMacros Macros, FakeClock Clock, Fg Fg, List<string> Diag);

    private static Rig Build(bool accountAware, params Trigger[] layerTriggers)
    {
        var store = new TriggerStore(Path.Combine(Path.GetTempPath(), "urocr-tests", Guid.NewGuid().ToString("N") + ".json"));
        store.UpsertRing(Ring());
        for (var order = 0; order < 8; order++) store.Add(Spot(order, accountAware));
        foreach (var t in layerTriggers) store.Add(t);
        var paint = new PaintedCapture();
        var macros = new RecordingMacros();
        var clock = new FakeClock();
        var fg = new Fg();
        var diag = new List<string>();
        var c = new TriggerCoordinator(store, paint, new ColorMatcher(), new NoText(), fg, new NotElevated(),
            new NoKeys(), new ActivityLog(), clock, new FakeMetrics(), macroClient: macros, diag: diag.Add);
        return new Rig(c, paint, macros, clock, fg, diag);
    }

    private static Task Tick(Rig rig) => rig.C.TickOnceAsync(CancellationToken.None);
    private static void Advance(Rig rig, int ms) => rig.Clock.Now = rig.Clock.Now.AddMilliseconds(ms);

    [Fact]
    public async Task Same_layer_for_the_hold_runs_the_rock_cap()
    {
        var rig = Build(false, LayerTrigger("rock cap", LayerCondition.SameLayer, 300_000, "go-to-top", false));

        await Tick(rig);
        Advance(rig, 299_999);
        await Tick(rig);
        Assert.Empty(rig.Macros.Calls);

        Advance(rig, 1);
        await Tick(rig);
        Assert.Equal(new[] { "go-to-top" }, rig.Macros.Calls);
        // Spec decision 7 (amended): the reason goes to ur-ocr.log.
        Assert.Contains("trigger \"rock cap\": macro go-to-top (Went to top: 5 minutes on the navy layer)", rig.Diag);
    }

    [Fact]
    public async Task A_layer_change_restarts_the_rock_cap()
    {
        var rig = Build(false, LayerTrigger("rock cap", LayerCondition.SameLayer, 300_000, "go-to-top", false));
        await Tick(rig);                                  // navy from t = 0

        Advance(rig, 200_000);
        rig.Paint.Default = Grey;
        await Tick(rig);                                  // grey from t = 200000
        Advance(rig, 200_000);
        await Tick(rig);                                  // t = 400000: 200 s on grey
        Assert.Empty(rig.Macros.Calls);

        Advance(rig, 100_000);
        await Tick(rig);                                  // t = 500000: 300 s on grey
        Assert.Equal(new[] { "go-to-top" }, rig.Macros.Calls);
    }

    [Fact]
    public async Task The_rock_cap_repeats_after_another_full_hold()
    {
        var rig = Build(false, LayerTrigger("rock cap", LayerCondition.SameLayer, 300_000, "go-to-top", false));
        await Tick(rig);
        Advance(rig, 300_000);
        await Tick(rig);
        Advance(rig, 300_000);
        await Tick(rig);

        Assert.Equal(new[] { "go-to-top", "go-to-top" }, rig.Macros.Calls);
    }

    [Fact]
    public async Task No_layer_for_ten_seconds_runs_the_camera_macro()
    {
        var rig = Build(false, LayerTrigger("camera", LayerCondition.NoLayer, 10_000, "camera-top-down", false));
        rig.Paint.Default = Orange;                       // no spot matches any rock: no layer

        await Tick(rig);
        Advance(rig, 9_999);
        await Tick(rig);
        Assert.Empty(rig.Macros.Calls);                   // ore spots do not fire without a layer either

        Advance(rig, 1);
        await Tick(rig);
        Assert.Equal(new[] { "camera-top-down" }, rig.Macros.Calls);
    }

    [Fact]
    public async Task Tabbing_away_is_not_no_layer()
    {
        var rig = Build(true, LayerTrigger("camera", LayerCondition.NoLayer, 10_000, "camera-top-down", true));
        rig.Paint.Default = Orange;
        rig.Fg.IsAlt = false;

        await Tick(rig);
        Advance(rig, 20_000);
        await Tick(rig);
        Assert.Empty(rig.Macros.Calls);
        Assert.Equal(RingStatus.Unknown, rig.C.Rings.Get("mine8").Status);

        rig.Fg.IsAlt = true;
        await Tick(rig);                                  // t = 20000: no layer starts now
        Advance(rig, 9_999);
        await Tick(rig);
        Assert.Empty(rig.Macros.Calls);

        Advance(rig, 1);
        await Tick(rig);
        Assert.Equal(new[] { "camera-top-down" }, rig.Macros.Calls);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~DwellTests|FullyQualifiedName~LayerTriggerTests"`
Expected: build FAILS: `'ActivityKind' does not contain a definition for 'Holding'`.

- [ ] **Step 3: Add `ActivityKind.Holding`**

In `Engine/ActivityLog.cs`, replace

```csharp
public enum ActivityKind { Fired, WouldFire, NoMatch, SkippedCooldown, SkippedNotAlt, BlockedElevated, Error, Busy, Deferred, LayerChanged }
```

with

```csharp
public enum ActivityKind { Fired, WouldFire, NoMatch, SkippedCooldown, SkippedNotAlt, BlockedElevated, Error, Busy, Deferred, LayerChanged, Holding }
```

- [ ] **Step 4: Rewrite the coordinator with holds and layer triggers**

Replace the whole of `Engine/TriggerCoordinator.cs` with:

```csharp
using System.Diagnostics;
using System.Drawing;
using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

public interface ICaptureSource { Bitmap Capture(RegionRect region); }
public interface IColorMatchEngine
{
    bool Matches(Bitmap b, ColorCriteria c);
    ColorMatchResult Evaluate(Bitmap b, ColorCriteria c);
    /// <summary>recordedRegion is the trigger's stored region, so a pick point
    /// scales when a client-anchored capture comes back at a different size.</summary>
    ColorMatchResult Evaluate(Bitmap b, ColorCriteria c, RegionRect recordedRegion) => Evaluate(b, c);
}
public interface ITextMatchEngine
{
    Task<(bool matched, string text)> RunAsync(Bitmap b, TextCriteria c);
    Task<(bool matched, string text)> RunWithPreprocessAsync(Bitmap b, TextCriteria c);
}
public interface IForegroundCheck { bool IsForegroundAnAlt(); int GetForegroundPid(); }
public interface IElevationCheck { bool IsForegroundProcessLikelyElevated(int pid); }
public interface IKeyPress { void Press(KeyCombo combo); }
public interface IClock { DateTimeOffset Now { get; } }

public sealed class SystemClock : IClock { public DateTimeOffset Now => DateTimeOffset.UtcNow; }

/// <summary>
/// Each tick: read every trigger that may run (valid, foreground gate), update
/// every ring's layer from its spots' samples and judge the spots against that
/// layer, judge layer triggers from the rings, then decide and fire. Ring spots
/// fire in ring order, at most one per ring per tick; the rest stay armed for
/// the next tick. A trigger with HoldForMs fires only after its match has held
/// that long, and starts a fresh hold when it fires.
/// </summary>
public sealed class TriggerCoordinator(
    TriggerStore store,
    ICaptureSource capture,
    IColorMatchEngine color,
    ITextMatchEngine text,
    IForegroundCheck foreground,
    IElevationCheck elevation,
    IKeyPress keys,
    ActivityLog log,
    IClock clock,
    IWindowMetrics metrics,
    Action<Trigger>? onFirstFire = null,
    IMacroRunClient? macroClient = null,
    Action<string>? diag = null)
{
    /// <summary>The refusal reason Ur Task returns while a sequence is running.</summary>
    public const string BusyReason = "busy";

    public int TickRateHz { get; set; } = 5;
    public TimeSpan WatchdogTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public bool Paused { get; set; }
    public bool DryRun { get; set; }

    /// <summary>The current layer of every ring, updated each tick.</summary>
    public RingTracker Rings { get; } = new();

    private readonly Dictionary<Guid, bool> _wasMatched = new();
    // Trigger id -> earliest retry after a busy refusal. A trigger in here stays armed.
    private readonly Dictionary<Guid, DateTimeOffset> _retryAt = new();
    // Trigger id -> the validation problem last reported, so each problem is logged once.
    private readonly Dictionary<Guid, string> _lastProblem = new();
    // Trigger id -> what has been matching and since when, for HoldForMs.
    private readonly Dictionary<Guid, (string Key, DateTimeOffset Since)> _holding = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>One trigger's reading this tick. A ring spot carries its sample until its ring is judged.</summary>
    private sealed class Reading(Trigger trigger)
    {
        public Trigger Trigger { get; } = trigger;
        public bool Matched { get; set; }
        public string Detail { get; set; } = "";
        public Rgb? Sampled { get; set; }
        /// <summary>What the match is "of", for holds: a change restarts the hold (the layer name for SameLayer).</summary>
        public string HoldKey { get; set; } = "";
    }

    private enum FireOutcome { Fired, Busy }

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_loop is not null) await _loop;
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
                log.Record(Guid.Empty, "(coordinator)", ActivityKind.Error, "watchdog: tick exceeded 5s");
            }
            catch (Exception ex)
            {
                log.Record(Guid.Empty, "(coordinator)", ActivityKind.Error, ex.Message);
            }
            var elapsed = clock.Now - tickStart;
            var remain = period - elapsed;
            if (remain > TimeSpan.Zero) await Task.Delay(remain, ct);
        }
    }

    public async Task TickOnceAsync(CancellationToken ct)
    {
        if (Paused) return;
        var rings = store.Rings;

        var readings = new List<Reading>();
        foreach (var trig in store.All)
        {
            ct.ThrowIfCancellationRequested();
            if (!trig.Enabled) continue;
            if (!IsValid(trig, rings)) continue;
            if (!PassesGate(trig)) continue;
            var reading = await ReadAsync(trig);
            if (reading is not null) readings.Add(reading);
        }

        var now = clock.Now;
        foreach (var ring in rings) JudgeRing(ring, readings, now);
        JudgeLayerTriggers(readings);

        var claimedRings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reading in InFiringOrder(readings))
        {
            ct.ThrowIfCancellationRequested();
            await DecideAsync(reading, now, claimedRings, ct);
        }
    }

    private bool IsValid(Trigger trig, IReadOnlyList<RingDefinition> rings)
    {
        var problem = TriggerValidation.Validate(trig, rings);
        if (problem is null)
        {
            _lastProblem.Remove(trig.Id);
            return true;
        }
        if (!_lastProblem.TryGetValue(trig.Id, out var last) || last != problem)
        {
            _lastProblem[trig.Id] = problem;
            log.Record(trig.Id, trig.Name, ActivityKind.Error, problem);
            diag?.Invoke($"trigger \"{trig.Name}\" skipped: {problem}");
        }
        Disarm(trig.Id);
        return false;
    }

    private bool PassesGate(Trigger trig)
    {
        if (!trig.AccountAware && !trig.IsClientSpace) return true;
        if (!foreground.IsForegroundAnAlt())
        {
            log.Record(trig.Id, trig.Name, ActivityKind.SkippedNotAlt);
            Disarm(trig.Id);
            return false;
        }
        var pid = foreground.GetForegroundPid();
        if (elevation.IsForegroundProcessLikelyElevated(pid))
        {
            log.Record(trig.Id, trig.Name, ActivityKind.BlockedElevated);
            Disarm(trig.Id);
            return false;
        }
        return true;
    }

    private async Task<Reading?> ReadAsync(Trigger trig)
    {
        // Layer triggers read the ring, not the screen (JudgeLayerTriggers).
        if (trig.Mode == TriggerMode.Layer) return new Reading(trig);

        var captureRegion = TriggerRegionResolver.Resolve(trig, trig.IsClientSpace ? foreground.GetForegroundPid() : 0, metrics);
        if (captureRegion is null || captureRegion.Width < 1 || captureRegion.Height < 1)
        {
            // client trigger whose anchor window vanished mid-tick
            log.Record(trig.Id, trig.Name, ActivityKind.SkippedNotAlt, "anchor window unavailable");
            Disarm(trig.Id);
            return null;
        }
        using var bmp = capture.Capture(captureRegion);
        if (trig.Mode == TriggerMode.Text && trig.Text is not null)
        {
            var (m, t) = trig.OcrPreprocess
                ? await text.RunWithPreprocessAsync(bmp, trig.Text)
                : await text.RunAsync(bmp, trig.Text);
            return new Reading(trig) { Matched = m, Detail = t.Length > 0 ? $"OCR: {t}" : "" };
        }
        if (trig.Mode == TriggerMode.Color && trig.Color is not null)
        {
            var r = color.Evaluate(bmp, trig.Color, trig.Region);
            // A ring spot is judged once its ring's layer is known (JudgeRing).
            if (trig.Ring is not null) return new Reading(trig) { Sampled = r.Sampled };
            // Logged every match so the default tolerance can be tuned from real runs.
            return new Reading(trig) { Matched = r.Matched, Detail = ColorDetail(r) };
        }
        return null;
    }

    private void JudgeRing(RingDefinition ring, List<Reading> readings, DateTimeOffset now)
    {
        var spots = readings
            .Where(r => r.Sampled is not null && r.Trigger.Ring is { } s
                        && string.Equals(s.RingId, ring.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var samples = spots
            .Select(r => new SpotSample(r.Trigger.Ring!.Order, r.Sampled!, r.Trigger.Color!.ToleranceRgb))
            .ToList();

        var (state, changed) = Rings.Update(ring, samples, now);
        if (changed)
        {
            log.Record(Guid.Empty, $"(ring {ring.Name})", ActivityKind.LayerChanged, state.Describe());
            diag?.Invoke($"ring {ring.Name}: {state.Describe()}");
        }

        var layer = state.Status == RingStatus.OnLayer
            ? ring.Layers.First(l => string.Equals(l.Name, state.Layer, StringComparison.OrdinalIgnoreCase))
            : null;
        foreach (var r in spots)
        {
            if (layer is null)
            {
                // Without a layer there is no rock to compare against, so nothing counts as ore.
                r.Matched = false;
                r.Detail = $"{ColorNamer.Describe(r.Sampled!)} {state.Describe()}";
                continue;
            }
            var j = ColorMatcher.Judge(r.Sampled!, r.Trigger.Color!, layer.Rock);
            r.Matched = j.Matched;
            r.Detail = $"{layer.Name}: {ColorDetail(j)}";
        }
    }

    private void JudgeLayerTriggers(List<Reading> readings)
    {
        foreach (var r in readings)
        {
            if (r.Trigger.Mode != TriggerMode.Layer || r.Trigger.Layer is not { } c) continue;
            var s = Rings.Get(c.RingId);
            switch (c.Condition)
            {
                case LayerCondition.SameLayer:
                    r.Matched = s.Status == RingStatus.OnLayer;
                    r.HoldKey = s.Layer ?? "";
                    break;
                case LayerCondition.NoLayer:
                    // Unknown (not visible) is not NoLayer: tabbing away never moves the camera.
                    r.Matched = s.Status == RingStatus.NoLayer;
                    r.HoldKey = "no-layer";
                    break;
            }
            r.Detail = s.Describe();
        }
    }

    /// <summary>
    /// Store order, except that a ring's spots are taken together, at the place of
    /// the ring's first spot, sorted by ring order.
    /// </summary>
    private static IEnumerable<Reading> InFiringOrder(List<Reading> readings)
    {
        var firstIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < readings.Count; i++)
            if (readings[i].Trigger.Ring is { } s) firstIndex.TryAdd(s.RingId, i);

        return readings
            .Select((r, i) => (r, i))
            .OrderBy(x => x.r.Trigger.Ring is { } s ? firstIndex[s.RingId] : x.i)
            .ThenBy(x => x.r.Trigger.Ring?.Order ?? 0)
            .ThenBy(x => x.i)
            .Select(x => x.r);
    }

    private async Task DecideAsync(Reading r, DateTimeOffset now, HashSet<string> claimedRings, CancellationToken ct)
    {
        var trig = r.Trigger;
        var detail = r.Detail.Length > 0 ? r.Detail : null;

        if (!r.Matched)
        {
            _retryAt.Remove(trig.Id);
            _holding.Remove(trig.Id);
            _wasMatched[trig.Id] = false;
            log.Record(trig.Id, trig.Name, ActivityKind.NoMatch, detail);
            return;
        }

        if (trig.HoldForMs > 0)
        {
            var heldMs = HeldMs(trig.Id, r.HoldKey, now);
            if (heldMs < trig.HoldForMs)
            {
                // Not held long enough yet. Armed, so it fires the tick the hold completes.
                log.Record(trig.Id, trig.Name, ActivityKind.Holding,
                    $"{r.Detail} held {heldMs / 1000:0.0}s of {trig.HoldForMs / 1000.0:0.#}s".TrimStart());
                _wasMatched[trig.Id] = false;
                return;
            }
        }

        var was = _wasMatched.GetValueOrDefault(trig.Id, false);

        if (!was && _retryAt.TryGetValue(trig.Id, out var retryAt) && now < retryAt)
        {
            // Ur Task was busy: stay armed, and keep this spot's turn in its ring.
            if (trig.Ring is { } waiting) claimedRings.Add(waiting.RingId);
            log.Record(trig.Id, trig.Name, ActivityKind.SkippedCooldown,
                $"{(retryAt - now).TotalMilliseconds:0}ms (Ur Task was busy)");
            return;
        }

        var cooldownReady = trig.LastFiredAt is null
            || (now - trig.LastFiredAt.Value).TotalMilliseconds >= trig.CooldownMs;
        if (!cooldownReady)
        {
            // An edge that lands inside the cooldown is spent, as it always has been. A held
            // trigger is not an edge: its hold is complete, so it stays armed and fires the tick
            // the cooldown ends (otherwise a hold shorter than the cooldown would never fire again).
            var remain = trig.CooldownMs - (now - trig.LastFiredAt!.Value).TotalMilliseconds;
            log.Record(trig.Id, trig.Name, ActivityKind.SkippedCooldown, $"{remain:0}ms");
            _wasMatched[trig.Id] = trig.HoldForMs == 0;
            return;
        }
        if (was) return;   // edge already spent

        if (trig.Ring is { } spot)
        {
            if (claimedRings.Contains(spot.RingId))
            {
                // A spot earlier in ring order went this tick; this one stays armed for the next.
                log.Record(trig.Id, trig.Name, ActivityKind.Deferred, "another ring spot went first");
                return;
            }
            claimedRings.Add(spot.RingId);
        }

        // Spec decision 7 (amended): Ur OCR says why it sends the account up; Ur Task only
        // logs the Go to Top playback's ending, since RunMacro carries no reason.
        if (trig.Layer is { Condition: LayerCondition.SameLayer })
            detail = $"Went to top: {RockCapMinutes(trig.HoldForMs)} minutes on the {r.HoldKey} layer";

        var outcome = await FireAsync(trig, detail, now, ct);
        if (outcome == FireOutcome.Busy)
        {
            _retryAt[trig.Id] = now.AddMilliseconds(trig.CooldownMs);
            _wasMatched[trig.Id] = false;
            return;
        }
        _retryAt.Remove(trig.Id);
        _wasMatched[trig.Id] = true;
        // A held trigger starts a fresh hold when it fires: it fires again only after another full hold.
        if (trig.HoldForMs > 0) _holding[trig.Id] = (r.HoldKey, now);
    }

    /// <summary>300000 ms is "5", 90000 ms is "1.5". Invariant culture, so the log reads the same everywhere.</summary>
    private static string RockCapMinutes(int holdForMs) =>
        (holdForMs / 60000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    private async Task<FireOutcome> FireAsync(Trigger trig, string? detail, DateTimeOffset now, CancellationToken ct)
    {
        if (DryRun)
        {
            log.Record(trig.Id, trig.Name, ActivityKind.WouldFire, detail);
            return FireOutcome.Fired;
        }
        if (trig.Action == TriggerAction.RunMacro && macroClient is not null && trig.MacroId is not null)
        {
            var resp = await macroClient.RunAsync(trig.MacroId, trig.MacroTargets, ct).ConfigureAwait(false);
            if (!resp.Ok && string.Equals(resp.Reason, BusyReason, StringComparison.OrdinalIgnoreCase))
            {
                // Not a fire: nothing ran. The caller keeps the trigger armed.
                log.Record(trig.Id, trig.Name, ActivityKind.Busy, $"Ur Task busy, retry in {trig.CooldownMs}ms");
                Diag(trig, $"Ur Task busy, retry in {trig.CooldownMs}ms");
                return FireOutcome.Busy;
            }
            store.MarkFired(trig.Id, now);
            var what = resp.Ok ? $"macro {trig.MacroId}" : $"macro refused: {resp.Reason}";
            log.Record(trig.Id, trig.Name, ActivityKind.Fired, what);
            Diag(trig, detail is null ? what : $"{what} ({detail})");
            if (!trig.FirstFireConfirmed) onFirstFire?.Invoke(trig);
            return FireOutcome.Fired;
        }
        keys.Press(trig.Keybind);
        store.MarkFired(trig.Id, now);
        log.Record(trig.Id, trig.Name, ActivityKind.Fired, detail);
        if (!trig.FirstFireConfirmed) onFirstFire?.Invoke(trig);
        return FireOutcome.Fired;
    }

    /// <summary>How long the same match (same key) has held; starts the clock on a new key.</summary>
    private double HeldMs(Guid id, string key, DateTimeOffset now)
    {
        if (_holding.TryGetValue(id, out var h) && h.Key == key) return (now - h.Since).TotalMilliseconds;
        _holding[id] = (key, now);
        return 0;
    }

    private void Disarm(Guid id)
    {
        _wasMatched[id] = false;
        _retryAt.Remove(id);
        _holding.Remove(id);
    }

    // ur-ocr.log carries the ring's decisions so a session can read them after a run.
    private void Diag(Trigger trig, string message)
    {
        if (trig.Ring is null && trig.Mode != TriggerMode.Layer) return;
        diag?.Invoke($"trigger \"{trig.Name}\": {message}");
    }

    private static string ColorDetail(ColorMatchResult r)
    {
        if (r.Nearest is { } n)
            return $"{ColorNamer.Describe(r.Sampled)} nearest {ColorNamer.Hex(n)} d={r.Distance:F1}";
        return $"{ColorNamer.Describe(r.Sampled)} d={r.Distance:F1}"
            + (r.DistanceToOther is { } o ? $" other={o:F1}" : "");
    }
}
```

- [ ] **Step 5: Run the tests, then the full suite**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~DwellTests|FullyQualifiedName~LayerTriggerTests"`
Expected: PASS, 10 tests.

Run: `dotnet build rororo-ur-ocr.csproj`
Expected: `Build succeeded.`

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj`
Expected: PASS, 195 tests, 0 failed.

- [ ] **Step 6: Commit**

```powershell
git add Engine/TriggerCoordinator.cs Engine/ActivityLog.cs tests/RoRoRo.UrOcr.Tests/Engine/DwellTests.cs tests/RoRoRo.UrOcr.Tests/Engine/LayerTriggerTests.cs
git commit -m "feat(engine): hold before firing, and layer triggers" -m "holdForMs: a match must hold unbroken that long before the trigger fires, and a fire starts a fresh hold. Layer triggers read the ring: same layer (the rock cap, Go to Top after 5 minutes on one layer, restarted by a layer change) and no layer (the camera rule, Camera top-down after 10 s). A ring that is not visible never counts as no layer. The rock cap writes its reason to ur-ocr.log (Went to top: 5 minutes on the grey layer), and a hold that completes inside the cooldown fires when the cooldown ends." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Capture sweep tools

**Files:**
- Create: `tools/ring-sweep.ps1`
- Create: `tools/ring-sample.ps1`
- Create: `tools/ring-fit.ps1`

**Interfaces:**
- Consumes: the measured-values file shape (Global Constraints). The sample box and clamping in `ring-sample.ps1` mirror `ColorMatcher.AverageBox` (Task 1).
- Produces: in a sweep folder, `frames.csv` (`frame,time,file,w,h,scale`) and `frame-NNNN.png`; `samples.csv` (`frame,time,spot,order,x,y,r,g,b,hex`) and `ring-NNNN.png` contact sheets; `ring-fit.ps1 -Write` fills `layers`, `ignore`, `toleranceRgb` of the measured file. Task 9 runs these live.

- [ ] **Step 1: Create `tools/ring-sweep.ps1`**

```powershell
# Ring sweep: saves the foreground Roblox window's game area (client area, pixel for pixel, no
# grid) every few seconds, for measuring the ore-stop ring. Pair with ring-sample.ps1.
#
# Read-only: never activates, moves, resizes, or clicks anything. A tick is skipped (and says so)
# whenever the foreground window is not Roblox, so tabbing away is harmless.
#
#   pwsh -File tools\ring-sweep.ps1 -OutDir C:\sweep                   every 3 s for 15 minutes
#   pwsh -File tools\ring-sweep.ps1 -OutDir C:\sweep -Seconds 2 -Minutes 30
#   pwsh -File tools\ring-sweep.ps1 -OutDir C:\sweep -Demo             two synthetic 800x599 frames
param(
    [Parameter(Mandatory = $true)][string]$OutDir,
    [double]$Seconds = 3,
    [double]$Minutes = 15,
    [switch]$Demo
)

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class Sweep {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int l, t, r, b; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
}
'@
[void][Sweep]::SetProcessDpiAwarenessContext([IntPtr](-4))

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$index = Join-Path $OutDir "frames.csv"
if (-not (Test-Path $index)) { "frame,time,file,w,h,scale" | Set-Content -Path $index -Encoding utf8 }
$n = @(Import-Csv $index).Count

function Save-Frame($bmp, [int]$scale) {
    $script:n++
    $name = "frame-{0:0000}.png" -f $script:n
    $bmp.Save((Join-Path $OutDir $name), [System.Drawing.Imaging.ImageFormat]::Png)
    "{0},{1},{2},{3},{4},{5}" -f $script:n, (Get-Date -Format "HH:mm:ss"), $name, $bmp.Width, $bmp.Height, $scale |
        Add-Content -Path $index -Encoding utf8
    "frame $($script:n): $name $($bmp.Width)x$($bmp.Height) at $scale%"
}

if ($Demo) {
    # Frame 1: navy rock, an orange ore block east of the character. Frame 2: grey rock all round.
    # The character is a white block at (400, 300); the demo spots sit 40 px from it.
    foreach ($spec in @(@{ rock = @(30, 30, 90); ore = $true }, @{ rock = @(120, 120, 120); ore = $false })) {
        $bmp = New-Object System.Drawing.Bitmap 800, 599
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.Clear([System.Drawing.Color]::FromArgb($spec.rock[0], $spec.rock[1], $spec.rock[2]))
        $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(250, 250, 250))), 390, 290, 20, 20)
        if ($spec.ore) {
            $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(240, 160, 40))), 430, 290, 20, 20)
        }
        $g.Dispose()
        Save-Frame $bmp 100
        $bmp.Dispose()
    }
    exit 0
}

$end = (Get-Date).AddMinutes($Minutes)
while ((Get-Date) -lt $end) {
    $h = [Sweep]::GetForegroundWindow()
    $procId = [uint32]0
    [void][Sweep]::GetWindowThreadProcessId($h, [ref]$procId)
    $proc = Get-Process -Id $procId -ErrorAction SilentlyContinue
    if (-not $proc -or $proc.ProcessName -ne "RobloxPlayerBeta") {
        "skip: the foreground window is $(if ($proc) { $proc.ProcessName } else { 'nothing' }), not Roblox"
    } else {
        $cr = New-Object Sweep+RECT; [void][Sweep]::GetClientRect($h, [ref]$cr)
        $pt = New-Object Sweep+POINT; [void][Sweep]::ClientToScreen($h, [ref]$pt)
        if ($cr.r -gt 100 -and $cr.b -gt 100) {
            $bmp = New-Object System.Drawing.Bitmap $cr.r, $cr.b
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            $g.CopyFromScreen($pt.x, $pt.y, 0, 0, (New-Object System.Drawing.Size $cr.r, $cr.b))
            $g.Dispose()
            Save-Frame $bmp ([math]::Round([Sweep]::GetDpiForWindow($h) / 96.0 * 100))
            $bmp.Dispose()
        }
    }
    Start-Sleep -Milliseconds ([int]($Seconds * 1000))
}
```

- [ ] **Step 2: Create `tools/ring-sample.ps1`**

```powershell
# For each frame from ring-sweep.ps1, averages every ring spot's sample box (the same box, clamped
# the same way, as Ur OCR's ColorMatcher.AverageBox) and writes samples.csv next to the frames.
# Also writes ring-NNNN.png per frame: the eight spot crops, enlarged, laid out around the
# character (centre tile), with the sample box outlined and the hex printed, for labelling by eye.
#
#   pwsh -File tools\ring-sample.ps1 -Frames C:\sweep -Measured C:\sweep\mine8.measured.json
param(
    [Parameter(Mandatory = $true)][string]$Frames,
    [Parameter(Mandatory = $true)][string]$Measured,
    [int]$Crop = 24,
    [int]$Zoom = 4
)

Add-Type -AssemblyName System.Drawing

$m = Get-Content $Measured -Raw | ConvertFrom-Json
$index = Join-Path $Frames "frames.csv"
if (-not (Test-Path $index)) { "NO frames.csv in ${Frames}: run ring-sweep.ps1 first"; exit 2 }
if (@($m.spots).Count -ne 8) { "the measured file lists $(@($m.spots).Count) spots, not 8"; exit 2 }
$box = $m.box
$rw = [int]$m.recordedClientW; $rh = [int]$m.recordedClientH

function Average-Box($bmp, [int]$px, [int]$py) {
    $x0 = [math]::Min([math]::Max($px + [int]$box.offsetX, 0), $bmp.Width - 1)
    $y0 = [math]::Min([math]::Max($py + [int]$box.offsetY, 0), $bmp.Height - 1)
    $x1 = [math]::Min([math]::Max($px + [int]$box.offsetX + [int]$box.w, $x0 + 1), $bmp.Width)
    $y1 = [math]::Min([math]::Max($py + [int]$box.offsetY + [int]$box.h, $y0 + 1), $bmp.Height)
    $r = 0; $gg = 0; $b = 0; $count = 0
    for ($y = $y0; $y -lt $y1; $y++) {
        for ($x = $x0; $x -lt $x1; $x++) {
            $c = $bmp.GetPixel($x, $y); $r += $c.R; $gg += $c.G; $b += $c.B; $count++
        }
    }
    # Integer division, as Ur OCR's AverageRect does.
    return @([int][math]::Floor($r / $count), [int][math]::Floor($gg / $count), [int][math]::Floor($b / $count))
}

$cells = @{ N = @(1, 0); NE = @(2, 0); E = @(2, 1); SE = @(2, 2); S = @(1, 2); SW = @(0, 2); W = @(0, 1); NW = @(0, 0) }
$tile = $Crop * $Zoom
$strip = 18
$font = New-Object System.Drawing.Font "Consolas", 9, ([System.Drawing.FontStyle]::Bold)
$white = [System.Drawing.Brushes]::White
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 255, 220, 0)), 1

function Draw-Crop($g, $bmp, [int]$px, [int]$py, [int]$col, [int]$row, [string]$label) {
    $half = [int]($Crop / 2)
    $src = New-Object System.Drawing.Rectangle ($px - $half), ($py - $half), $Crop, $Crop
    $x = $col * $tile; $y = $row * ($tile + $strip)
    $dst = New-Object System.Drawing.Rectangle $x, $y, $tile, $tile
    $g.DrawImage($bmp, $dst, $src, [System.Drawing.GraphicsUnit]::Pixel)
    $bx = $x + ($half + [int]$box.offsetX) * $Zoom
    $by = $y + ($half + [int]$box.offsetY) * $Zoom
    $g.DrawRectangle($pen, $bx, $by, [int]$box.w * $Zoom - 1, [int]$box.h * $Zoom - 1)
    $g.DrawString($label, $font, $white, [float]($x + 2), [float]($y + $tile + 2))
}

$out = Join-Path $Frames "samples.csv"
"frame,time,spot,order,x,y,r,g,b,hex" | Set-Content -Path $out -Encoding utf8
$sheets = 0
foreach ($f in (Import-Csv $index)) {
    $bmp = [System.Drawing.Bitmap]::FromFile((Join-Path $Frames $f.file))
    if ($bmp.Width -ne $rw -or $bmp.Height -ne $rh) {
        "note: frame $($f.frame) is $($bmp.Width)x$($bmp.Height); spots scaled from ${rw}x${rh}"
    }
    $sx = $bmp.Width / $rw; $sy = $bmp.Height / $rh
    $sheet = New-Object System.Drawing.Bitmap (3 * $tile), (3 * ($tile + $strip))
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $g.Clear([System.Drawing.Color]::Black)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $cx = 0; $cy = 0
    foreach ($s in $m.spots) {
        $px = [int][math]::Round($s.x * $sx); $py = [int][math]::Round($s.y * $sy)
        $cx += $px; $cy += $py
        $avg = Average-Box $bmp $px $py
        $hex = "#{0:X2}{1:X2}{2:X2}" -f $avg[0], $avg[1], $avg[2]
        "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9}" -f $f.frame, $f.time, $s.name, $s.order, $px, $py, $avg[0], $avg[1], $avg[2], $hex |
            Add-Content -Path $out -Encoding utf8
        $cell = $cells[[string]$s.name]
        Draw-Crop $g $bmp $px $py $cell[0] $cell[1] "$($s.name) $hex"
    }
    Draw-Crop $g $bmp ([int]($cx / 8)) ([int]($cy / 8)) 1 1 "self"
    $g.Dispose()
    $sheet.Save((Join-Path $Frames ("ring-{0:0000}.png" -f [int]$f.frame)), [System.Drawing.Imaging.ImageFormat]::Png)
    $sheet.Dispose(); $bmp.Dispose()
    $sheets++
}
"wrote $out and $sheets ring sheets"
```

- [ ] **Step 3: Create `tools/ring-fit.ps1`**

```powershell
# Fits the ring's colours from labelled samples. Inputs: samples.csv from ring-sample.ps1, the
# measured-values JSON, and a labels.csv you write while looking at the ring-NNNN.png sheets:
#
#   frame,spot,kind,layer
#   12,E,ore,navy
#   12,*,rock,navy          (* = every spot of that frame not labelled on its own row)
#
# kind is rock, ore, empty (sky or a mined-out hole), self (the character), or skip.
# layer is the layer the frame shows; every row except skip needs one.
#
# For each layer it picks up to -MaxColours rock colours that cover the layer's rock samples within
# tolerance (greedy: the sample colour covering the most still-uncovered samples, then the next),
# and the same for the ignore list from empty and self samples. Then it checks the fit the way
# Ur OCR runs it, and lists:
#   MISSED ORE   an ore sample within tolerance of its layer's rock or the ignore list (never stopped for)
#   FALSE STOP   a rock, empty or self sample outside every chosen colour (a short needless mine)
#   WRONG LAYER  a frame whose votes pick another layer, or no layer
# With -Write it puts layers, ignore and toleranceRgb into the measured-values JSON.
#
#   pwsh -File tools\ring-fit.ps1 -Samples C:\sweep\samples.csv -Labels C:\sweep\labels.csv -Measured C:\sweep\mine8.measured.json
#   ... -Tolerance 25 -Write
param(
    [Parameter(Mandatory = $true)][string]$Samples,
    [Parameter(Mandatory = $true)][string]$Labels,
    [Parameter(Mandatory = $true)][string]$Measured,
    [int]$Tolerance = -1,
    [int]$MaxColours = 6,
    [switch]$Write
)

Add-Type -TypeDefinition @'
using System; using System.Collections.Generic;
public static class RingFit {
  public static double Dist(int[] a, int[] b) {
    double dr = a[0] - b[0], dg = a[1] - b[1], db = a[2] - b[2];
    return Math.Sqrt(dr * dr + dg * dg + db * db);
  }
  public static double Nearest(int[] c, List<int[]> set) {
    double best = double.PositiveInfinity;
    foreach (var s in set) { var d = Dist(c, s); if (d < best) best = d; }
    return best;
  }
  public static List<int[]> Cover(List<int[]> colours, double tol, int max) {
    var cands = new List<int[]>(); var seen = new HashSet<string>();
    foreach (var c in colours) if (seen.Add(c[0] + "," + c[1] + "," + c[2])) cands.Add(c);
    var uncovered = new List<int[]>(colours); var chosen = new List<int[]>();
    while (uncovered.Count > 0 && chosen.Count < max) {
      int[] best = null; int bestN = 0;
      foreach (var cand in cands) {
        int k = 0; foreach (var u in uncovered) if (Dist(cand, u) <= tol) k++;
        if (k > bestN) { bestN = k; best = cand; }
      }
      if (best == null) break;
      chosen.Add(best);
      var picked = best;
      uncovered.RemoveAll(u => Dist(picked, u) <= tol);
    }
    return chosen;
  }
}
'@

function Hex($c) { "#{0:X2}{1:X2}{2:X2}" -f $c[0], $c[1], $c[2] }

$m = Get-Content $Measured -Raw | ConvertFrom-Json
if ($Tolerance -lt 0) { $Tolerance = [int]$m.toleranceRgb }
$tol = [double]$Tolerance
$minSpots = [int]$m.minLayerSpots

$exact = @{}; $wild = @{}
foreach ($l in (Import-Csv $Labels)) {
    if ($l.spot -eq '*') { $wild[[int]$l.frame] = $l } else { $exact["$([int]$l.frame)|$($l.spot)"] = $l }
}
$rows = [System.Collections.Generic.List[object]]::new()
foreach ($s in (Import-Csv $Samples)) {
    $f = [int]$s.frame
    $l = $exact["$f|$($s.spot)"]
    if (-not $l) { $l = $wild[$f] }
    if (-not $l -or $l.kind -eq 'skip') { continue }
    if ($l.kind -notin @('rock', 'ore', 'empty', 'self')) { "BAD LABEL frame $f $($s.spot): kind '$($l.kind)'"; exit 2 }
    if (-not $l.layer) { "BAD LABEL frame $f $($s.spot): no layer"; exit 2 }
    $rows.Add([pscustomobject]@{ frame = $f; spot = $s.spot; kind = $l.kind; layer = $l.layer; c = [int[]]@([int]$s.r, [int]$s.g, [int]$s.b) })
}
if ($rows.Count -eq 0) { "NO LABELLED SAMPLES: check labels.csv against samples.csv"; exit 2 }

$layerNames = [System.Collections.Generic.List[string]]::new()
foreach ($r in $rows) { if (-not $layerNames.Contains($r.layer)) { $layerNames.Add($r.layer) } }

$ignoreIn = [System.Collections.Generic.List[int[]]]::new()
foreach ($r in $rows) { if ($r.kind -in @('empty', 'self')) { $ignoreIn.Add($r.c) } }
$ignore = [RingFit]::Cover($ignoreIn, $tol, $MaxColours)
"ignore: $($ignoreIn.Count) samples -> $($ignore.Count) colours: $((@($ignore | ForEach-Object { Hex $_ })) -join ' ')"

$fitted = @{}
foreach ($name in $layerNames) {
    $rockIn = [System.Collections.Generic.List[int[]]]::new()
    foreach ($r in $rows) { if ($r.layer -eq $name -and $r.kind -eq 'rock') { $rockIn.Add($r.c) } }
    $fitted[$name] = [RingFit]::Cover($rockIn, $tol, $MaxColours)
    "layer ${name}: $($rockIn.Count) rock samples -> $($fitted[$name].Count) colours: $((@($fitted[$name] | ForEach-Object { Hex $_ })) -join ' ')"
    if ($rockIn.Count -eq 0) { "WARNING: layer $name has no rock samples; Ur OCR will reject it" }
}

$missed = 0; $falseStops = 0; $wrong = 0
foreach ($r in $rows) {
    $d = [math]::Min([RingFit]::Nearest($r.c, $fitted[$r.layer]), [RingFit]::Nearest($r.c, $ignore))
    if ($r.kind -eq 'ore') {
        if ($d -le $tol) { "MISSED ORE   frame {0} {1} {2} d={3:F1}" -f $r.frame, $r.spot, (Hex $r.c), $d; $missed++ }
    } elseif ($d -gt $tol) {
        "FALSE STOP   frame {0} {1} {2} ({3}) d={4:F1}" -f $r.frame, $r.spot, (Hex $r.c), $r.kind, $d; $falseStops++
    }
}

foreach ($group in ($rows | Group-Object frame)) {
    $labelled = $group.Group[0].layer
    $best = $null; $bestVotes = -1; $line = @()
    foreach ($name in $layerNames) {
        $v = 0
        foreach ($r in $group.Group) { if ([RingFit]::Nearest($r.c, $fitted[$name]) -le $tol) { $v++ } }
        $line += "$name $v"
        if ($v -gt $bestVotes) { $bestVotes = $v; $best = $name }
    }
    if ($bestVotes -lt $minSpots) { $best = "no layer" }
    if ($best -ne $labelled) {
        "WRONG LAYER  frame {0}: labelled {1}, reads {2} ({3})" -f $group.Name, $labelled, $best, ($line -join ', ')
        $wrong++
    }
}

$verdict = if ($missed + $falseStops + $wrong -eq 0) { "fit clean" } else { "fit has problems" }
"{0}: {1} missed ore, {2} false stops, {3} wrong layers (tolerance {4}, {5} samples)" -f $verdict, $missed, $falseStops, $wrong, $Tolerance, $rows.Count

if ($Write) {
    $toJson = { param($list) @(foreach ($c in $list) { [pscustomobject]@{ r = $c[0]; g = $c[1]; b = $c[2] } }) }
    $layersOut = @(foreach ($name in $layerNames) { [pscustomobject]@{ name = $name; rock = @(& $toJson $fitted[$name]) } })
    $m | Add-Member -Force -NotePropertyName layers -NotePropertyValue $layersOut
    $m | Add-Member -Force -NotePropertyName ignore -NotePropertyValue @(& $toJson $ignore)
    $m | Add-Member -Force -NotePropertyName toleranceRgb -NotePropertyValue $Tolerance
    $m | ConvertTo-Json -Depth 8 | Set-Content -Path $Measured -Encoding utf8
    "wrote $($layerNames.Count) layers and $($ignore.Count) ignore colours to $Measured"
}
```

- [ ] **Step 4: Check the three tools end to end on the demo frames**

Run in PowerShell:

```powershell
$d = Join-Path $env:TEMP "ring-tools-check"
Remove-Item -Recurse -Force $d -ErrorAction SilentlyContinue
pwsh -NoProfile -File tools\ring-sweep.ps1 -OutDir $d -Demo
@'
{
  "schema": 1, "ringId": "demo", "name": "Demo", "recordedClientW": 800, "recordedClientH": 599,
  "scalePercent": 100, "toleranceRgb": 30, "minLayerSpots": 3,
  "box": { "offsetX": -2, "offsetY": -2, "w": 5, "h": 5 },
  "spots": [
    { "order": 0, "name": "N",  "x": 400, "y": 260, "macro": "Mine spot N" },
    { "order": 1, "name": "NE", "x": 440, "y": 260, "macro": "Mine spot NE" },
    { "order": 2, "name": "E",  "x": 440, "y": 300, "macro": "Mine spot E" },
    { "order": 3, "name": "SE", "x": 440, "y": 340, "macro": "Mine spot SE" },
    { "order": 4, "name": "S",  "x": 400, "y": 340, "macro": "Mine spot S" },
    { "order": 5, "name": "SW", "x": 360, "y": 340, "macro": "Mine spot SW" },
    { "order": 6, "name": "W",  "x": 360, "y": 300, "macro": "Mine spot W" },
    { "order": 7, "name": "NW", "x": 360, "y": 260, "macro": "Mine spot NW" }
  ],
  "ignore": [], "layers": [],
  "rockCap": { "holdForMs": 300000, "macro": "Go to Top", "cooldownMs": 5000 },
  "camera": { "holdForMs": 10000, "macro": "Camera top-down", "cooldownMs": 5000 },
  "spotCooldownMs": 1000
}
'@ | Set-Content -Path (Join-Path $d "demo.measured.json") -Encoding utf8
pwsh -NoProfile -File tools\ring-sample.ps1 -Frames $d -Measured (Join-Path $d "demo.measured.json")
Select-String -Path (Join-Path $d "samples.csv") -Pattern "^1,.*,E,|^2,.*,E,"
@'
frame,spot,kind,layer
1,E,ore,navy
1,*,rock,navy
2,*,rock,grey
'@ | Set-Content -Path (Join-Path $d "labels.csv") -Encoding utf8
pwsh -NoProfile -File tools\ring-fit.ps1 -Samples (Join-Path $d "samples.csv") -Labels (Join-Path $d "labels.csv") -Measured (Join-Path $d "demo.measured.json") -Write
Get-Content (Join-Path $d "demo.measured.json") -Raw | ConvertFrom-Json | Select-Object -ExpandProperty layers | ConvertTo-Json -Depth 5 -Compress
```

Expected, in order:
- `frame 1: frame-0001.png 800x599 at 100%` and `frame 2: frame-0002.png 800x599 at 100%`.
- `wrote ...samples.csv and 2 ring sheets`.
- The two `Select-String` lines end in `,E,2,440,300,240,160,40,#F0A028` (frame 1) and `,E,2,440,300,120,120,120,#787878` (frame 2).
- `layer navy: 7 rock samples -> 1 colours: #1E1E5A`, `layer grey: 8 rock samples -> 1 colours: #787878`, then `fit clean: 0 missed ore, 0 false stops, 0 wrong layers (tolerance 30, 16 samples)`.
- The last line: `[{"name":"navy","rock":[{"r":30,"g":30,"b":90}]},{"name":"grey","rock":[{"r":120,"g":120,"b":120}]}]`.

Open `ring-0001.png` in the demo folder (Read it as an image): the E tile is orange, the centre tile white, the other six navy, each with a yellow 5x5 box outline. If any expectation differs, fix the script before committing.

- [ ] **Step 5: Commit**

```powershell
git add tools/ring-sweep.ps1 tools/ring-sample.ps1 tools/ring-fit.ps1
git commit -m "feat(tools): ring sweep, sample and fit scripts for the ore-stop capture" -m "ring-sweep saves the foreground Roblox game area every few seconds; ring-sample averages each ring spot's 5x5 box the way Ur OCR does and draws a contact sheet per frame; ring-fit picks each layer's rock colours and the ignore list from labelled samples, reports missed ore, false stops and wrong layers, and writes them into the measured-values file. Read-only against the game." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Ring import command, and 0.5.0

**Files:**
- Create: `Storage/MeasuredRing.cs`
- Create: `Storage/RingImporter.cs`
- Create: `RingImportCommand.cs`
- Modify: `Program.cs` (whole file)
- Modify: `rororo-ur-ocr.csproj`, `manifest.json`, `CHANGELOG.md`, `README.md`
- Test: `tests/RoRoRo.UrOcr.Tests/Storage/RingImporterTests.cs`
- Test: `tests/RoRoRo.UrOcr.Tests/RingImportCommandTests.cs`

**Interfaces:**
- Consumes: `RingDefinition`, `LayerDefinition`, `RingSpot`, `LayerCriteria`, `LayerCondition`, `TriggerMode.Layer`, `Trigger.HoldForMs`, `TriggerStore.Upsert`, `TriggerStore.UpsertRing`, `TriggerValidation.Validate(...)`, `TriggerValidation.MaxHoldForMs` (Task 2); `ColorCriteria(... NoneOf:)`, `ColorCriteria.InRange`, `ColorCriteria.MaxTolerance` (Task 1); `UrTaskMacro(string Id, string Name)`, `UrTaskMacros.Load(string dir)`, `UrTaskMacros.MacrosDir` (existing); `PluginPaths.TriggersFile`, `PluginPaths.PluginDataDir` (existing); `DiagLog.Write` (existing, internal).
- Produces:
  - `record MeasuredSpot(int Order, string Name, int X, int Y, string Macro)`, `record MeasuredLayer(string Name, IReadOnlyList<Rgb> Rock)`, `record MeasuredAction(int HoldForMs, string Macro, int CooldownMs = 5000)`
  - `record MeasuredRing(int Schema, string RingId, string Name, int RecordedClientW, int RecordedClientH, int ScalePercent, int ToleranceRgb, int MinLayerSpots, SampleBox Box, IReadOnlyList<MeasuredSpot> Spots, IReadOnlyList<Rgb> Ignore, IReadOnlyList<MeasuredLayer> Layers, MeasuredAction RockCap, MeasuredAction Camera, int SpotCooldownMs = 1000)` with `CurrentSchema = 1`, `static string[] RingOrder`, `static MeasuredRing Load(string path)`, `string? Validate()`
  - `record RingImportResult(RingDefinition Ring, IReadOnlyList<Trigger> Triggers)`; `static class RingImporter`: `RingImportResult Build(MeasuredRing m, IReadOnlyList<UrTaskMacro> macros)`, `RingImportResult Apply(TriggerStore store, MeasuredRing m, IReadOnlyList<UrTaskMacro> macros)`, `string ResolveMacro(IReadOnlyList<UrTaskMacro> macros, string nameOrId)`, `Guid StableId(string ringId, string role)`
  - `internal static class RingImportCommand`: `const string Flag = "--import-ring"`, `static string ReportPath`, `static int Run(IReadOnlyList<string> args)`, `static int Run(IReadOnlyList<string> args, string triggersPath, string macrosDir, Func<bool> otherInstanceRunning, string reportPath, Action<string> diag)`. Exit codes 0 imported, 2 bad input, 3 Ur OCR running, 64 usage.

- [ ] **Step 1: Write the failing tests**

Create `tests/RoRoRo.UrOcr.Tests/Storage/RingImporterTests.cs`:

```csharp
using System.IO;
using System.Text.Json;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Storage;

public class RingImporterTests
{
    private static int Dx(string n) => n.Contains('E') ? 40 : n.Contains('W') ? -40 : 0;
    private static int Dy(string n) => n.Contains('N') ? -40 : n.Contains('S') ? 40 : 0;

    internal static MeasuredRing Measured() => new(
        Schema: 1, RingId: "mine8", Name: "Mine #8",
        RecordedClientW: 800, RecordedClientH: 599, ScalePercent: 100,
        ToleranceRgb: 30, MinLayerSpots: 3, Box: new SampleBox(),
        Spots: MeasuredRing.RingOrder.Select((n, i) => new MeasuredSpot(i, n, 400 + Dx(n), 300 + Dy(n), $"Mine spot {n}")).ToList(),
        Ignore: new[] { new Rgb(135, 206, 235) },
        Layers: new[]
        {
            new MeasuredLayer("navy", new[] { new Rgb(30, 30, 90) }),
            new MeasuredLayer("grey", new[] { new Rgb(120, 120, 120) }),
        },
        RockCap: new MeasuredAction(300_000, "Go to Top"),
        Camera: new MeasuredAction(10_000, "Camera top-down"));

    internal static IReadOnlyList<UrTaskMacro> Macros() =>
        MeasuredRing.RingOrder.Select(n => new UrTaskMacro($"id-mine-{n}", $"Mine spot {n}"))
            .Append(new UrTaskMacro("id-go-to-top", "Go to Top"))
            .Append(new UrTaskMacro("id-camera", "Camera top-down"))
            .ToList();

    private static TriggerStore TempStore() =>
        new(Path.Combine(Path.GetTempPath(), "urocr-tests", Guid.NewGuid().ToString("N") + ".json"));

    [Fact]
    public void Builds_the_ring_eight_spots_the_rock_cap_and_the_camera_rule()
    {
        var result = RingImporter.Build(Measured(), Macros());

        Assert.Equal("mine8", result.Ring.Id);
        Assert.Equal(3, result.Ring.MinLayerSpots);
        Assert.Equal(new[] { "navy", "grey" }, result.Ring.Layers.Select(l => l.Name));
        Assert.Equal(10, result.Triggers.Count);
        Assert.Equal(8, result.Triggers.Count(t => t.Ring is not null));
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, result.Triggers.Where(t => t.Ring is not null).Select(t => t.Ring!.Order));
    }

    [Fact]
    public void A_spot_samples_the_measured_point_with_the_measured_box()
    {
        var e = RingImporter.Build(Measured(), Macros()).Triggers.Single(t => t.Name == "Mine #8: E");

        Assert.Equal(new RegionRect(437, 297, 7, 7), e.Region);   // (440, 300) with a reach of 3
        Assert.Equal(new PickPoint(3, 3), e.Color!.Point);
        Assert.Equal(new SampleBox(), e.Color.Box);
        Assert.Equal(new[] { new Rgb(135, 206, 235) }, e.Color.NoneOf);
        Assert.Equal(30, e.Color.ToleranceRgb);
        Assert.Equal(new RingSpot("mine8", 2), e.Ring);
        Assert.Equal(Trigger.CoordSpaceClient, e.CoordSpace);
        Assert.Equal(800, e.RecordedClientW);
        Assert.Equal(599, e.RecordedClientH);
        Assert.True(e.AccountAware);
        Assert.Equal(TriggerAction.RunMacro, e.Action);
        Assert.Equal("id-mine-E", e.MacroId);
        Assert.Equal(1000, e.CooldownMs);
    }

    [Fact]
    public void Layer_triggers_carry_the_hold_and_the_macro()
    {
        var triggers = RingImporter.Build(Measured(), Macros()).Triggers;
        var cap = triggers.Single(t => t.Layer?.Condition == LayerCondition.SameLayer);
        var camera = triggers.Single(t => t.Layer?.Condition == LayerCondition.NoLayer);

        Assert.Equal(TriggerMode.Layer, cap.Mode);
        Assert.Equal(300_000, cap.HoldForMs);
        Assert.Equal("id-go-to-top", cap.MacroId);
        Assert.Equal(10_000, camera.HoldForMs);
        Assert.Equal("id-camera", camera.MacroId);
        Assert.Equal("mine8", camera.Layer!.RingId);
    }

    [Fact]
    public void Every_built_trigger_passes_validation()
    {
        var result = RingImporter.Build(Measured(), Macros());

        Assert.All(result.Triggers, t => Assert.Null(TriggerValidation.Validate(t, new[] { result.Ring })));
    }

    [Fact]
    public void Ids_are_stable_across_builds()
    {
        var a = RingImporter.Build(Measured(), Macros()).Triggers.Select(t => t.Id);
        var b = RingImporter.Build(Measured(), Macros()).Triggers.Select(t => t.Id);

        Assert.Equal(a, b);
        Assert.Equal(10, a.Distinct().Count());
    }

    [Fact]
    public void A_reimport_replaces_instead_of_adding()
    {
        var store = TempStore();
        var unrelated = new Trigger
        {
            Id = Guid.NewGuid(), Name = "unrelated", Region = new RegionRect(0, 0, 10, 10), Mode = TriggerMode.Color,
            Color = new ColorCriteria(new Rgb(0, 0, 0), 5, ColorSamplingMode.SinglePixel),
            Keybind = new KeyCombo("A", Array.Empty<string>()),
        };
        store.Add(unrelated);

        RingImporter.Apply(store, Measured(), Macros());
        RingImporter.Apply(store, Measured() with { ToleranceRgb = 25 }, Macros());

        Assert.Equal(11, store.All.Count);
        Assert.Single(store.Rings);
        Assert.All(store.All.Where(t => t.Ring is not null), t => Assert.Equal(25, t.Color!.ToleranceRgb));
    }

    [Fact]
    public void A_missing_macro_names_the_macro_and_writes_nothing()
    {
        var store = TempStore();
        var macros = Macros().Where(m => m.Name != "Go to Top").ToList();

        var ex = Assert.Throws<InvalidDataException>(() => RingImporter.Apply(store, Measured(), macros));

        Assert.Contains("Go to Top", ex.Message);
        Assert.Empty(store.All);
        Assert.Empty(store.Rings);
    }

    [Fact]
    public void Two_macros_with_one_name_is_an_error()
    {
        var macros = Macros().Append(new UrTaskMacro("id-other", "Go to Top")).ToList();

        Assert.Throws<InvalidDataException>(() => RingImporter.Build(Measured(), macros));
    }

    [Fact]
    public void A_macro_can_be_named_by_id()
    {
        var m = Measured() with { RockCap = new MeasuredAction(300_000, "id-go-to-top") };

        var cap = RingImporter.Build(m, Macros()).Triggers.Single(t => t.Layer?.Condition == LayerCondition.SameLayer);

        Assert.Equal("id-go-to-top", cap.MacroId);
    }

    public static TheoryData<string, MeasuredRing> Invalid() => new()
    {
        { "7 spots", Measured() with { Spots = Measured().Spots.Take(7).ToList() } },
        { "125%", Measured() with { ScalePercent = 125 } },
        { "outside", Measured() with { Spots = Measured().Spots.Select(s => s.Order == 0 ? s with { Y = 700 } : s).ToList() } },
        { "misnamed", Measured() with { Spots = Measured().Spots.Select(s => s.Order == 1 ? s with { Name = "E" } : s).ToList() } },
        { "no layers", Measured() with { Layers = Array.Empty<MeasuredLayer>() } },
        { "empty rock", Measured() with { Layers = new[] { new MeasuredLayer("navy", Array.Empty<Rgb>()) } } },
        { "schema 2", Measured() with { Schema = 2 } },
        { "no hold", Measured() with { RockCap = new MeasuredAction(0, "Go to Top") } },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void An_invalid_measured_file_is_refused(string why, MeasuredRing m)
    {
        Assert.False(string.IsNullOrEmpty(why));
        Assert.NotNull(m.Validate());
        Assert.Throws<InvalidDataException>(() => RingImporter.Build(m, Macros()));
    }

    [Fact]
    public void Reads_the_documented_shape()
    {
        var path = Path.Combine(Path.GetTempPath(), "urocr-tests", Guid.NewGuid().ToString("N") + ".measured.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
        {
          "schema": 1,
          "ringId": "mine8",
          "name": "Mine #8",
          "recordedClientW": 800,
          "recordedClientH": 599,
          "scalePercent": 100,
          "toleranceRgb": 30,
          "minLayerSpots": 3,
          "box": { "offsetX": -2, "offsetY": -2, "w": 5, "h": 5 },
          "spots": [
            { "order": 0, "name": "N",  "x": 400, "y": 260, "macro": "Mine spot N" },
            { "order": 1, "name": "NE", "x": 440, "y": 260, "macro": "Mine spot NE" },
            { "order": 2, "name": "E",  "x": 440, "y": 300, "macro": "Mine spot E" },
            { "order": 3, "name": "SE", "x": 440, "y": 340, "macro": "Mine spot SE" },
            { "order": 4, "name": "S",  "x": 400, "y": 340, "macro": "Mine spot S" },
            { "order": 5, "name": "SW", "x": 360, "y": 340, "macro": "Mine spot SW" },
            { "order": 6, "name": "W",  "x": 360, "y": 300, "macro": "Mine spot W" },
            { "order": 7, "name": "NW", "x": 360, "y": 260, "macro": "Mine spot NW" }
          ],
          "ignore": [ { "r": 135, "g": 206, "b": 235 } ],
          "layers": [
            { "name": "navy", "rock": [ { "r": 30, "g": 30, "b": 90 } ] },
            { "name": "grey", "rock": [ { "r": 120, "g": 120, "b": 120 } ] }
          ],
          "rockCap": { "holdForMs": 300000, "macro": "Go to Top", "cooldownMs": 5000 },
          "camera": { "holdForMs": 10000, "macro": "Camera top-down", "cooldownMs": 5000 },
          "spotCooldownMs": 1000
        }
        """);

        var m = MeasuredRing.Load(path);

        Assert.Null(m.Validate());
        Assert.Equal("mine8", m.RingId);
        Assert.Equal(new MeasuredSpot(2, "E", 440, 300, "Mine spot E"), m.Spots[2]);
        Assert.Equal(new SampleBox(-2, -2, 5, 5), m.Box);
        Assert.Equal(300_000, m.RockCap.HoldForMs);
        Assert.Equal(5000, m.Camera.CooldownMs);
        Assert.Equal(new Rgb(120, 120, 120), m.Layers[1].Rock[0]);
    }
}
```

Create `tests/RoRoRo.UrOcr.Tests/RingImportCommandTests.cs`:

```csharp
using System.IO;
using System.Text.Json;
using RoRoRo.UrOcr.Storage;
using RoRoRo.UrOcr.Tests.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests;

public class RingImportCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "urocr-tests", "import-" + Guid.NewGuid().ToString("N"));

    public RingImportCommandTests() => Directory.CreateDirectory(Path.Combine(_dir, "macros"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Triggers => Path.Combine(_dir, "triggers.json");
    private string Report => Path.Combine(_dir, "ring-import.log");
    private string MacrosDir => Path.Combine(_dir, "macros");

    private string WriteMeasured()
    {
        var path = Path.Combine(_dir, "mine8.measured.json");
        File.WriteAllText(path, JsonSerializer.Serialize(RingImporterTests.Measured(), TriggerJsonOptions.Default));
        return path;
    }

    private void WriteMacros()
    {
        foreach (var mac in RingImporterTests.Macros())
            File.WriteAllText(Path.Combine(MacrosDir, mac.Id + ".json"), JsonSerializer.Serialize(new { id = mac.Id, name = mac.Name }));
    }

    private int Run(Func<bool> running, params string[] args) =>
        RingImportCommand.Run(args, Triggers, MacrosDir, running, Report, _ => { });

    [Fact]
    public void Imports_the_ring_and_reports_it()
    {
        WriteMacros();

        var code = Run(() => false, WriteMeasured());

        Assert.Equal(0, code);
        Assert.Equal(10, new TriggerStore(Triggers).All.Count);
        Assert.Contains("imported ring mine8", File.ReadAllText(Report));
    }

    [Fact]
    public void Refuses_while_Ur_OCR_is_running()
    {
        WriteMacros();

        var code = Run(() => true, WriteMeasured());

        Assert.Equal(3, code);
        Assert.False(File.Exists(Triggers));
        Assert.Contains("Close it first", File.ReadAllText(Report));
    }

    [Fact]
    public void Missing_macros_exit_2_and_write_nothing()
    {
        var code = Run(() => false, WriteMeasured());

        Assert.Equal(2, code);
        Assert.False(File.Exists(Triggers));
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
    public void No_argument_prints_usage()
    {
        Assert.Equal(64, Run(() => false));
        Assert.Contains("usage", File.ReadAllText(Report));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~RingImporterTests|FullyQualifiedName~RingImportCommandTests"`
Expected: build FAILS: `The type or namespace name 'MeasuredRing' could not be found`, `The name 'RingImportCommand' does not exist`.

- [ ] **Step 3: Create `Storage/MeasuredRing.cs`**

```csharp
using System.IO;
using System.Text.Json;

namespace RoRoRo.UrOcr.Storage;

public sealed record MeasuredSpot(int Order, string Name, int X, int Y, string Macro);
public sealed record MeasuredLayer(string Name, IReadOnlyList<Rgb> Rock);
public sealed record MeasuredAction(int HoldForMs, string Macro, int CooldownMs = 5000);

/// <summary>
/// The measured-values file the live capture sweep writes (docs/reference/ore-stop/).
/// Ur OCR imports it as a ring (RingImporter); Ur Task's macro generator reads the
/// same file for the spot points. Coordinates are game-area pixels at the recorded
/// client size, recorded at 100% display scale. Macro fields are Ur Task macro names.
/// </summary>
public sealed record MeasuredRing(
    int Schema, string RingId, string Name,
    int RecordedClientW, int RecordedClientH, int ScalePercent,
    int ToleranceRgb, int MinLayerSpots, SampleBox Box,
    IReadOnlyList<MeasuredSpot> Spots, IReadOnlyList<Rgb> Ignore, IReadOnlyList<MeasuredLayer> Layers,
    MeasuredAction RockCap, MeasuredAction Camera, int SpotCooldownMs = 1000)
{
    public const int CurrentSchema = 1;

    /// <summary>Ring order: order 0 is N, then clockwise.</summary>
    public static readonly string[] RingOrder = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    public static MeasuredRing Load(string path) =>
        JsonSerializer.Deserialize<MeasuredRing>(File.ReadAllText(path), TriggerJsonOptions.Default)
        ?? throw new InvalidDataException($"{path} is empty.");

    /// <summary>Null when the file can be imported, else one sentence naming the first problem.</summary>
    public string? Validate()
    {
        if (Schema != CurrentSchema) return $"schema must be {CurrentSchema}, not {Schema}.";
        if (string.IsNullOrWhiteSpace(RingId)) return "ringId is missing.";
        if (string.IsNullOrWhiteSpace(Name)) return "name is missing.";
        if (RecordedClientW < 1 || RecordedClientH < 1) return "recordedClientW and recordedClientH must be positive.";
        if (ScalePercent != 100) return $"The sweep must be recorded at 100% display scale, not {ScalePercent}%.";
        if (ToleranceRgb < 1 || ToleranceRgb > ColorCriteria.MaxTolerance)
            return $"toleranceRgb must be 1 to {ColorCriteria.MaxTolerance}.";
        if (MinLayerSpots < 1 || MinLayerSpots > RingOrder.Length) return $"minLayerSpots must be 1 to {RingOrder.Length}.";
        if (Box is null || !Box.IsValid) return $"box must be 1 to {SampleBox.MaxSide} pixels a side.";
        if (Spots is null || Spots.Count != RingOrder.Length) return $"spots must list exactly {RingOrder.Length} spots.";
        for (var i = 0; i < RingOrder.Length; i++)
        {
            var s = Spots.FirstOrDefault(x => x is not null && x.Order == i);
            if (s is null) return $"No spot has order {i}.";
            if (!string.Equals(s.Name, RingOrder[i], StringComparison.Ordinal))
                return $"Spot {i} must be named {RingOrder[i]}, not {s.Name}.";
            if (s.X < 0 || s.Y < 0 || s.X >= RecordedClientW || s.Y >= RecordedClientH)
                return $"Spot {s.Name} ({s.X}, {s.Y}) is outside the {RecordedClientW}x{RecordedClientH} game area.";
            if (string.IsNullOrWhiteSpace(s.Macro)) return $"Spot {s.Name} names no macro.";
        }
        if (Ignore is null) return "ignore is missing (use [] for none).";
        if (Ignore.Any(c => c is null || !ColorCriteria.InRange(c))) return "An ignore colour has a channel outside 0 to 255.";
        if (Layers is null || Layers.Count == 0) return "layers is empty: run ring-fit.ps1 -Write first.";
        var ring = new RingDefinition(RingId, Name,
            Layers.Select(l => new LayerDefinition(l?.Name ?? "", l?.Rock ?? Array.Empty<Rgb>())).ToList(), MinLayerSpots);
        if (TriggerValidation.Validate(ring) is { } ringProblem) return ringProblem;
        foreach (var (label, a) in new[] { ("rockCap", RockCap), ("camera", Camera) })
        {
            if (a is null) return $"{label} is missing.";
            if (a.HoldForMs < 1 || a.HoldForMs > TriggerValidation.MaxHoldForMs)
                return $"{label}.holdForMs must be 1 to {TriggerValidation.MaxHoldForMs}.";
            if (a.CooldownMs < 0) return $"{label}.cooldownMs cannot be negative.";
            if (string.IsNullOrWhiteSpace(a.Macro)) return $"{label} names no macro.";
        }
        if (SpotCooldownMs < 0) return "spotCooldownMs cannot be negative.";
        return null;
    }
}
```

- [ ] **Step 4: Create `Storage/RingImporter.cs`**

```csharp
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RoRoRo.UrOcr.Storage;

public sealed record RingImportResult(RingDefinition Ring, IReadOnlyList<Trigger> Triggers);

/// <summary>
/// Turns a measured ring into its ring definition and ten triggers: eight spots
/// (none-of checks against the ring's current layer, each running its "Mine spot"
/// macro), the rock cap (same layer, runs Go to Top) and the camera rule (no layer,
/// runs Camera top-down). Ids are derived from ring and role, so a re-import
/// replaces the same ten triggers.
/// </summary>
public static class RingImporter
{
    public static RingImportResult Build(MeasuredRing m, IReadOnlyList<UrTaskMacro> macros)
    {
        if (m.Validate() is { } problem) throw new InvalidDataException(problem);

        var ring = new RingDefinition(m.RingId, m.Name,
            m.Layers.Select(l => new LayerDefinition(l.Name, l.Rock.ToList())).ToList(), m.MinLayerSpots);

        // The capture region reaches far enough around the point to hold the whole box.
        var reach = Math.Max(1, new[] { -m.Box.OffsetX, -m.Box.OffsetY, m.Box.OffsetX + m.Box.W, m.Box.OffsetY + m.Box.H }.Max());

        Trigger Base(string role, string label, RegionRect region, string macro, int cooldownMs) => new()
        {
            Id = StableId(m.RingId, role),
            Name = $"{m.Name}: {label}",
            Enabled = true,
            Region = region,
            Mode = TriggerMode.Color,
            AccountAware = true,
            Keybind = new KeyCombo("F13", Array.Empty<string>()),   // never pressed: these run macros
            CoordSpace = Trigger.CoordSpaceClient,
            RecordedClientW = m.RecordedClientW,
            RecordedClientH = m.RecordedClientH,
            Action = TriggerAction.RunMacro,
            MacroId = ResolveMacro(macros, macro),
            CooldownMs = cooldownMs,
            FirstFireConfirmed = true,                              // agent-authored: no first-fire toast
        };

        var triggers = new List<Trigger>();
        foreach (var s in m.Spots.OrderBy(x => x.Order))
        {
            var t = Base("spot-" + s.Name, s.Name,
                new RegionRect(s.X - reach, s.Y - reach, 2 * reach + 1, 2 * reach + 1), s.Macro, m.SpotCooldownMs);
            t.Color = new ColorCriteria(new Rgb(0, 0, 0), m.ToleranceRgb, ColorSamplingMode.SinglePixel,
                Point: new PickPoint(reach, reach), Box: m.Box, NoneOf: m.Ignore.ToList());
            t.Ring = new RingSpot(m.RingId, s.Order);
            triggers.Add(t);
        }

        var whole = new RegionRect(0, 0, m.RecordedClientW, m.RecordedClientH);
        var cap = Base("rock-cap", "rock cap", whole, m.RockCap.Macro, m.RockCap.CooldownMs);
        cap.Mode = TriggerMode.Layer;
        cap.Layer = new LayerCriteria(m.RingId, LayerCondition.SameLayer);
        cap.HoldForMs = m.RockCap.HoldForMs;
        triggers.Add(cap);

        var camera = Base("camera", "camera top-down", whole, m.Camera.Macro, m.Camera.CooldownMs);
        camera.Mode = TriggerMode.Layer;
        camera.Layer = new LayerCriteria(m.RingId, LayerCondition.NoLayer);
        camera.HoldForMs = m.Camera.HoldForMs;
        triggers.Add(camera);

        foreach (var t in triggers)
            if (TriggerValidation.Validate(t, new[] { ring }) is { } p) throw new InvalidDataException($"{t.Name}: {p}");
        return new RingImportResult(ring, triggers);
    }

    /// <summary>Builds first, so a bad file or a missing macro writes nothing.</summary>
    public static RingImportResult Apply(TriggerStore store, MeasuredRing m, IReadOnlyList<UrTaskMacro> macros)
    {
        var result = Build(m, macros);
        store.UpsertRing(result.Ring);
        foreach (var t in result.Triggers) store.Upsert(t);
        return result;
    }

    /// <summary>A macro by name (exactly one), else by id. Ur Task's RunMacro takes the id.</summary>
    public static string ResolveMacro(IReadOnlyList<UrTaskMacro> macros, string nameOrId)
    {
        var byName = macros.Where(x => string.Equals(x.Name, nameOrId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byName.Count > 1)
            throw new InvalidDataException($"{byName.Count} Ur Task macros are named \"{nameOrId}\". Rename or delete the extras.");
        if (byName.Count == 1) return byName[0].Id;
        var byId = macros.FirstOrDefault(x => string.Equals(x.Id, nameOrId, StringComparison.OrdinalIgnoreCase));
        return byId?.Id ?? throw new InvalidDataException(
            $"No Ur Task macro is named \"{nameOrId}\". Generate the ore-stop macros in Ur Task first.");
    }

    /// <summary>The same ring and role always give the same id.</summary>
    public static Guid StableId(string ringId, string role)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(
            $"626labs.ur-ocr/ring/{ringId.ToLowerInvariant()}/{role.ToLowerInvariant()}"));
        return new Guid(bytes);
    }
}
```

- [ ] **Step 5: Create `RingImportCommand.cs`**

```csharp
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using RoRoRo.UrOcr.Diagnostics;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr;

/// <summary>
/// RoRoRo.UrOcr.exe --import-ring &lt;measured.json&gt; writes a measured ring (its layers,
/// eight spot triggers, the rock cap and the camera rule) into triggers.json and exits
/// without opening a window. Ur OCR must be closed: a running instance rewrites
/// triggers.json from memory. The result goes to ring-import.log in the plugin data
/// folder and to ur-ocr.log. Exit codes: 0 imported, 2 bad input, 3 Ur OCR running, 64 usage.
/// </summary>
internal static class RingImportCommand
{
    public const string Flag = "--import-ring";

    public static string ReportPath => Path.Combine(PluginPaths.PluginDataDir, "ring-import.log");

    public static int Run(IReadOnlyList<string> args) =>
        Run(args, PluginPaths.TriggersFile, UrTaskMacros.MacrosDir, OtherInstanceRunning, ReportPath, DiagLog.Write);

    public static int Run(IReadOnlyList<string> args, string triggersPath, string macrosDir,
        Func<bool> otherInstanceRunning, string reportPath, Action<string> diag)
    {
        var lines = new List<string>();
        int code;
        if (args.Count != 1)
        {
            lines.Add($"usage: RoRoRo.UrOcr.exe {Flag} <measured.json>");
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
                var measured = MeasuredRing.Load(args[0]);
                var macros = UrTaskMacros.Load(macrosDir);
                var result = RingImporter.Build(measured, macros);   // fails before the store is touched
                var store = new TriggerStore(triggersPath);
                if (store.CorruptedBackupPath is { } backup)
                    lines.Add($"triggers.json was unreadable; the old file is kept at {backup}");
                RingImporter.Apply(store, measured, macros);
                lines.Add($"imported ring {result.Ring.Id} ({result.Ring.Name}): {result.Ring.Layers.Count} layers, " +
                          $"{result.Triggers.Count} triggers into {triggersPath}");
                foreach (var t in result.Triggers) lines.Add($"  {t.Name} -> macro {t.MacroId}");
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
        foreach (var line in lines) diag($"ring import: {line}");
        return code;
    }

    private static bool OtherInstanceRunning()
    {
        var me = Environment.ProcessId;
        return Process.GetProcessesByName("RoRoRo.UrOcr").Any(p => p.Id != me);
    }
}
```

- [ ] **Step 6: Route the flag in `Program.cs`**

Replace the whole of `Program.cs` with:

```csharp
namespace RoRoRo.UrOcr;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Headless: write a measured ring into triggers.json and exit, no window.
        if (args.Length > 0 && string.Equals(args[0], RingImportCommand.Flag, StringComparison.OrdinalIgnoreCase))
            return RingImportCommand.Run(args.Skip(1).ToArray());

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
```

- [ ] **Step 7: Run the tests, build, and the full suite**

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj --filter "FullyQualifiedName~RingImporterTests|FullyQualifiedName~RingImportCommandTests"`
Expected: PASS, 23 tests.

Run: `dotnet build rororo-ur-ocr.csproj`
Expected: `Build succeeded.`

Run: `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj`
Expected: PASS, 218 tests, 0 failed.

- [ ] **Step 8: Check the command against the real exe (usage path only)**

A real import writes the live `%LOCALAPPDATA%\626Labs\rororo-ur-ocr\triggers.json`, so this check runs only the usage path. It leaves `triggers.json` untouched. It does write two things: it overwrites `%LOCALAPPDATA%\626Labs\rororo-ur-ocr\ring-import.log` with the usage line, and it appends one `ring import: usage: ...` line to the live `ur-ocr.log`. Both are harmless.

```powershell
$exe = (Resolve-Path "bin\Debug\net10.0-windows10.0.19041.0\RoRoRo.UrOcr.exe").Path
$p = Start-Process -FilePath $exe -ArgumentList "--import-ring" -Wait -PassThru
$p.ExitCode
Get-Content "$env:LOCALAPPDATA\626Labs\rororo-ur-ocr\ring-import.log"
```

Expected: `64`, then `usage: RoRoRo.UrOcr.exe --import-ring <measured.json>`. No window opens.

- [ ] **Step 9: Bump to 0.5.0 and record it**

In `rororo-ur-ocr.csproj`, replace `<Version>0.4.0</Version>` with `<Version>0.5.0</Version>`.

In `manifest.json`, replace `"version": "0.4.0",` with `"version": "0.5.0",`.

In `CHANGELOG.md`, replace the line `## Unreleased` with `## 0.5.0 — ` followed by today's date from `Get-Date -Format yyyy-MM-dd` (for example `## 0.5.0 — 2026-09-28`). The existing Fixed and Added entries from the colour-pick-box work stay under it.

In `CHANGELOG.md`, replace

```markdown
- The activity log names the colour it saw and its distance on every colour check, e.g. `green #8BE03A d=4.1`, in the same words Ur Task uses.
```

with

```markdown
- The activity log names the colour it saw and its distance on every colour check, e.g. `green #8BE03A d=4.1`, in the same words Ur Task uses.
- **Ore stop: ring triggers.** Eight colour triggers can form a ring around your character and share one reading of which mine layer you are on: the layer whose rock the most spots match (at least three). A spot fires its macro when its colour is none of that layer's rock and none of its ignore colours, so every ore gets stopped for. With no layer, nothing fires. Built for PS99's Mining League.
- **None-of colour checks.** A colour trigger can list colours and match when the sample is near none of them (`noneOf`), in place of one target colour.
- **Hold before firing.** `holdForMs`: the match must hold that long, unbroken, before the trigger fires, and it fires again only after another full hold. The rock cap (same layer for 5 minutes, runs Go to Top) and the camera rule (no layer for 10 seconds, runs Camera top-down) are layer triggers built on it. Tabbing away from Roblox never counts as "no layer". When the rock cap fires, `ur-ocr.log` says why, e.g. `Went to top: 5 minutes on the grey layer`.
- **Busy retry.** When Ur Task refuses a macro because a sequence is already running, the trigger stays armed and tries again after its cooldown while it still matches. A busy refusal no longer counts as a fire.
- **Ring order.** When several ring spots match at once, the first in ring order (N, NE, E, SE, S, SW, W, NW) runs its macro; the rest wait their turn.
- `RoRoRo.UrOcr.exe --import-ring <measured.json>` writes a measured ring into triggers.json without opening a window. Close Ur OCR first. The capture sweep that produces the file lives in `tools/`.
- Ring layer changes, ring fires and busy retries are written to `ur-ocr.log`.
```

In `README.md`, replace

```markdown
## Capabilities
```

with

```markdown
## Ore stop (ring triggers)

Built for PS99's Mining League. Eight colour triggers form a ring around your character in a top-down camera view and share one reading of which mine layer you are on. A spot runs its "Mine spot" macro in Ur Task when its colour is none of that layer's rock, so every ore gets stopped for. Two layer triggers ride along: after 5 minutes on one layer the account goes back to the top, and when no layer reads for 10 seconds the camera is set top-down again.

The ring is measured, not drawn by hand. The capture sweep in `tools/` writes a measured-values file (`docs/reference/ore-stop/`), and Ur OCR imports it while closed:

    RoRoRo.UrOcr.exe --import-ring mine8.measured.json

The result is in `ring-import.log` next to `triggers.json`. Only the foreground account is watched, as with every account-aware trigger.

## Capabilities
```

In `README.md`, replace

```markdown
- **Elevated foreground windows block fires**
```

with

```markdown
- **Ring spots show no live preview.** A ring spot is judged against the mine layer the whole ring reads, which the trigger editor does not know, so its match meter stays blank. Watch the activity log or `ur-ocr.log` instead.
- **Elevated foreground windows block fires**
```

- [ ] **Step 10: Final full run, then commit**

Run: `dotnet build rororo-ur-ocr.csproj` then `dotnet test tests/RoRoRo.UrOcr.Tests/RoRoRo.UrOcr.Tests.csproj`
Expected: `Build succeeded.`; PASS, 218 tests.

Run: `Select-String -Path rororo-ur-ocr.csproj, manifest.json -Pattern "0\.5\.0"`
Expected: one hit in each file.

```powershell
git add Storage/MeasuredRing.cs Storage/RingImporter.cs RingImportCommand.cs Program.cs rororo-ur-ocr.csproj manifest.json CHANGELOG.md README.md tests/RoRoRo.UrOcr.Tests/Storage/RingImporterTests.cs tests/RoRoRo.UrOcr.Tests/RingImportCommandTests.cs
git commit -m "feat: import a measured ring, and 0.5.0" -m "RoRoRo.UrOcr.exe --import-ring <measured.json> turns the capture sweep's measured-values file into a ring, its eight spot triggers, the rock cap and the camera rule, headless. Macro names resolve to Ur Task ids; ids are stable so a re-import replaces. It refuses while Ur OCR runs and writes nothing on a bad file or a missing macro. Bumps csproj and manifest to 0.5.0, with changelog and readme." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Live capture sweep and live run on Dunder-MiffLan (manual, Este present)

**Files:**
- Create: `docs/reference/ore-stop/mine8.measured.json`
- Modify, committed in the sibling repo: `..\rororo-ur-task\docs\reference\events\macros\space-mine-ore-stop\measured.json` (`ring.spots`, `ring.box`, `ring.measuredOn`, the `camera` group, `goToTop.check`) and its regenerated `macros\*.json`
- Sweep working folder (not committed): `%LOCALAPPDATA%\626Labs\ore-stop-sweep\<yyyy-MM-dd>\`

**Interfaces:**
- Consumes: `tools/ring-sweep.ps1`, `tools/ring-sample.ps1`, `tools/ring-fit.ps1` (Task 7); `--import-ring` (Task 8); `..\rororo-ur-task\tools\grid-capture.ps1` (existing, sibling repo); from the Ur Task plan: its `measured.json` (`client`, `goToTop`, `ring`, `camera`), `generate.ps1`, `OreStopExampleMacrosTests`, the hold step, and the macros `Camera top-down`, `Go to Top`, `Auto Mine off (checked)`, `Auto Mine on (checked)`, `Mine spot N` ... `Mine spot NW`.
- Produces: the measured Mine #8 ring, committed here; the measured ring, camera and Go to Top check, committed in `..\rororo-ur-task`; the regenerated macros installed in Ur Task; 0.5.0 triggers imported on Dunder-MiffLan; a live-run record in the commit message.

**Dependencies:** Tasks 1 to 8 are on `feat/ore-stop` and built on Dunder-MiffLan. The Ur Task plan is complete on its own `feat/ore-stop` branch in `..\rororo-ur-task` (check with `git -C ..\rororo-ur-task branch --show-current`), with its 0.10.0 build installed, so its `measured.json`, `generate.ps1` and hold step exist. Run every block from the Ur-OCR repo root.

**Shell variables do not carry between commands.** Step 1 writes the sweep folder's path to a file once; every later block starts with this prelude, which reads it back:

```powershell
$sweep = (Get-Content (Join-Path $env:LOCALAPPDATA "626Labs\ore-stop-sweep\current.txt") -Raw).Trim()
$utDir = "..\rororo-ur-task\docs\reference\events\macros\space-mine-ore-stop"
$utMeasured = Join-Path $utDir "measured.json"
```

- [ ] **Step 1: Preconditions (Este at the rig)**

Este confirms, and the session records each answer:
1. Dunder-MiffLan, main account in Mine #8 (Eclipse Rift), windowed, in the foreground.
2. **The client size and scale equal Ur Task's measured values, or the task stops here (controller ruling).** Every Ur Task macro point (pickaxe, dot box, Go to Top) was measured at Ur Task `measured.json`'s `client`, 800x599 at 100%, and the two measured files must describe the same pixels. Check it live, do not assume:

   ```powershell
   $sweep = Join-Path $env:LOCALAPPDATA ("626Labs\ore-stop-sweep\" + (Get-Date -Format yyyy-MM-dd))
   New-Item -ItemType Directory -Force -Path $sweep | Out-Null
   Set-Content -Path (Join-Path $env:LOCALAPPDATA "626Labs\ore-stop-sweep\current.txt") -Value $sweep
   $client = (Get-Content "..\rororo-ur-task\docs\reference\events\macros\space-mine-ore-stop\measured.json" -Raw | ConvertFrom-Json).client
   "Ur Task measured.json: $($client.w)x$($client.h) at $($client.displayScale)%"
   pwsh -NoProfile -File ..\rororo-ur-task\tools\grid-capture.ps1 -OutDir $env:TEMP
   ```

   Read `game area WxH ... scale N%` in the grid-capture output. Pass only if W x H equals `client.w` x `client.h` and N equals `client.displayScale` (800x599 at 100%). **If either differs, stop.** Este resizes the Roblox window (or sets the display scale) and the check runs again. Never sweep at another size.
3. The camera is top-down, set by hand for now: Este drags the camera to the pitch limit and counts back. Step 4 makes the `Camera top-down` macro reproduce this view.
4. Ur OCR is closed (so it does not fire while measuring). Ur Task is running (Step 4 runs its macros).

- [ ] **Step 2: Find the eight ring spots**

```powershell
$sweep = (Get-Content (Join-Path $env:LOCALAPPDATA "626Labs\ore-stop-sweep\current.txt") -Raw).Trim()
pwsh -NoProfile -File tools\ring-sweep.ps1 -OutDir $sweep -Seconds 2 -Minutes 0.1
pwsh -NoProfile -File ..\rororo-ur-task\tools\grid-capture.ps1 -Image (Join-Path $sweep "frame-0001.png") -OutDir $sweep -Tag ring -Grid 20
```

Expected: `frame 1: frame-0001.png 800x599 at 100%` (any other size or scale: back to Step 1.2).

Read the gridded picture (`ring-1-...png` in `$sweep`) as an image. Find the character and the block pitch, then the centre of each of the eight neighbouring blocks, one block out: N, NE, E, SE, S, SW, W, NW. Pick each centre inside a block's face, away from its cell lines. Write `$sweep\mine8.measured.json` from the shape in Global Constraints with: `recordedClientW` 800 and `recordedClientH` 599 (Ur Task's `client.w` and `client.h`, confirmed in Step 1), `scalePercent` 100, the eight measured `x`, `y`, `"ignore": []`, `"layers": []`, and everything else as shown (tolerance 30, minLayerSpots 3, box 5x5, rockCap 300000 "Go to Top", camera 10000 "Camera top-down", spotCooldownMs 1000, macros `Mine spot N` ... `Mine spot NW`).

- [ ] **Step 3: Check the spot placement**

```powershell
$sweep = (Get-Content (Join-Path $env:LOCALAPPDATA "626Labs\ore-stop-sweep\current.txt") -Raw).Trim()
pwsh -NoProfile -File tools\ring-sample.ps1 -Frames $sweep -Measured (Join-Path $sweep "mine8.measured.json")
```

Read `ring-0001.png`. Pass: each outer tile's yellow box sits inside one block face, not on a cell line, not on the character; the centre tile shows the character. If not, move that spot's `x`, `y` and re-run. Este confirms the sheet matches what he sees. Keep this `ring-0001.png` open: it is the reference view for Step 4.

- [ ] **Step 4: Measure the camera (Ur Task's `camera` group)**

Ur Task's `measured.json` owns the camera (`camera.start {x, y}`, `pitchTravelPx`, `dragMs`, `countBackPx`, `countBackMs`, `measuredOn`), and its values are provisional until this step. The target: after `Camera top-down` runs from a knocked camera, the ring spots land on the same block faces as in Step 3.

1. Generate the macros from the current values, install them, and restart Ur Task:

   ```powershell
   $utDir = "..\rororo-ur-task\docs\reference\events\macros\space-mine-ore-stop"
   pwsh -NoProfile -File (Join-Path $utDir "generate.ps1")
   $dest = Join-Path $env:LOCALAPPDATA "626Labs\RoRoRoUrTask\macros"
   New-Item -ItemType Directory -Force -Path $dest | Out-Null
   Copy-Item (Join-Path $utDir "macros\*.json") $dest -Force
   ```

   Generating here writes only working files in the sibling repo; Step 9 commits them. The generator warns that `ring` and `camera` are provisional; that is expected until Step 9. Este closes Ur Task and starts it again through RoRoRo, so it loads the copied files.
2. Este knocks the camera out of top-down (a short right-drag upward), then runs `Camera top-down` (from Ur Task's library, or Ur MCP `run_macro`).
3. Capture and sample the result:

   ```powershell
   $sweep = (Get-Content (Join-Path $env:LOCALAPPDATA "626Labs\ore-stop-sweep\current.txt") -Raw).Trim()
   $cam = Join-Path $sweep "camera"
   Remove-Item -Recurse -Force $cam -ErrorAction SilentlyContinue
   pwsh -NoProfile -File tools\ring-sweep.ps1 -OutDir $cam -Seconds 1 -Minutes 0.05
   pwsh -NoProfile -File tools\ring-sample.ps1 -Frames $cam -Measured (Join-Path $sweep "mine8.measured.json")
   ```

   Read `$cam\ring-0001.png` next to Step 3's `$sweep\ring-0001.png`. Pass: every yellow box sits on the same block's face as in Step 3, and the centre tile shows the character. Repeat 2 and 3 three times; all three must pass.
4. If a run fails, change the camera values in `$utMeasured` (hand edit) and go back to 1:
   - The view differs from knock to knock: the first drag did not reach the pitch limit. Raise `pitchTravelPx`. The generator refuses a drag that would leave the 599-pixel client; if it does, move `camera.start.y` up.
   - The view is the same every time but steeper (more straight down) than Step 3's: raise `countBackPx`. Flatter: lower it.
   - Alternatively, when the macro's view is steady and still shows all eight neighbouring blocks cleanly, re-measure the spots in the macro's view instead: run the macro, then repeat Steps 2 and 3. A view the macro reproduces matters more than the hand-set one.
5. When three runs pass, record it:

   ```powershell
   $utMeasured = "..\rororo-ur-task\docs\reference\events\macros\space-mine-ore-stop\measured.json"
   $u = Get-Content $utMeasured -Raw | ConvertFrom-Json
   $u.camera.measuredOn = Get-Date -Format yyyy-MM-dd
   $u | ConvertTo-Json -Depth 8 | Set-Content -Path $utMeasured -Encoding utf8
   $u.camera | ConvertTo-Json -Compress
   ```

   Paste the printed camera line into the session notes.

From here on, Este sets the camera with `Camera top-down`, not by hand.

- [ ] **Step 5: Run the sweep down all three layers**

Este turns Auto Mine on and lets the main ride down Mine #8. Start:

```powershell
$sweep = (Get-Content (Join-Path $env:LOCALAPPDATA "626Labs\ore-stop-sweep\current.txt") -Raw).Trim()
pwsh -NoProfile -File tools\ring-sweep.ps1 -OutDir $sweep -Seconds 3 -Minutes 25
```

While it runs, Este calls out each layer change (navy with violet lines, black with bright violet lines, grey) and any ore he sees next to the character, especially Sunstone, Dark Quartz and Eclipse Onyx; the session writes each call-out with the clock time. If a layer passes with no ore beside the character, Este stops Auto Mine next to one for a few frames. The sweep ends when all three layers and at least one of each named ore are in frames, or after 25 minutes.

- [ ] **Step 6: Measure the Go to Top check (Ur Task's `goToTop.check`)**

Go to Top must not be an unchecked press (controller ruling): a popup or card over the button must stop the macro, not be clicked. The check averages the same 5x5 box around the button's point (`goToTop.x`, `goToTop.y`, about 400, 50) that Ur Task's colour checks use. The sweep frames already show the button, so measure it from them:

```powershell
$sweep = (Get-Content (Join-Path $env:LOCALAPPDATA "626Labs\ore-stop-sweep\current.txt") -Raw).Trim()
$utMeasured = "..\rororo-ur-task\docs\reference\events\macros\space-mine-ore-stop\measured.json"
$write = $false                                   # set to $true to write, once the acceptance below passes
Add-Type -AssemblyName System.Drawing
$u = Get-Content $utMeasured -Raw | ConvertFrom-Json
$gx = [int]$u.goToTop.x; $gy = [int]$u.goToTop.y
$avgs = @(foreach ($f in Import-Csv (Join-Path $sweep "frames.csv")) {
    $bmp = [System.Drawing.Bitmap]::FromFile((Join-Path $sweep $f.file))
    $r = 0; $gg = 0; $b = 0
    for ($y = $gy - 2; $y -le $gy + 2; $y++) { for ($x = $gx - 2; $x -le $gx + 2; $x++) { $px = $bmp.GetPixel($x, $y); $r += $px.R; $gg += $px.G; $b += $px.B } }
    $bmp.Dispose()
    [pscustomobject]@{ frame = [int]$f.frame; r = [int][math]::Floor($r / 25); g = [int][math]::Floor($gg / 25); b = [int][math]::Floor($b / 25) }
})
$mid = [int][math]::Floor($avgs.Count / 2)
$expect = [pscustomobject]@{ r = @($avgs.r | Sort-Object)[$mid]; g = @($avgs.g | Sort-Object)[$mid]; b = @($avgs.b | Sort-Object)[$mid] }
$far = @($avgs | Where-Object { [math]::Sqrt([math]::Pow($_.r - $expect.r, 2) + [math]::Pow($_.g - $expect.g, 2) + [math]::Pow($_.b - $expect.b, 2)) -gt 20 })
"Go to Top at ({0}, {1}): median #{2:X2}{3:X2}{4:X2} over {5} frames; {6} frames more than 20 away: {7}" -f $gx, $gy, $expect.r, $expect.g, $expect.b, $avgs.Count, $far.Count, (($far | ForEach-Object { $_.frame }) -join ' ')
if ($write) {
    $u.goToTop.check = [pscustomobject]@{
        box = [pscustomobject]@{ offsetX = -2; offsetY = -2; w = 5; h = 5 }
        expect = $expect
        tolerance = 20
    }
    $u | ConvertTo-Json -Depth 8 | Set-Content -Path $utMeasured -Encoding utf8
    "wrote goToTop.check to $utMeasured"
}
```

Acceptance: the median is the button's red (Este confirms against the screen), and at most 5% of frames are more than 20 away, each of which, read as an image, shows something over the button (a card, a popup) or a mid-teleport frame. If the plain button itself reads differently in some frames (it changes colour by layer, or moves), stop and ask Este: do not write a check that would refuse a clean press. When it passes, run the block again with `$write = $true`.

- [ ] **Step 7: Sample, label and fit the ring colours**

```powershell
$sweep = (Get-Content (Join-Path $env:LOCALAPPDATA "626Labs\ore-stop-sweep\current.txt") -Raw).Trim()
pwsh -NoProfile -File tools\ring-sample.ps1 -Frames $sweep -Measured (Join-Path $sweep "mine8.measured.json")
```

Read the `ring-NNNN.png` sheets in order (batches of 10 to 20) with `frames.csv` times against Este's call-outs, and write `$sweep\labels.csv` (header `frame,spot,kind,layer`): one `N,*,rock,<layer>` row per frame plus a row for each spot that is `ore`, `empty` (sky, a mined-out hole) or `self` (the character overlapping a spot), and `skip` for frames mid-fall, mid-camera-move or with a "New Item!" card over the ring. Layer names: short and lower case (for example `navy`, `black`, `grey`), in the order they appear.

Then fit:

```powershell
$sweep = (Get-Content (Join-Path $env:LOCALAPPDATA "626Labs\ore-stop-sweep\current.txt") -Raw).Trim()
pwsh -NoProfile -File tools\ring-fit.ps1 -Samples (Join-Path $sweep "samples.csv") -Labels (Join-Path $sweep "labels.csv") -Measured (Join-Path $sweep "mine8.measured.json")
```

Acceptance, in priority order:
1. **0 MISSED ORE.** The spec stops for every ore; a missed ore is a hard fail. If Eclipse Onyx is missed against the black-and-violet rock, lower `-Tolerance` (try 25, 20, 15) and re-run; if lowering creates many false stops, check whether the misses are mislabelled frames first.
2. **0 WRONG LAYER**, or only frames at a layer boundary. If whole stretches read the wrong layer, the layers' rock sets overlap: lower tolerance, or raise `minLayerSpots` in the file if "no layer" frames are the problem in reverse.
3. **FALSE STOP** under about 2% of rock samples. Each costs one short mine, not a stall.

When it passes, write it: add `-Write` (and the chosen `-Tolerance`). Paste the final summary line and the per-layer colour lines into the session notes.

- [ ] **Step 8: Commit the measured file**

```powershell
$sweep = (Get-Content (Join-Path $env:LOCALAPPDATA "626Labs\ore-stop-sweep\current.txt") -Raw).Trim()
New-Item -ItemType Directory -Force -Path docs\reference\ore-stop | Out-Null
Copy-Item (Join-Path $sweep "mine8.measured.json") docs\reference\ore-stop\mine8.measured.json
git add docs/reference/ore-stop/mine8.measured.json
git commit -m "docs(ore-stop): measured Mine #8 ring" -m "<the ring-fit summary line, the per-layer colour lines, the tolerance, and the sweep length and frame count>" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

Replace the angle-bracket body with the actual numbers from Step 7. Step 9 carries the spots and box into Ur Task.

- [ ] **Step 9: Keep the two measured files in step, then install the macros (controller ruling)**

Copy the ring into Ur Task's `measured.json`, regenerate, test and commit there, then install. This comes before the import because the importer resolves the `Mine spot` macro names against the macros Ur Task has loaded.

```powershell
$utDir = "..\rororo-ur-task\docs\reference\events\macros\space-mine-ore-stop"
$utMeasured = Join-Path $utDir "measured.json"
$u = Get-Content $utMeasured -Raw | ConvertFrom-Json
$ring = Get-Content docs\reference\ore-stop\mine8.measured.json -Raw | ConvertFrom-Json
if ($ring.recordedClientW -ne $u.client.w -or $ring.recordedClientH -ne $u.client.h -or $ring.scalePercent -ne $u.client.displayScale) {
    throw "Client mismatch: Ur OCR $($ring.recordedClientW)x$($ring.recordedClientH) at $($ring.scalePercent)%, Ur Task $($u.client.w)x$($u.client.h) at $($u.client.displayScale)%."
}
if ($null -eq $u.camera.measuredOn) { throw "camera is not measured yet: finish Step 4." }
if ($null -eq $u.goToTop.check) { throw "goToTop.check is not measured yet: finish Step 6." }
$u.ring.spots = @($ring.spots | Sort-Object order | ForEach-Object { [pscustomobject]@{ name = $_.name; x = [int]$_.x; y = [int]$_.y } })
$u.ring.box = $ring.box
$u.ring.measuredOn = Get-Date -Format yyyy-MM-dd
$u | ConvertTo-Json -Depth 8 | Set-Content -Path $utMeasured -Encoding utf8
pwsh -NoProfile -File (Join-Path $utDir "generate.ps1")
dotnet test ..\rororo-ur-task\tests\rororo-ur-task.Tests\rororo-ur-task.Tests.csproj -p:StandaloneTestsOnly=true --filter "FullyQualifiedName~OreStopExampleMacrosTests"
```

Expected: no `provisional` warning, 12 `wrote ...json  <name>` lines, and the `OreStopExampleMacrosTests` all pass (they check the Go to Top press carries its check). Then commit in the sibling repo, on its `feat/ore-stop` branch (its pre-commit guards run; do not skip them):

```powershell
git -C ..\rororo-ur-task branch --show-current
git -C ..\rororo-ur-task add docs/reference/events/macros/space-mine-ore-stop/measured.json docs/reference/events/macros/space-mine-ore-stop/macros
git -C ..\rororo-ur-task commit -m "docs(ore-stop): measured ring, camera and Go to Top check from the Ur OCR sweep" -m "Ring spots and box copied from Ur-OCR docs/reference/ore-stop/mine8.measured.json; the camera and the Go to Top colour check measured live on Dunder-MiffLan at 800x599, 100%. Macros regenerated." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

Install the regenerated macros and restart Ur Task:

```powershell
$utDir = "..\rororo-ur-task\docs\reference\events\macros\space-mine-ore-stop"
$dest = Join-Path $env:LOCALAPPDATA "626Labs\RoRoRoUrTask\macros"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item (Join-Path $utDir "macros\*.json") $dest -Force
```

Este closes Ur Task and starts it again through RoRoRo. Then check that each of the twelve names is loaded exactly once:

```powershell
$dest = Join-Path $env:LOCALAPPDATA "626Labs\RoRoRoUrTask\macros"
$names = Get-ChildItem $dest -Filter *.json | ForEach-Object { (Get-Content $_.FullName -Raw | ConvertFrom-Json).name }
$want = @('Auto Mine off (checked)', 'Auto Mine on (checked)', 'Camera top-down', 'Go to Top') + @('N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW' | ForEach-Object { "Mine spot $_" })
foreach ($w in $want) { "{0,-26} {1}" -f $w, @($names | Where-Object { $_ -eq $w }).Count }
```

Expected: every line ends in `1`. A `0` means the copy failed; a `2` means an older macro with the same name is in the library, which the importer refuses. Este deletes the extra from Ur Task's library before going on.

- [ ] **Step 10: Import into Ur OCR**

```powershell
Get-Process RoRoRo.UrOcr -ErrorAction SilentlyContinue      # must print nothing; else close Ur OCR from its tray
dotnet build rororo-ur-ocr.csproj
$exe = (Resolve-Path "bin\Debug\net10.0-windows10.0.19041.0\RoRoRo.UrOcr.exe").Path
$measured = (Resolve-Path "docs\reference\ore-stop\mine8.measured.json").Path
$p = Start-Process -FilePath $exe -ArgumentList "--import-ring", "`"$measured`"" -Wait -PassThru
$p.ExitCode
Get-Content "$env:LOCALAPPDATA\626Labs\rororo-ur-ocr\ring-import.log"
```

Expected: `0`, then `imported ring mine8 (Mine #8): 3 layers, 10 triggers ...` and ten `-> macro <id>` lines. Then install the 0.5.0 build on Dunder-MiffLan the way Este installs dev builds (`pwsh ./build/build-plugin.ps1`, then RoRoRo Plugins, Install, pointed at `artifacts`, or his usual path) and start Ur OCR through RoRoRo. Pass: `ur-ocr.log` has `=== RoRoRo Ur OCR v0.5.0 starting` and the trigger list shows the ten `Mine #8: ...` triggers.

- [ ] **Step 11: Live run**

Este puts the main in Mine #8, runs `Camera top-down`, turns Auto Mine on, and keeps the main in the foreground. Watch for 20 to 30 minutes, reading the logs with:

```powershell
Select-String -Path "$env:LOCALAPPDATA\626Labs\rororo-ur-ocr\logs\ur-ocr.log" -Pattern "ring Mine #8|trigger ""Mine #8" | Select-Object -Last 40
Get-Content "$env:LOCALAPPDATA\626Labs\RoRoRoUrTask\logs\ur-task.log" -Tail 40
```

Pass criteria, each with its evidence line:
1. **Layer reading:** `ring Mine #8: layer <name> (n of 8 spots)` appears for each of the three layers as the main descends.
2. **Ore stop, every layer:** on each layer at least one `trigger "Mine #8: <spot>": macro <id> (<layer>: ... nearest ... d=...)`, followed in `ur-task.log` by that Mine spot macro holding and ending, and Auto Mine back on (Este confirms on screen). At least one Eclipse Onyx stop is seen, if one appears next to the character.
3. **Busy re-arm:** at least one `Ur Task busy, retry in 1000ms` for a spot, followed by a fire of the same spot after the first macro ended.
4. **No stall:** at no point is the account stuck with Auto Mine off for more than one ore's hold (Este watches; `GetPlayback` via Ur MCP shows no playback running longer than the ore takes).
5. **Camera rule:** Este nudges the camera (right-drag) away from top-down. Within about 10 s plus the cooldown, `trigger "Mine #8: camera top-down": macro <id>` fires and the next `ring Mine #8: layer ...` line returns.
6. **Tabbing away:** Este focuses another window for 20 s. The log shows `ring Mine #8: ring not visible` and no camera or rock-cap fire; on return the layer line comes back.

- [ ] **Step 12: Rock cap, shortened**

Five minutes is too long to watch reliably, so prove the rule at 60 s, then restore:

```powershell
$sweep = (Get-Content (Join-Path $env:LOCALAPPDATA "626Labs\ore-stop-sweep\current.txt") -Raw).Trim()
$m = Get-Content docs\reference\ore-stop\mine8.measured.json -Raw | ConvertFrom-Json
$m.rockCap.holdForMs = 60000
$short = Join-Path $sweep "mine8.rockcap-60s.json"
$m | ConvertTo-Json -Depth 8 | Set-Content -Path $short -Encoding utf8
$short
```

Close Ur OCR, import the printed `$short` path as in Step 10 (in place of `$measured`), start Ur OCR. Pass: after 60 s on one layer, `trigger "Mine #8: rock cap": macro <id> (Went to top: 1 minutes on the <layer> layer)` is in `ur-ocr.log`, the Go to Top check passes and Ur Task presses Go to Top and turns Auto Mine on, `ur-task.log` shows the Go to Top playback ending (`bridge playback <id> 'Go to Top': finished`), and the ring reads the top layer. Then close Ur OCR and re-import the committed file (the Step 10 command) so the cap is back to 300000; confirm with `Select-String "$env:LOCALAPPDATA\626Labs\rororo-ur-ocr\triggers.json" -Pattern '"holdForMs": 300000'` (one hit) and restart Ur OCR.

- [ ] **Step 13: Record the run**

If any value changed during Steps 11 and 12 (tolerance, a spot moved, minLayerSpots), update `docs/reference/ore-stop/mine8.measured.json` to the values in use and commit it; if a spot moved, repeat Step 9 so Ur Task's copy follows. Either way, make one commit that records the live run:

```powershell
git add docs/reference/ore-stop/mine8.measured.json
git commit --allow-empty -m "docs(ore-stop): 0.5.0 live run on Dunder-MiffLan" -m "<pass or fail for each of the six criteria in Step 11 and the rock cap in Step 12, with the evidence line for each, and any value changed>" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

Replace the angle-bracket body with the observed results. Any failed criterion becomes a follow-up for Este to triage; do not paper over it in the message.

---

## Self-review

**1. Spec coverage.**
- "None of" colour match (Ur OCR changes 1): Task 1 (`NoneOf`, `Judge`, validation), used by ring spots in Task 5.
- Layer sets, current layer by most spots (2, and "The ring"): Task 2 (storage), Task 3 (vote), Task 5 (judging spots against the layer, exposing `Rings`, logging).
- Dwell `HoldForMs` and the rock cap, 5 minutes default, configurable per loop (3, Decision 6): Task 2 (field), Task 6 (engine, `SameLayer`), Task 8 (`rockCap.holdForMs` per measured file, default 300000), Task 9 Step 12 (live). A held trigger never loses its fire to the cooldown (Task 6 `A_hold_that_completes_inside_the_cooldown_fires_when_the_cooldown_ends`).
- The rock cap's reason line (Decision 7, amended by controller ruling: Ur OCR logs the reason, Ur Task logs the Go to Top playback ending): Task 6 (`Went to top: <N> minutes on the <layer> layer` to `ur-ocr.log`, asserted in `Same_layer_for_the_hold_runs_the_rock_cap`), Task 9 Step 12 (live, both logs).
- Re-arm when Ur Task is busy (4): Task 4, kept by the Task 5 and 6 rewrites; live in Task 9 Step 11.3.
- Priority in ring order (5): Task 5 (`InFiringOrder`, one claim per ring per tick, busy wait keeps the turn).
- Camera rule, 10 s with no layer (Failure and edge cases): Task 6 (`NoLayer` + hold), Task 8 (`camera.holdForMs` 10000), Task 9 Step 11.5. The `Camera top-down` macro's own values are measured live in Task 9 Step 4 and written into Ur Task's `measured.json`, which owns them.
- Screen geometry (Decision 8) and the two measured files: Task 9 Step 1.2 stops unless the client is Ur Task's 800x599 at 100%; Step 9 copies the ring into Ur Task's file, refuses a client mismatch, regenerates, tests, commits there and installs the macros before the Step 10 import.
- Go to Top is a checked press (controller ruling): Task 9 Step 6 measures `goToTop.check` from the sweep frames; Step 9 refuses to go on without it.
- Foreground only: unchanged gate in Tasks 5 and 6; `Unknown` never counts as no layer.
- RunMacro by name, contract unchanged: importer resolves names to ids (Task 8); no bridge change.
- Ring geometry, 5x5 boxes, 100% recording, client-space scaling: Task 8 (importer, validation), Task 9 (sweep).
- Testing section: NoneOf (Task 1), layer selection (Tasks 3, 5), HoldForMs (Task 6), busy re-arm (Task 4), capture sweep and live run (Task 9).
- Out of scope respected: no ore valuation, no notifications, no round robin, no host change, Ur Task side left to its own plan.

**2. Placeholder scan.** Every code step carries complete code. The two angle-bracket commit bodies in Task 9 (Steps 8 and 13) are live measurements that do not exist until the run; each says exactly what to put there. Spot coordinates in Task 9 Step 2 are measured on the rig by design (the spec forbids guessing them).

**3. Type consistency.** Checked across tasks: `ColorMatcher.Judge(Rgb, ColorCriteria, IReadOnlyList<Rgb>?)`, `ColorMatcher.Distance`, `ColorMatchResult.Nearest`, `ColorCriteria.Validate(bool)`, `ColorCriteria.InRange`, `ColorCriteria.MaxTolerance`; `RingDefinition(Id, Name, Layers, MinLayerSpots)`, `LayerDefinition(Name, Rock)`, `RingSpot(RingId, Order)`, `LayerCriteria(RingId, Condition)`, `LayerCondition.SameLayer/NoLayer`, `TriggerMode.Layer`, `Trigger.Ring/Layer/HoldForMs`; `TriggerStore.Rings/Upsert/UpsertRing`; `TriggerValidation.Validate(Trigger, IReadOnlyList<RingDefinition>)`, `Validate(RingDefinition)`, `MaxHoldForMs`; `RingTracker.Get/Update/MarkUnknown/Vote/Pick`, `RingState(Status, Layer, Since, Votes, Spots).Describe()`, `RingStatus`, `SpotSample(Order, Sampled, ToleranceRgb)`; `TriggerCoordinator(..., macroClient, diag)`, `.Rings`, `.BusyReason`; `ActivityKind.Busy/Deferred/LayerChanged/Holding` (added in Tasks 4, 5, 5, 6, appended in that order); `MeasuredRing`/`MeasuredSpot`/`MeasuredLayer`/`MeasuredAction`, `RingImporter.Build/Apply/ResolveMacro/StableId`, `RingImportCommand.Run` (both overloads) and `Flag`. The measured JSON keys in Global Constraints, the Task 7 demo file, the Task 8 literal test and `MeasuredRing`'s camelCase properties match.

**4. Review Focus.** Five lines, each pinned by a named test in its owning task: tabbing away (Task 6 `Tabbing_away_is_not_no_layer`), no layer (Task 5 `No_layer_means_no_ore`), non-busy refusals (Task 4 `Other_refusals_spend_the_edge`), broken ring logged once (Task 5 `A_spot_on_a_missing_ring_logs_one_error`), import refusals (Task 8 `Refuses_while_Ur_OCR_is_running`, `Missing_macros_exit_2_and_write_nothing`). The layer-boundary behaviour is not a bug to guard but a deliberate cost; it is pinned by `A_neighbouring_layers_rock_reads_as_ore` (Task 5) and named under Decisions.

**Test counts** (baseline 120): Task 1 +18 = 138; Task 2 +21 = 159; Task 3 +10 = 169; Task 4 +4 = 173; Task 5 +12 = 185 (11 ring coordinator tests, 1 preview test); Task 6 +10 = 195; Task 8 +23 = 218.

**5. Preflight rulings applied (2026-09-27).** Client size and scale gate before the sweep (Task 9 Step 1.2, Global Constraints); the Ur Task sync moved after the ring commit, with the macro install and Ur Task restart before the import (Task 9 Step 9); the rock cap's reason line (Task 6); held triggers stay armed through the cooldown (Task 6); no live preview for ring spots, named in the README (Task 5 Step 5a, Task 8 Step 9); every path relative to the repo root; Task 1 Step 1 marked done; Task 8 Step 8's claim corrected. From the Ur Task preflight: the camera is measured live and written into Ur Task's `measured.json` (Task 9 Step 4), and Go to Top gets a measured colour check (Task 9 Step 6). If a count differs by the number of theory rows, trust the pass/fail, not the arithmetic.
