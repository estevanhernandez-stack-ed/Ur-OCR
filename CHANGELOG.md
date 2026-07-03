# Changelog

## 0.4.0 — 2026-07-02

### Added

- **Window-anchored trigger regions.** A trigger's watch region can now anchor to the alt window's client area instead of a fixed screen spot. It follows whichever alt is in the foreground (watching the same relative UI on each alt) and scales with the window's size, so moving or resizing the Roblox window no longer breaks detection — no re-pick needed. New triggers picked over an alt default to window-anchored; regions picked over a non-alt window stay screen-absolute, as do all pre-0.4 triggers (they migrate to schema v2 as `screen`, non-breaking). The Ur-OCR counterpart to Ur Task v0.4.0's window awareness.

### Fixed

- Macro picker refreshes on open (no restart needed to see a newly-recorded macro); themed the dropdown, column headers, and toasts; clarified the cooldown field.
