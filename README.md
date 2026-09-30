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

## Ore stop pulse

Watching the ring while Auto Mine rides misses ore: effects and popups sit over the spots, and plain rock comes in colours that look like ore. The pulse stops to look instead. Per account, it rides Auto Mine for a short burst, turns it off, waits a second, and reads the layer from the ring on the still frame. At your target layer it asks Ur Task to clear each ring spot, ore first. Ur Task only presses on a block the game outlines in white (one the pickaxe can reach and break), and skips the rest. The game hides the outline while the button is down, so Ur Task presses in one-second beats and looks again between them, until the outline is gone. There's no time limit on a block. When nothing is left to clear, it rides another burst.

**Before you start, turn on Hide My Pets** (in the game's Settings). Pets sit over your character and the ring spots, and Ur OCR reads them as blocks.

Set it up per account in a pulse file (one entry per account; your Roblox user id is on your profile URL):

    {
      "schema": 1,
      "pulses": [
        { "accountUserId": 123456789, "ringId": "mine8", "targetLayer": 3, "mode": "top" },
        { "accountUserId": 987654321, "ringId": "mine8", "targetLayer": 2, "mode": "oneAbove",
          "burstMs": 2000, "settleMs": 1000, "rockCapMinutes": 5,
          "sweepDwellMs": 600, "sweepNearSide": true, "sweepBlockPx": 160, "oreSweepDwellMs": 400 }
      ]
    }

`targetLayer` counts the ring's layers from the top. `top` clears on that layer; `oneAbove` clears on the layer above it, for an account whose pickaxe struggles there. Left out, `burstMs` is 2000, `settleMs` 1000, `rockCapMinutes` 5, `sweepDwellMs` 400, `sweepNearSide` true, `sweepBlockPx` unset and `oreSweepDwellMs` 400. `sweepDwellMs` is how long the sweep holds on each block (50 to 5000): raise it for an account whose pickaxe breaks blocks slowly. `oreSweepDwellMs` is the same for the ore sweep (50 to 5000; see "Ore is swept too"). `sweepBlockPx` is the block size the sweep steps by when none can be read off the frame (8 to 240): the read tops out near 100 px, so at a close zoom, where a block is 150 to 180 px, set it to your block size and the sweep still runs. A size read off the frame wins when it is near yours (three quarters to four thirds of it); a read further off is ignored for that pass and the log says `block size 110 px (account setting; read 30 px is far from it)`. `sweepNearSide` lets the sweep reach further toward the bottom of the window, where the blocks nearest the camera sit (see "Stone is swept"). Import the ring first, then the pulse, with Ur OCR closed and Ur Task's ore stop macros installed:

    RoRoRo.UrOcr.exe --import-ring mine8.measured.json
    RoRoRo.UrOcr.exe --import-pulse pulse.json

The result is in `pulse-import.log`. An account with a pulse no longer uses the ring triggers above; every other account still does. The pulse only acts while its account's window is in front, and never switches windows for you.

If Ur Task restarts mid-macro (a running playback is never dropped, and Ur Task keeps an ended one for 10 minutes), the pulse stops rather than guess whether it pressed anything; restart Ur OCR to start it again. Pause all (F9) and dry run hold the pulse too, though a macro already in flight keeps being followed: in dry run, a pulse-owned ring shows nothing, since its ring triggers stand down and the pulse itself does not act on anything new.

### Usables

A pulse can fire charges from the hotbar on the way. Each account can have a `ride` usable and a `target` usable, each an Ur Task macro (by id) that presses one hotbar key, and the least time between two fires:

    { "accountUserId": 123456789, "ringId": "mine8", "targetLayer": 3,
      "usables": {
        "ride":   { "macro": "a1000000-0000-4000-8000-000000000011", "everyMs": 3000 },
        "target": { "macro": "a1000000-0000-4000-8000-000000000012", "everyMs": 20000 } } }

`ride` fires at the start of a ride only while the pulse is sure it is above the layer it clears on (a Rover bomb, key 5, on the way down): the last calm read named a layer above it, and no read has reached that layer or gone deeper since the last Go to Top. So not on the first ride from the top, where the game says "You cannot do that here!", not on a burst after a read that found no layer, and not again once the pulse has reached its layer, until it goes to the top. `target` fires when a read lands on the layer the pulse clears on (the target, or the one above it with `oneAbove`), before that pass; then the pulse waits `settleMs` and reads again, since a Core Charge (key 1) drops the character down its shaft. Each fires at most once per `everyMs` (1000 to 600000), never while paused, in dry run or going to top, and never in the middle of another macro. If Ur Task is busy, refuses it or it fails, that fire is skipped and the pulse carries on. The log says `fired usable ride (macro <id>)` or `skipped usable ride (macro <id>): <why>`. Either can be left out; without `usables`, nothing changes. `--import-pulse` checks each macro id is one of Ur Task's.

### Ore finder

The eight ring spots touch about 8 of the 70 blocks in view. With an ore finder measured for the layer an account clears on, the pulse looks at the calm frame around your character instead: every patch in an ore colour within reach (the grid's radius plus one block) becomes a target, nearest first, then a grid of points one block apart out to `radiusBlocks` blocks (5 by default) around your character, nearest first, 64 points at most. Big ore blocks get several spots across them, and the finder no longer aims at your own character. It sends them to Ur Task as one ClearAt call. Ur Task sizes the window to the measured client, hovers each point, checks the white outline, and clears what your pickaxe can reach. A pass that cleared anything reads again; a pass where every point was skipped rides another burst.

Ur OCR also remembers the ore points that showed no outline. The same empty spots tend to come back pass after pass (an ore-coloured patch your pickaxe can't reach), and each one costs about 0.6 s of hovering, so for the next 5 passes an ore point within half a block of one of them is left out of the ClearAt, and the log says `skipping 3 ore points that showed no outline on a recent pass`. After 5 passes the spot is tried again. The memory is wiped whenever the view changes under it: a ride or burst, a Go to Top, a camera turn or a new layer. When every ore point is left out, the pass goes straight to the sweep. Needs Ur Task 0.12.0 or later to say which points were empty; with an older one nothing is remembered and every point is sent as before.

The block size is read from the screen on every pass. The camera stays zoomed out, but the game pulls it in to the first wall behind you, so a block is about 22 px on the open surface, 32 px in a pit and 170 px down a one-block shaft. Ur OCR measures the repeat of the rock pattern around your character and uses it for the grid, the reach and the outline box (one block square, 16 to 240 px). When the frame shows no clear pattern, it uses the `pitch` you measured for the layer. The log says which it used, `block size 32 px (read from the frame)` or `block size 50 px (layer default; no clear pattern)`, each time the size changes.

### Stone is swept

With a guard in the finder and a block size read off the frame, the pulse no longer presses stone one block at a time. Ore still goes first, point by point. Then Ur Task holds the left button down on the block right of your character and walks the pointer around you, one block at a time, ring by ring out to 4 blocks, and lets go back on the block it started on. It never touches the block you stand on, and it skips the game's buttons: the Roblox menu, Go to Top, the icon column on the left, the hotbar with the pets button, and the update timer. The rest of the top strip and the ground right of the hotbar are swept, as the ore finder's points are. Toward the bottom of the window, where the blocks nearest the camera sit, the rows carry on past 4 blocks to just short of the edge, unless you set `sweepNearSide` to false. The next calm frame decides how it went: a sweep that changed none of its blocks counts as a pass with nothing in reach. When the block size can't be read (or reads outside 8 to 240), the sweep steps by the account's `sweepBlockPx` and the log says `block size 160 px (account setting)`; without one, or when the finder has no guard, the stone is cleared point by point as before, and so it is when no sweep path fits around your character at that size (the log says so). Needs Ur Task 0.12.0 or later.

### Ore is swept too

On a pass that sweeps the stone, the ore goes first in one held drag as well, instead of one ClearAt point at a time (a jump and two looks each: 24 ore points took 35 s even when each broke in one 0.3 s hold). The drag starts on the ore point nearest the stone sweep's start block, goes to the nearest ore point not yet visited each time, and ends back on its first point, holding `oreSweepDwellMs` on each. It never stops on the block you stand on, and a move that would cross it goes round by a block beside you; an ore point it can't reach that way goes to ClearAt instead. The log says `ore sweep of 12 points`. Then the stone sweep runs as before, and the next calm frame judges both sweeps together.

Ore the finder sees again on the next pass, within half a block of a spot the last ore sweep went over, did not break in one sweep: it goes to ClearAt, which holds until it breaks, and the log says `ore spots surviving a sweep go to ClearAt: 3`. Only the last ore sweep's spots count, and a ride, burst, Go to Top, camera turn, target usable or new layer forgets them. With fewer than 2 ore points to sweep, the ore goes to ClearAt as before. An Ur Task without free paths (older than 0.12.0's `freePath`) refuses the ore sweep: that pass's ore goes to ClearAt, the stone sweep still runs, and the log says so once.

When nothing is in reach, the pulse turns the camera to look again from three more angles before riding on.

When nothing is in reach, the pulse rides longer each time (up to 30 s), so a tunnel where the camera is jammed close costs little. When it can't tell the block size, it checks fewer spots with a bigger box.

The finder goes in the measured file, one entry per layer you clear on, in pixels of the file's `recordedClientW` x `recordedClientH` (the values below show the shape; measure your own):

    "finders": [
      { "layer": "bottom", "pitch": 50, "centerX": 400, "centerY": 310, "radiusBlocks": 5,
        "outline": { "w": 50, "h": 50, "minCount": 60, "whiteMin": 225 },
        "ore": [ { "name": "cyan crystal", "rgb": { "r": 60, "g": 220, "b": 230 } } ],
        "oreToleranceRgb": 40 }
    ]

`layer` names one of the file's `layers`. `pitch` is one block in pixels at that layer, used only when the screen shows no clear block pattern, `centerX` and `centerY` the middle of your character, `radiusBlocks` 5 when left out. `outline` is Ur Task's outline check: at most 240 pixels a side, `whiteMin` 1 to 255, `minCount` no more than the box's pixels. A patch within `oreToleranceRgb` of an `ore` colour is ore; `"ore": []` clears the grid alone. `ring-fit.ps1 -Write` keeps `finders` when it rewrites `layers`. Import the ring again after adding one; `ring-import.log` names each finder, or says there is none.

With a finder, the pulse also tells the layers apart on that calm frame. Layers can share a colour (the top and bottom of Mine #8 both have a near-black base), so a single spot can't. Instead the pulse looks at the area around your character, three blocks out, with the game's buttons left out, and counts how much of it is in colours only one layer has. The layer with the most wins when it covers at least 4% of the area and at least 1.5 times the next layer. Otherwise it rides another burst. The log gives the shares, for example `layer black (38% of the area, next blue 6%)`. So each layer's `rock` list should hold only the colours that layer alone has, and leave out any colour two layers share. Ore colours never count for a layer. To change the two thresholds, set `layerMinShare` (0.04) or `layerLead` (1.5) at the top of the measured file and import the ring again.

A measured file without `finders`, or an account whose clearing layer has none, clears with the eight "Clear spot" macros as before. The ore finder needs Ur Task 0.11.0 or later; an older one makes the pulse stop with `Unknown method 'ClearAt'`.

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
