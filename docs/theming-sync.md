# Theming sync with the RoRoRo host — implementation notes

Ur Task v0.5 made the plugin follow the host's active theme live, with **zero
plugin-contract changes and no pipe traffic** — everything reads from disk.
These notes carry that playbook to Ur OCR. Reference implementation:
[`rororo-ur-task/src/Theming/HostThemeReader.cs` + `HostThemeService.cs`](https://github.com/estevanhernandez-stack-ed/rororo-ur-task/pull/18)
(+ `HostThemeReaderTests.cs`). The reader is UI-free and ports verbatim; only
namespaces and the wiring point change.

## What the host persists (and where)

| Thing | Path | Format |
|---|---|---|
| Active theme id | `%LOCALAPPDATA%\ROROROblox\settings.json` | camelCase — `"activeThemeId": "midnight"` |
| User themes | `%LOCALAPPDATA%\ROROROblox\themes\<id>.json` | snake_case slots (`muted_text`, `row_bg`, …); comments + trailing commas tolerated |
| Built-in themes | compile-time constants in `ROROROblox.Core` `ThemeStore.BuildBuiltIns()` | `brand`, `midnight`, `magenta-heat` — **must be mirrored in plugin code** |

## Slot mapping for Ur OCR

Ur OCR's `App.xaml` brush keys are identical to Ur Task's, so the mapping is
1:1 — the Ur Task code drops in unchanged:

| App.xaml key | Host theme slot |
|---|---|
| `BgBrush` | `bg` |
| `CyanBrush` | `cyan` |
| `MagentaBrush` | `magenta` |
| `WhiteBrush` | `white` |
| `MutedTextBrush` | `muted_text` |
| `DividerBrush` | `divider` |
| `RowBgBrush` | `row_bg` |
| `RowHoverBrush` | *(derived — `row_bg` blended 4% toward `white`; no host slot)* |

Host slots `row_expired_bg`, `row_expired_accent`, `navy` are unused here.

## Resolve algorithm

1. Read `activeThemeId` from `settings.json`. Missing file / field / bad JSON → **Brand**.
2. Case-insensitive match against the mirrored built-ins → use the mirror.
3. Else parse `themes\<id>.json` (snake_case). Missing file / malformed / missing slot → **Brand**.

Every failure path lands on Brand — a hand-edited host file must never break
the plugin, and the plugin stays fully usable standalone (host not installed →
Brand, which matches the XAML defaults).

## Apply strategy — mutate brushes, don't sweep to DynamicResource

`App.xaml`'s brushes are plain **unfrozen** `SolidColorBrush`. Setting
`brush.Color = newColor` re-renders every `{StaticResource}` consumer — so the
entire XAML surface re-themes with **no DynamicResource sweep**. Preconditions:

- Keep the brushes plain `SolidColorBrush` — no `PresentationOptions:Freeze`.
- If an entry comes back frozen or non-brush, fall back to replacing the
  dictionary entry (replacement only propagates to DynamicResource consumers,
  so treat it as a defensive fallback, not the main path).
- Marshal applies to the dispatcher; skip slots whose hex fails to parse
  (keep the current color rather than painting black).

## Live follow

One `FileSystemWatcher` on `%LOCALAPPDATA%\ROROROblox`, filter `*.json`,
`IncludeSubdirectories = true` — catches both `settings.json` and
`themes\*.json` in one subscription. Host saves are tmp-write + rename bursts,
so debounce ~300ms (DispatcherTimer) and marshal watcher events to the UI
thread. Watching is best-effort: folder missing → apply once and skip watching.

## Wiring points (this repo)

- Port `HostThemeReader` + `HostThemeService` into `RoRoRo.UrOcr.Theming`.
- `App.xaml.cs OnStartup`: start the service immediately after
  `base.OnStartup(e)` and **before** `await Runtime.StartAsync()` — the
  `StartupUri` window must first-render already themed.
- Dispose it in `OnExit`.

## Named risk — built-in mirror drift

The three built-in palettes are compile-time constants in `ROROROblox.Core`,
mirrored in plugin code. If the host's built-ins change, every plugin mirror
needs a matching bump. Cheap future fix host-side: publish built-ins as JSON in
the themes folder so plugins read them like user themes.

## Tests to port

`HostThemeReaderTests` from Ur Task: settings parse (camelCase), theme-file
parse (snake_case + comments), built-in resolve, user-file resolve, Brand
fallbacks (missing settings / unknown id / missing folder), blend determinism.
All pure — no WPF Application needed.
