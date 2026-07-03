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
