# Changelog

## 0.6.0 — unreleased

### Added

- **Ore stop pulse.** Riding and watching at once did not work in Mine #8 (effects cover the ring, and plain rock spans navy to hot magenta), so each account now pulses: Auto Mine rides for a short burst, stops, waits a second for the effects to settle, and reads the layer on that calm frame. Above your target layer it rides on. At the target it runs Ur Task's "Clear spot" macro for each of the eight ring spots, ore first (the spot furthest from the layer's rock), then the rest; Ur Task skips any spot without the white outline. A pass that clears nothing rides another burst. Past the target it presses Go to Top, and 5 minutes on the target layer without clearing a block also goes to the top, logged as `Went to top: 5 minutes on the <layer> layer` (time with the account behind or paused does not count).
- **Settings per account:** target layer (1 to 3, counting rock types down) and whether to clear on it (`top`) or on the layer above it (`oneAbove`, for an under-powered account); ride burst (2 s); pause before reading (1 s); rock cap (5 minutes).
- `RoRoRo.UrOcr.exe --import-pulse <pulse.json>` writes the pulse settings into triggers.json without opening a window, looking up Ur Task's macros by name. Import the ring first (`--import-ring`), and close Ur OCR first. The result is in `pulse-import.log`.
- Ur OCR asks Ur Task how each macro ended (`GetPlayback`), so the pulse waits for a macro to finish before its next move.
- Pulse decisions go to the activity panel and `ur-ocr.log`, each line starting `pulse <account id>:`.
- If Ur Task loses track of a playback (it restarted, or the playback ran past 10 minutes), that playback is lost: rather than guess whether it pressed anything, the pulse stops instead of re-running it blind. In dry run, a pulse-owned ring shows nothing — its 0.5.0 triggers stand down and the pulse itself is held, same as pause-all.

### Changed

- **One owner per account and ring.** For an account with a pulse, the pulse replaces that ring's 0.5.0 triggers (the eight spots, the rock cap and the camera rule), which stand down while that account is in front. Accounts without a pulse keep the 0.5.0 ring triggers as they were.

### Notes

- The pulse acts only while its own account is the foreground alt, and never brings a window to the front. Pause all (F9) and dry run pause the pulse too.
- If a macro stops at a colour check (a popup or captcha over the Auto Mine dot, or a Clear spot that cannot see the window), if you press Esc during one of its macros, or if Ur Task closes, that account's pulse stops and says why. Restart Ur OCR to start it again.
- Needs Ur Task 0.11.0 or later (the "Clear spot" macros, the outline check and the `skipped` reason). An older Ur Task makes the pulse stop with `Unknown method 'GetPlayback'` or `No Ur Task macro is named "Clear spot N"`.
- Downgrading to 0.5.0 keeps your triggers, but 0.5.0 drops the pulse settings the next time it saves.

## 0.5.0 — 2026-09-27

### Fixed

- **Colour triggers check the pixel you picked.** The picker took the colour at the pixel you clicked, but the trigger checked the region's centre, so any pick off-centre compared the wrong spot. New colour triggers store the pick point and average the same 5x5 box at pick time and at check time, scaled with window-anchored regions. Triggers made before this keep their old behaviour.

### Added

- **Two-state colour rule.** A colour trigger can carry the colour of the opposite state (the grey of a locked tile, the red of "Off"); it matches only when it is within tolerance of the target and closer to the target than to that other colour. Stored and evaluated now; the editor has no control for it yet.
- The activity log names the colour it saw and its distance on every colour check, e.g. `green #8BE03A d=4.1`, in the same words Ur Task uses.
- **Ore stop: ring triggers.** Eight colour triggers can form a ring around your character and share one reading of which mine layer you are on: the layer whose rock the most spots match, at least `minLayerSpots` (set per measured file, 3 by default). A spot fires its macro when its colour is none of that layer's rock and none of its ignore colours, so every ore gets stopped for. With no layer, nothing fires. Built for PS99's Mining League.
- **None-of colour checks.** A colour trigger can list colours and match when the sample is near none of them (`noneOf`), in place of one target colour.
- **Hold before firing.** `holdForMs`: the match must hold that long, unbroken, before the trigger fires, and it fires again only after another full hold. The rock cap (same layer for 5 minutes, runs Go to Top) and the camera rule (no layer for 10 seconds, runs Camera top-down) are layer triggers built on it. Tabbing away from Roblox never counts as "no layer". When the rock cap fires, `ur-ocr.log` says why, e.g. `Went to top: 5 minutes on the grey layer`.
- **Busy retry.** When Ur Task refuses a macro because a sequence is already running, the trigger stays armed and tries again after its cooldown while it still matches. A busy refusal no longer counts as a fire.
- **Ring order.** When several ring spots match at once, the first in ring order (N, NE, E, SE, S, SW, W, NW) runs its macro; the rest wait their turn.
- `RoRoRo.UrOcr.exe --import-ring <measured.json>` writes a measured ring into triggers.json without opening a window. Close Ur OCR first. The capture and fit tools that help build the measured file live in `tools/`; the file's spots, client size and macros are still measured and written by hand.
- Ring layer changes, ring fires and busy retries are written to `ur-ocr.log`.
- **Downgrading after a ring import is unsupported.** Once a layer trigger is saved, 0.4.0 cannot parse `"mode": "layer"`, and it backs up `triggers.json` as corrupted on next launch — taking every existing trigger with it. Don't downgrade to 0.4.0 after importing a ring.

## 0.4.0 — 2026-07-03

### Added

- **Window-anchored trigger regions.** A trigger's watch region can now anchor to the alt window's client area instead of a fixed screen spot. It follows whichever alt is in the foreground (watching the same relative UI on each alt) and scales with the window's size, so moving or resizing the Roblox window no longer breaks detection — no re-pick needed. New triggers picked over an alt default to window-anchored; regions picked over a non-alt window stay screen-absolute, as do all pre-0.4 triggers (they migrate to schema v2 as `screen`, non-breaking). The Ur-OCR counterpart to Ur Task v0.4.0's window awareness.
- **Delete triggers.** Each row in the trigger list has a ✕ to remove that trigger.

### Fixed

- **Edge picks now anchor.** Pick-time anchoring used a center-only test, so a region drawn near a window's edge fell back to screen-absolute. It now anchors to the alt window the region overlaps most, so any region drawn over or straddling a game window becomes window-anchored.
- **The live match meter follows the alt you last focused.** It previewed against the first running alt regardless of focus, so window-anchored triggers looked like they "didn't adapt" when you focused a different alt — even though the running trigger always re-anchored correctly. The preview now matches what the trigger does.
- Themed the color picker, keybind-confirm, and Settings dialogs (were default white — `Window` subclasses don't inherit the app theme).
- Macro picker refreshes on open (no restart needed to see a newly-recorded macro); themed the dropdown, column headers, and toasts; clarified the cooldown field.
