# Ur OCR smoke checklist (v0.1.0)

Run on a fresh Windows 11 box with RoRoRo (patched, PR #20 merged) installed.

- [ ] Build via `pwsh build/build-plugin.ps1`. Verify `manifest.json`, `manifest.sha256`, `plugin.zip` in `artifacts/`.
- [ ] Sideload plugin against local RoRoRo. Consent sheet shows all five capabilities cleanly (no "Unknown capability" line).
- [ ] Create one text trigger against a local HTML page with a colored banner served `file://`. Verify edge-trigger + cooldown by toggling visibility.
- [ ] Create one color trigger against the same banner. Verify both single-pixel and region-average modes.
- [ ] Test "Pause all" hotkey (F9).
- [ ] Test "Test now" button on both modes; verify result strip populates.
- [ ] Test elevation handling: focus an elevated process (admin Task Manager), confirm account-aware trigger refuses to fire + row shows the warning.
- [ ] Test DPI banner: save a trigger, change display scale, restart plugin, confirm banner.
- [ ] Inject corrupted `triggers.json` (`%LOCALAPPDATA%\626Labs\rororo-ur-ocr\triggers.json`), restart plugin, confirm recovery + backup file + banner.
- [ ] Smoke against a real Roblox session — one PetSim-style event banner detected, key fires in-game, does not fire into other windows when account-aware is on.
