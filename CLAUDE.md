# CLAUDE.md — AccountDeck

Windows multi-account launcher and switcher (C# / .NET 9 WinForms). Original code by duynt-dev; do not copy code from other projects into it.

## Layout

- `src/AccountDeck/Core` — no UI: config (`ConfigStore`, `Models`), app presets and isolation modes (`AppPresets`), Claude detection (`ClaudeLocator`), process inspection without WMI (`ProcessInspector`, `InstanceTracker`), launching/focusing (`Launcher`), icons, shortcuts, autostart.
- `src/AccountDeck/UI` — owner-drawn dark UI: `DeckHost` (tray, global hotkey, lifetime), `MainForm`, `QuickSwitcher`, `Dialogs`, `Controls`, `Theme`.
- `src/AccountDeck/Conversations` — reader/viewer for Claude's official data export.

## Conventions

- All sizes go through `Theme.S(...)` (forms use `AutoScaleMode.None`).
- Controls are created in code; `WFO1000` is suppressed on purpose.
- An account with an empty `DataDir` is the app's normal sign-in and is launched without isolation.
- Never read or reuse another app's cookies/tokens; conversation access is only through official exports.
- Allowed metadata reads: Chromium `Local State` profile names, Claude settings files keyed by account id, and — with the owner's approval — the single key `lastKnownAccountUuid` from Claude's `config.json`, read with a forward-only `Utf8JsonReader` that skips every other value (`ClaudeLocalInfo`). Do not widen this to other keys in that file.

## Build

`dotnet build src/AccountDeck` · run with `dotnet run --project src/AccountDeck`.
