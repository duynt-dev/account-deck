using System.ComponentModel;
using AccountDeck.Conversations;
using AccountDeck.Core;

namespace AccountDeck.UI;

/// <summary>
/// Owns the app's lifetime: tray icon, global hotkey, the manager and quick switcher windows,
/// and a periodic scan of which accounts are running.
/// </summary>
internal sealed class DeckHost : ApplicationContext
{
    readonly NotifyIcon _tray;
    readonly HotkeyWindow _hotkey;
    readonly System.Windows.Forms.Timer _timer;
    readonly Control _ui;
    readonly RegisteredWaitHandle _showWait;
    MainForm? _main;
    QuickSwitcher? _quick;
    ExportWatcher? _exports;
    Action? _balloonAction;
    bool _scanning;

    public DeckHost(ConfigStore store, EventWaitHandle showSignal, bool startHidden)
    {
        Store = store;
        AppIcon = AppIconArt.CreateIcon();

        _ui = new Control();
        _ = _ui.Handle; // bind to the UI thread for cross-thread calls

        var menu = Theme.Menu();
        menu.Opening += BuildTrayMenu;
        _tray = new NotifyIcon { Icon = AppIcon, Text = "AccountDeck", Visible = true, ContextMenuStrip = menu };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowManager(); };
        _tray.BalloonTipClicked += (_, _) => { var action = _balloonAction; _balloonAction = null; action?.Invoke(); };
        _tray.BalloonTipClosed += (_, _) => _balloonAction = null;

        _hotkey = new HotkeyWindow();
        _hotkey.Pressed += ShowQuickSwitcher;
        HotkeyText = store.Config.QuickSwitchHotkey;
        if (!TryParseHotkey(HotkeyText, out var modifiers, out var key) || !_hotkey.Register(modifiers, key))
        {
            _tray.ShowBalloonTip(5000, "AccountDeck",
                $"The quick-switch shortcut \"{HotkeyText}\" is unavailable (taken by another app or not valid). " +
                $"Change QuickSwitchHotkey in {AppPaths.ConfigFile}. The quick switcher is still in the tray menu.", ToolTipIcon.Info);
            HotkeyText = "";
        }

        _timer = new System.Windows.Forms.Timer { Interval = 2500 };
        _timer.Tick += (_, _) => RefreshRunning();
        _timer.Start();
        RefreshRunning();

        _showWait = ThreadPool.RegisterWaitForSingleObject(showSignal,
            (_, _) => _ui.BeginInvoke(ShowManager), null, Timeout.Infinite, executeOnlyOnce: false);

        if (store.Config.AutoImportClaudeExports) StartExportWatcher();

