# RoRoRo Ur OCR

> A RoRoRo plugin that watches user-defined screen regions for OCR text or color triggers and fires keybinds when they match. Built for clan members who want a banner to appear and the right key to press itself.

A 626 Labs product · *Imagine Something Else*.

## Install

1. Go to [Releases](https://github.com/estevanhernandez-stack-ed/rororo-ur-ocr/releases) and copy the directory URL of the latest release (the parent path containing `manifest.json`, `manifest.sha256`, and `plugin.zip`).
2. In RoRoRo: **Plugins → Install** → paste the URL.
3. The consent sheet will show five capabilities — see [Capabilities](#capabilities). Tick the boxes and Install.

## What it does

- Pick a region of your screen.
- Define what to look for in that region — **a piece of text** (via Windows OCR) or **a color**.
- When the trigger matches, Ur OCR fires the keybind you assigned.
- Per-trigger account-aware safety — fires only when a RoRoRo-managed Roblox window is foreground (so a misconfigured trigger doesn't spam keys into your browser).
- Edge-triggered with cooldown — fires once on the no-match → match transition, then waits.

## Window-anchored regions

Trigger regions can anchor to an alt's window instead of a fixed screen spot. When a region is window-anchored, it follows whichever alt is in the foreground and scales with the window's size — moving or resizing the Roblox window no longer breaks detection. New triggers picked over an alt default to window-anchored; regions picked over a non-alt window stay screen-absolute, as do all pre-0.4 triggers (backward compatible, no re-pick needed).

## Ore stop (ring triggers)

Built for PS99's Mining League. Eight colour triggers form a ring around your character in a top-down camera view and share one reading of which mine layer you are on. A spot runs its "Mine spot" macro in Ur Task when its colour is none of that layer's rock, so every ore gets stopped for. The floor for how many spots must agree on a layer is `minLayerSpots`, set per measured file (3 by default). Two layer triggers ride along: after 5 minutes on one layer the account goes back to the top, and when no layer reads for 10 seconds the camera is set top-down again.

The ring is measured, not drawn entirely by hand. `tools/ring-sweep.ps1` captures frames and `tools/ring-fit.ps1 -Write` samples them to fill in `layers`, `ignore` and `toleranceRgb` in a measured-values file (`docs/reference/ore-stop/`); the spots, client size and macros in that file are measured and written by hand. Ur OCR then imports the finished file while closed:

    RoRoRo.UrOcr.exe --import-ring mine8.measured.json

The result is in `ring-import.log` next to `triggers.json`. Only the foreground account is watched, as with every account-aware trigger.

## Capabilities

| Capability | What it means |
|---|---|
| `system.read-screen` | Captures pixels from your screen to detect triggers. |
| `system.synthesize-keyboard-input` | Fires the keybind you assign when a trigger matches. |
| `host.events.account-launched` | Tracks which Roblox windows belong to your RoRoRo accounts. |
| `host.events.account-exited` | Same — knows when an account window closes. |
| `host.ui.tray-menu` | Forward-compat; current UI lives in this plugin's own window/tray. |

## Known limitations

- **Exclusive fullscreen capture is unreliable** — windowed/borderless Roblox is fine. Switch your client out of exclusive fullscreen if triggers stop matching.
- **OCR needs a language pack** — install one in Settings → Time & language → Language → OCR if Text triggers don't fire.
- **Ring spots show no live preview.** A ring spot is judged against the mine layer the whole ring reads, which the trigger editor does not know, so its match meter stays blank. Watch the activity log or `ur-ocr.log` instead.
- **Elevated foreground windows block fires** — if Task Manager (admin) or a UAC prompt has focus, account-aware triggers refuse to fire (we can't synthesize keys into elevated windows from a non-elevated process). Activity log will say `blocked: elevated`.

## Troubleshooting

- **"Test now" button** in the edit panel runs one match against the current trigger and shows what OCR read or what color was sampled. Use this first when a trigger isn't matching.
- **Activity log** at the bottom of the main window shows the last 100 trigger events: fires, misses, skips. Tells you whether a trigger fired and you missed it.
- **Pause all** is bound to **F9** by default — global hotkey, works even when the plugin window isn't focused.

## Build from source

```powershell
git clone https://github.com/estevanhernandez-stack-ed/rororo-ur-ocr.git
cd rororo-ur-ocr
dotnet build
dotnet test
pwsh ./build/build-plugin.ps1
```

## License

Apache License 2.0, © 2026 626Labs LLC. See [LICENSE](LICENSE) and [NOTICE](NOTICE). The contract bindings (`ROROROblox.PluginContract`) come from the parent RoRoRo repository under its own license (MIT).
