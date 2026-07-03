# Changelog

## 0.4.0 — 2026-07-03

### Added

- **Window-anchored trigger regions.** A trigger's watch region can now anchor to the alt window's client area instead of a fixed screen spot. It follows whichever alt is in the foreground (watching the same relative UI on each alt) and scales with the window's size, so moving or resizing the Roblox window no longer breaks detection — no re-pick needed. New triggers picked over an alt default to window-anchored; regions picked over a non-alt window stay screen-absolute, as do all pre-0.4 triggers (they migrate to schema v2 as `screen`, non-breaking). The Ur-OCR counterpart to Ur Task v0.4.0's window awareness.
- **Delete triggers.** Each row in the trigger list has a ✕ to remove that trigger.

### Fixed

- **Edge picks now anchor.** Pick-time anchoring used a center-only test, so a region drawn near a window's edge fell back to screen-absolute. It now anchors to the alt window the region overlaps most, so any region drawn over or straddling a game window becomes window-anchored.
- **The live match meter follows the alt you last focused.** It previewed against the first running alt regardless of focus, so window-anchored triggers looked like they "didn't adapt" when you focused a different alt — even though the running trigger always re-anchored correctly. The preview now matches what the trigger does.
- Themed the color picker, keybind-confirm, and Settings dialogs (were default white — `Window` subclasses don't inherit the app theme).
- Macro picker refreshes on open (no restart needed to see a newly-recorded macro); themed the dropdown, column headers, and toasts; clarified the cooldown field.
