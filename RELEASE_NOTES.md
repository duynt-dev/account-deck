# AccountDeck v0.1.0

First public release.

## Highlights

- **Run several accounts side by side.** Each account of Claude Desktop, Chrome, Edge, Brave, Vivaldi, VS Code, Firefox, Telegram, or any Electron or desktop app gets its own data folder and sign-in.
- **Quick switcher.** Press `Ctrl+Alt+A` anywhere, type an app or account name, then press `Enter`. If the account is already open, its window comes to the front; otherwise the account starts.
- **Manager window.** Shows each account as a card with its live running status. Includes a tray menu, desktop shortcuts with per-account icons, and a start-with-Windows option.
- **Scan PC.** Finds installed apps and groups them by how well they support multiple accounts.
- **Claude conversations.** Imports Claude's official data export automatically from your Downloads folder. Browse and search the chats, export them to Markdown, or copy a conversation into another account to keep working on it.

## Downloads

| File | For |
| --- | --- |
| `AccountDeck-0.1.0-win-x64-portable.zip` | Any 64-bit Windows 10/11 PC; nothing else to install |
| `AccountDeck-0.1.0-win-x64.zip` | PCs that already have the .NET 9 Desktop Runtime |
| `SHA256SUMS.txt` | Checksums to verify the downloads |

## Known limitations

- The binaries are not code-signed, so Windows SmartScreen may warn on first launch. Choose **More info → Run anyway**.
- Apps that enforce a single instance can switch accounts but cannot run two accounts at once.
- Game launchers and games with anti-cheat (for example Steam or Riot Client) may refuse to start under a redirected profile.