        if (!startHidden) ShowManager();
    }

    public ConfigStore Store { get; }
    public Icon AppIcon { get; }
    public bool Exiting { get; private set; }

    /// <summary>The registered quick-switch shortcut, or empty when none could be registered.</summary>
    public string HotkeyText { get; }

    /// <summary>Account id → its process ids, refreshed every few seconds.</summary>
    public IReadOnlyDictionary<string, List<int>> Running { get; private set; } = new Dictionary<string, List<int>>();

    public event EventHandler? RunningChanged;

    public void ShowManager()
    {
        if (_main is null || _main.IsDisposed) _main = new MainForm(this);
        if (!_main.Visible) _main.Show();
        if (_main.WindowState == FormWindowState.Minimized) _main.WindowState = FormWindowState.Normal;
        _main.Activate();
    }

    public void ShowQuickSwitcher()
    {
        if (_quick is null || _quick.IsDisposed) _quick = new QuickSwitcher(this);
        _quick.Present();
    }

    public bool Open(AppEntry app, AccountProfile account, IWin32Window? owner)
    {
        try
        {
            Launcher.Open(app, account);
            _ = RefreshSoonAsync();
            return true;
        }
        catch (Exception ex) when (ex is LaunchException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(owner, ex.Message, "AccountDeck", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    public async void RefreshRunning()
    {
        if (_scanning) return;
        _scanning = true;
        try
        {
            var apps = Store.Apps.ToList();
            var result = await Task.Run(() => InstanceTracker.Scan(apps));
            var changed = !SameAccounts(result, Running);
            Running = result;
            if (changed) RunningChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or IOException)
        {
            // A process vanished mid-scan; the next tick will catch up.
        }
        finally
        {
            _scanning = false;
        }
    }

    public void Exit()
    {
        Exiting = true;
        _timer.Stop();
        _tray.Visible = false;
        _main?.Close();
        _quick?.Close();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _showWait.Unregister(null);
            StopExportWatcher();
            _hotkey.Dispose();
            _tray.Dispose();
            _timer.Dispose();
            _ui.Dispose();
            AppIcon.Dispose();
        }
        base.Dispose(disposing);
    }

    // ----- Claude exports in Downloads ------------------------------------

    void StartExportWatcher()
    {
        if (_exports is not null) return;
        _exports = new ExportWatcher(SynchronizationContext.Current!);
        _exports.ExportFound += OnExportFound;
        _exports.Start(Store.Config.LastExportCheckUtc);
    }

    void StopExportWatcher()
    {
        if (_exports is null) return;
        _exports.ExportFound -= OnExportFound;
        _exports.Dispose();
        _exports = null;
    }

    IReadOnlyList<(AppEntry App, AccountProfile Account)> ClaudeAccounts() =>
        Store.Apps.Where(a => a.PresetId == "claude").SelectMany(a => a.Accounts.Select(acc => (a, acc))).ToList();

    void OnExportFound(string zipPath)
    {
        Store.Config.LastExportCheckUtc = DateTime.UtcNow;
        Store.Save();

        var accounts = ClaudeAccounts();
        if (accounts.Count == 0) return;

        // Best signal first: the account id read from the profile's own settings, then the
        // e-mail of an earlier import, then an e-mail typed into the account's note.
        var owner = ClaudeExport.OwnerInArchive(zipPath);
        var email = owner?.Email;
        var matches = owner?.Uuid is { } uuid
            ? accounts.Where(x => string.Equals(ClaudeLocalInfo.AccountUuid(x.App, x.Account), uuid, StringComparison.OrdinalIgnoreCase)).ToList()
            : new List<(AppEntry App, AccountProfile Account)>();
        if (matches.Count != 1 && email is not null)
            matches = accounts.Where(x =>
                    string.Equals(ClaudeExport.ImportedEmail(x.Account), email, StringComparison.OrdinalIgnoreCase) ||
                    x.Account.Note.Contains(email, StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (matches.Count == 1)
        {
            ImportExport(matches[0].App, matches[0].Account, zipPath);
            return;
        }

        var from = email is null ? "" : $" from {email}";
        ShowBalloon($"Claude export found{from}. Click to choose which account it belongs to.",
            () => AskWhereToImport(zipPath, email));
    }

    void AskWhereToImport(string zipPath, string? email)
    {
        var accounts = ClaudeAccounts();
        if (accounts.Count == 0 || !File.Exists(zipPath)) return;
        using var dialog = new ImportTargetDialog(zipPath, email, accounts) { StartPosition = FormStartPosition.CenterScreen, TopMost = true };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        ImportExport(dialog.Selected.App, dialog.Selected.Account, zipPath);
    }

    void ImportExport(AppEntry app, AccountProfile account, string zipPath)
    {
        try
        {
            var library = ClaudeExport.Import(account, zipPath);
            ShowBalloon($"Imported {library.Conversations.Count} conversations into {app.Name} · {account.Name}. Click to open them.",
                () => new ConversationsForm(app, account).Show());
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ShowBalloon($"Could not import {Path.GetFileName(zipPath)}: {ex.Message}", null);
        }
    }

    void ShowBalloon(string text, Action? onClick)
    {
        _balloonAction = onClick;
        _tray.ShowBalloonTip(8000, "AccountDeck", text, ToolTipIcon.Info);
    }

    async Task RefreshSoonAsync()
    {
        // Give a freshly started app a moment to spawn its processes.
        await Task.Delay(1500);
        RefreshRunning();
    }

    /// <summary>Parses "Ctrl+Alt+A"-style text into RegisterHotKey modifiers and a key.</summary>
    static bool TryParseHotkey(string text, out uint modifiers, out Keys key)
    {
        modifiers = 0;
        key = Keys.None;
        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= Native.ModControl; break;
                case "alt": modifiers |= Native.ModAlt; break;
                case "shift": modifiers |= Native.ModShift; break;
                case "win" or "windows": modifiers |= Native.ModWin; break;
                default:
                    var name = part.Length == 1 && char.IsDigit(part[0]) ? "D" + part : part;
                    if (!Enum.TryParse(name, ignoreCase: true, out key)) return false;
                    break;
            }
        }
        return modifiers != 0 && key != Keys.None;
    }

    static bool SameAccounts(IReadOnlyDictionary<string, List<int>> a, IReadOnlyDictionary<string, List<int>> b) =>
        a.Count == b.Count && a.Keys.All(b.ContainsKey);

    void BuildTrayMenu(object? sender, CancelEventArgs e)
    {
        var menu = (ContextMenuStrip)sender!;
        menu.Items.Clear();

        var quick = Theme.Item("Quick switch", ShowQuickSwitcher, Theme.Accent);
        quick.ShortcutKeyDisplayString = HotkeyText;
        menu.Items.Add(quick);
        menu.Items.Add(Theme.Item("Open AccountDeck", ShowManager));
        menu.Items.Add(new ToolStripSeparator());

        foreach (var app in Store.Apps)
        {
            var sub = new ToolStripMenuItem(app.Name, AppIcons.Get(app)) { ForeColor = Theme.Text };
            if (sub.DropDown is ToolStripDropDownMenu dropDown)
            {
                dropDown.Renderer = menu.Renderer;
                dropDown.BackColor = Theme.Card;
                dropDown.ShowImageMargin = false;
            }
            foreach (var account in app.Accounts)
            {
                var running = Running.ContainsKey(account.Id);
                var item = Theme.Item(account.Name, () => Open(app, account, null), running ? Theme.Running : Theme.Text);
                if (running) item.ShortcutKeyDisplayString = "running";
                sub.DropDownItems.Add(item);
            }
            menu.Items.Add(sub);
        }

        menu.Items.Add(new ToolStripSeparator());
        var autostart = Theme.Item("Start with Windows", () => SystemIntegration.StartWithWindows = !SystemIntegration.StartWithWindows);
        autostart.Checked = SystemIntegration.StartWithWindows;
        menu.Items.Add(autostart);
        var hide = Theme.Item("Hide manager after opening an account", () =>
        {
            Store.Config.HideManagerAfterLaunch = !Store.Config.HideManagerAfterLaunch;
            Store.Save();
        });
        hide.Checked = Store.Config.HideManagerAfterLaunch;
        menu.Items.Add(hide);
        var autoImport = Theme.Item("Auto-import Claude exports from Downloads", () =>
        {
            Store.Config.AutoImportClaudeExports = !Store.Config.AutoImportClaudeExports;
            Store.Config.LastExportCheckUtc = DateTime.UtcNow;
            Store.Save();
            if (Store.Config.AutoImportClaudeExports) StartExportWatcher();
            else StopExportWatcher();
        });
        autoImport.Checked = Store.Config.AutoImportClaudeExports;
        menu.Items.Add(autoImport);
        menu.Items.Add(Theme.Item("Exit", Exit));
        e.Cancel = false;
    }

    /// <summary>Hidden window that receives the global hotkey.</summary>
    sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        const int HotkeyId = 0xAD01;
        bool _registered;

        public HotkeyWindow() => CreateHandle(new CreateParams());

        public event Action? Pressed;

        public bool Register(uint modifiers, Keys key)
        {
            _registered = Native.RegisterHotKey(Handle, HotkeyId, modifiers | Native.ModNoRepeat, (uint)key);
            return _registered;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WmHotkey && (int)m.WParam == HotkeyId) Pressed?.Invoke();
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (_registered) Native.UnregisterHotKey(Handle, HotkeyId);
            DestroyHandle();
        }
    }
}
