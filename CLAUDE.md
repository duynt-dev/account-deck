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

## Build

`dotnet build src/AccountDeck` · run with `dotnet run --project src/AccountDeck`.
