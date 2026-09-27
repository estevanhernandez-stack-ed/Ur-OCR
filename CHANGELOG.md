# Changelog

## Unreleased

### Fixed

- **Colour triggers check the pixel you picked.** The picker took the colour at the pixel you clicked, but the trigger checked the region's centre, so any pick off-centre compared the wrong spot. New colour triggers store the pick point and average the same 5x5 box at pick time and at check time, scaled with window-anchored regions. Triggers made before this keep their old behaviour.

### Added

- **Two-state colour rule.** A colour trigger can carry the colour of the opposite state (the grey of a locked tile, the red of "Off"); it matches only when it is within tolerance of the target and closer to the target than to that other colour. Stored and evaluated now; the editor has no control for it yet.
- The activity log names the colour it saw and its distance on every colour check, e.g. `green #8BE03A d=4.1`, in the same words Ur Task uses.

## 0.4.0 — 2026-07-03

### Added

- **Window-anchored trigger regions.** A trigger's watch region can now anchor to the alt window's client area instead of a fixed screen spot. It follows whichever alt is in the foreground (watching the same relative UI on each alt) and scales with the window's size, so moving or resizing the Roblox window no longer breaks detection — no re-pick needed. New triggers picked over an alt default to window-anchored; regions picked over a non-alt window stay screen-absolute, as do all pre-0.4 triggers (they migrate to schema v2 as `screen`, non-breaking). The Ur-OCR counterpart to Ur Task v0.4.0's window awareness.
- **Delete triggers.** Each row in the trigger list has a ✕ to remove that trigger.

### Fixed

- **Edge picks now anchor.** Pick-time anchoring used a center-only test, so a region drawn near a window's edge fell back to screen-absolute. It now anchors to the alt window the region overlaps most, so any region drawn over or straddling a game window becomes window-anchored.
- **The live match meter follows the alt you last focused.** It previewed against the first running alt regardless of focus, so window-anchored triggers looked like they "didn't adapt" when you focused a different alt — even though the running trigger always re-anchored correctly. The preview now matches what the trigger does.
- Themed the color picker, keybind-confirm, and Settings dialogs (were default white — `Window` subclasses don't inherit the app theme).
- Macro picker refreshes on open (no restart needed to see a newly-recorded macro); themed the dropdown, column headers, and toasts; clarified the cooldown field.
