# Display scale and window-anchored regions

**Found:** 2026-09-21, in the rororo-ur-task repo, while converting a clan AutoHotkey macro to run
on any screen. **Status:** findings and proposals. Nothing in Ur OCR has been changed.

The full write-up, with every measurement and the Ur Task analysis, lives in the rororo-ur-task
repo at `docs/display-scale-findings.md`. This note carries the part that applies here.

## The facts that matter to Ur OCR

1. **Roblox lays out its UI in logical units.** The viewport is the physical client size divided
   by the Windows display scale, and offset-sized UI is enlarged by that scale. Roblox staff stated
   this on the developer forum in March 2026 (thread 4473249).
2. **Roblox has a minimum window size, and it scales.** The minimum client area is 800x599 at 100%
   and 1002x750 at 125%, both measured. A resize below it fails silently: `SetWindowPos` returns
   success and the window lands at the minimum.
3. **Even scaling is not the whole story.** At 125%, with everything multiplied by 1.25, one Pet
   Sim 99 HUD button still sat away from its predicted spot. A game can place its own buttons by
   its own logic.

## What that means here

A client-space trigger stores its region plus the recorded client size, and
`WindowSpaceMath.ToScreenRegion` scales the region by current client size over recorded client
size.

- That ratio is right for UI that grows with the window. It is wrong for UI sized in fixed logical
  units, which includes Roblox's own dialogs and many HUD buttons.
- Display scale is a second input the ratio cannot see. Two PCs with the same physical client size
  and different scales give a ratio of 1, while fixed-unit UI is 1.25x larger and corner-anchored
  elements have moved.
- Exposure is smaller than Ur Task's. A region is usually larger than a click target, and OCR
  tolerates padding. Small colour triggers are the risky ones.
- `DpiGuard` is per machine. It catches a display change on one PC. It does not travel with a
  trigger, so it cannot explain a trigger authored on another PC.
- The standing warning holds: do not add PerMonitorV2 without fixing the region picker first. The
  AutoHotkey fix for mixed-scale monitors was a thread-level awareness switch, which is not a
  drop-in for a WPF process.

## Proposals

1. Record the display scale per client-space trigger, nullable, so old triggers still load.
2. When it differs at run time, keep the proportional scaling and surface the mismatch on the
   trigger row, so a miss is explainable.
3. Use the measured minimum sizes above for any future arranging or resizing work.

## An enhancement worth taking: expose and drag

The converted AutoHotkey script got a button that exposes every click point as a named tag over the
real window, with drag to re-aim and the original always shown and restorable. Este used it on the
Surface and wants the pattern in the products. Ur OCR's version: expose every trigger's region as a
labelled box over the alt window, drag to move or resize, and show the live read, colour or text,
while dragging. Keep two rules from the original: a marker must never cover the pixels it is about
to read, and an adjustment is stored apart from the shared trigger, per display scale. The Ur Task
backlog entry "Expose and drag a macro's click points" has the full list.

## Testing

The Surface at 125% is the rig for anything other than 100%. Both monitors on the main PC are at
100%. A stand-in window is only a faithful test if it enforces Roblox's minimum size, and test
harnesses on the main PC should run hidden, because live Roblox sessions and screen-recording
plugins run there.
