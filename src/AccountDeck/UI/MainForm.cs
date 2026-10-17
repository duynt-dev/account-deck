using System.Drawing.Drawing2D;
using AccountDeck.Conversations;
using AccountDeck.Core;

namespace AccountDeck.UI;

/// <summary>The manager window: apps on the left, the selected app's accounts as cards on the right.</summary>
internal sealed class MainForm : Form
{
    readonly DeckHost _host;
    readonly ItemList _apps;
    readonly Panel _header;
    readonly PillButton _addAccount;
    readonly PillButton _appSettings;
    readonly SearchBox _filter;
    readonly ScrollView _cardScroll;
    FlowLayoutPanel _cards => _cardScroll.Content;
    List<AccountProfile> _visibleAccounts = new();
    AppEntry? _current;

    ConfigStore Store => _host.Store;

    public MainForm(DeckHost host)
    {
        _host = host;
        Theme.StyleForm(this);
        Text = "AccountDeck";
        Icon = host.AppIcon;
        Size = new Size(Theme.S(1040), Theme.S(680));
        MinimumSize = new Size(Theme.S(780), Theme.S(480));
        StartPosition = FormStartPosition.CenterScreen;

        // Sidebar: brand, app list, "add app".
        var sidebar = new Panel { Dock = DockStyle.Left, Width = Theme.S(240), BackColor = Theme.Sidebar, Padding = new Padding(Theme.S(12), Theme.S(14), Theme.S(12), Theme.S(14)) };
        var brand = new Panel { Dock = DockStyle.Top, Height = Theme.S(44) };
        brand.Paint += PaintBrand;
        var caption = new Label
        {
            Text = "APPS", Dock = DockStyle.Top, Height = Theme.S(28), Font = Theme.Small, ForeColor = Theme.Faint,
            TextAlign = ContentAlignment.BottomLeft, Padding = new Padding(Theme.S(8), 0, 0, Theme.S(4)),
        };
        _apps = new ItemList(48) { Dock = DockStyle.Fill, BackColor = Theme.Sidebar, Painter = PaintAppRow };
        _apps.SelectionChanged += (_, _) => ShowApp(_apps.SelectedItem as AppEntry);
        _apps.ItemMenuRequested += (_, p) => ShowAppMenu(_apps.PointToScreen(p));
        var sidebarButtons = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = Theme.S(36), ColumnCount = 2, Margin = Padding.Empty };
        sidebarButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        sidebarButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var addApp = new PillButton("+  Add app") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, Theme.S(4), 0) };
        addApp.Click += (_, _) => AddApp();
        var scan = new PillButton("Scan PC") { Dock = DockStyle.Fill, Margin = new Padding(Theme.S(4), 0, 0, 0) };
        scan.Click += (_, _) => ScanForApps();
        sidebarButtons.Controls.Add(addApp, 0, 0);
        sidebarButtons.Controls.Add(scan, 1, 0);
        sidebar.Controls.Add(_apps);
        sidebar.Controls.Add(sidebarButtons);
        sidebar.Controls.Add(caption);
        sidebar.Controls.Add(brand);

        // Content: header, filter, cards, footer.
        var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Window, Padding = new Padding(Theme.S(28), Theme.S(18), Theme.S(20), Theme.S(10)) };
        _header = new Panel { Dock = DockStyle.Top, Height = Theme.S(70) };
        _header.Paint += PaintHeader;
        _header.Resize += (_, _) => _header.Invalidate();

        var headerButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, AutoSize = true, WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, Theme.S(12), Theme.S(8), 0),
        };
        _appSettings = new PillButton("App settings");
        _appSettings.Click += (_, _) => EditApp(_current);
        _addAccount = new PillButton("+  New account", primary: true);
        _addAccount.Click += (_, _) => AddAccount();
        headerButtons.Controls.Add(_appSettings);
        headerButtons.Controls.Add(_addAccount);
        _header.Controls.Add(headerButtons);

        var toolbar = new Panel { Dock = DockStyle.Top, Height = Theme.S(56) };
        _filter = new SearchBox("Filter accounts") { Width = Theme.S(320), Location = new Point(0, Theme.S(6)) };
        _filter.TextChanged += (_, _) => RenderCards();
        toolbar.Controls.Add(_filter);

        _cardScroll = new ScrollView(wrap: true) { Dock = DockStyle.Fill };

        var footer = new Label
        {
            Dock = DockStyle.Bottom, Height = Theme.S(26), Font = Theme.Small, ForeColor = Theme.Faint, TextAlign = ContentAlignment.MiddleLeft,
            Text = (host.HotkeyText.Length > 0 ? $"{host.HotkeyText} quick switch   ·   " : "") +
                   "Alt+1…9 open   ·   Ctrl+1…9 change app   ·   Ctrl+N new account   ·   Ctrl+F filter",
        };

        content.Controls.Add(_cardScroll);
        content.Controls.Add(footer);
        content.Controls.Add(toolbar);
        content.Controls.Add(_header);
        Controls.Add(content);
        Controls.Add(sidebar);

        _host.RunningChanged += OnRunningChanged;
        Store.Changed += OnStoreChanged;
        ReloadApps(Store.FindApp(Store.Config.LastAppId) ?? Store.Apps.FirstOrDefault());
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_host.Exiting && Store.Config.CloseToTray && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        _host.RunningChanged -= OnRunningChanged;
        Store.Changed -= OnStoreChanged;
        base.OnFormClosing(e);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _filter.Box.Focus();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        var digit = key is >= Keys.D1 and <= Keys.D9 ? key - Keys.D1 : -1;

        if (keyData == (Keys.Control | Keys.F)) { _filter.Box.Focus(); _filter.Box.SelectAll(); return true; }
        if (keyData == (Keys.Control | Keys.N)) { AddAccount(); return true; }
        if (keyData == (Keys.Control | Keys.Shift | Keys.N)) { AddApp(); return true; }
        if (keyData == Keys.F5) { _host.RefreshRunning(); return true; }
        if (digit >= 0 && keyData == (Keys.Alt | key))
        {
            if (digit < _visibleAccounts.Count) OpenAccount(_visibleAccounts[digit]);
            return true;
        }
        if (digit >= 0 && keyData == (Keys.Control | key))
        {
            if (digit < _apps.Items.Count) _apps.Select(digit);
            return true;
        }
        if (keyData == Keys.Enter && _filter.Box.Focused && _visibleAccounts.Count > 0)
        {
            OpenAccount(_visibleAccounts[0]);
            return true;
        }
        if (keyData == Keys.Escape)
        {
            if (_filter.Text.Length > 0) _filter.Text = "";
            else Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ----- data → UI -------------------------------------------------------

    void ReloadApps(AppEntry? select)
    {
        _apps.SetItems(Store.Apps, select ?? _current);
        if (_apps.SelectedItem is null) ShowApp(null);
    }

    void ShowApp(AppEntry? app)
    {
        _current = app;
        if (app is not null && Store.Config.LastAppId != app.Id)
        {
            Store.Config.LastAppId = app.Id;
        }
        _addAccount.Enabled = _appSettings.Enabled = app is not null;
        _header.Invalidate();
        _cardScroll.ScrollToTop();
        RenderCards();
    }

    void RenderCards()
    {
        _cards.SuspendLayout();
        foreach (var control in _cards.Controls.Cast<Control>().ToList()) control.Dispose();

        if (_current is null)
        {
            _visibleAccounts = new();
            var tile = new AddTile("Add your first app");
            tile.Click += (_, _) => AddApp();
            _cards.Controls.Add(tile);
            _cards.ResumeLayout();
            return;
        }

        var query = _filter.Text.Trim();
        _visibleAccounts = _current.Accounts
            .Where(a => query.Length == 0 ||
                        a.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        a.Note.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        for (var i = 0; i < _visibleAccounts.Count; i++)
        {
            var account = _visibleAccounts[i];
            var card = new AccountCard(_current, account, i) { Running = _host.Running.ContainsKey(account.Id) };
            card.OpenRequested += (_, _) => OpenAccount(account);
            card.MenuRequested += (_, p) => ShowAccountMenu(account, card.PointToScreen(p));
            _cards.Controls.Add(card);
        }

        var add = new AddTile("New account");
        add.Click += (_, _) => AddAccount();
        _cards.Controls.Add(add);
        _cards.ResumeLayout();
    }

    void OnRunningChanged(object? sender, EventArgs e)
    {
        foreach (var card in _cards.Controls.OfType<AccountCard>())
            card.Running = _host.Running.ContainsKey(card.Account.Id);
        _apps.Invalidate();
    }

    void OnStoreChanged(object? sender, EventArgs e)
    {
        ReloadApps(_current);
        RenderCards();
    }

    // ----- actions ---------------------------------------------------------

    void OpenAccount(AccountProfile account)
    {
        if (_current is null) return;
        if (_host.Open(_current, account, this) && Store.Config.HideAfterLaunch) Hide();
    }

    void AddApp()
    {
        using var dialog = new AppDialog(null);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Store.Config.Apps.Add(dialog.Result);
        _current = dialog.Result;
        Store.Save();
    }

    async void ScanForApps()
    {
        UseWaitCursor = true;
        List<FoundApp> found;
        try
        {
            var existing = Store.Apps.ToList();
            found = await Task.Run(() => AppScanner.Scan(existing));
        }
        finally
        {
            UseWaitCursor = false;
        }

        using var dialog = new ScanAppsDialog(found);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Selected.Count == 0) return;
        var added = dialog.Selected.Select(f => f.ToAppEntry()).ToList();
        Store.Config.Apps.AddRange(added);
        _current = added[0];
        Store.Save();
    }

    void EditApp(AppEntry? app)
    {
        if (app is null) return;
        using var dialog = new AppDialog(app);
        if (dialog.ShowDialog(this) == DialogResult.OK) Store.Save();
    }

    void RemoveApp(AppEntry app)
    {
        var answer = MessageBox.Show(this,
            $"Remove {app.Name} and its {app.Accounts.Count} account(s) from AccountDeck?\n\nNo sign-in data is deleted; the accounts' folders stay on disk.",
            "Remove app", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (answer != DialogResult.OK) return;
        Store.Config.Apps.Remove(app);
        _current = null;
        Store.Save();
    }

    void MoveApp(AppEntry app, int delta)
    {
        var list = Store.Config.Apps;
        var from = list.IndexOf(app);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= list.Count) return;
        list.RemoveAt(from);
        list.Insert(to, app);
        Store.Save();
    }

    void AddAccount()
    {
        if (_current is null) { AddApp(); return; }
        using var dialog = new AccountDialog(Store, _current, null);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _current.Accounts.Add(dialog.Result);
        Store.Save();
        if (dialog.CreateShortcut) TryCreateShortcut(dialog.Result);
    }

    void EditAccount(AccountProfile account)
    {
        if (_current is null) return;
        var oldShortcut = SystemIntegration.ShortcutPath(_current, account);
        var hadShortcut = File.Exists(oldShortcut);
        using var dialog = new AccountDialog(Store, _current, account);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Store.Save();
        if (hadShortcut)
        {
            File.Delete(oldShortcut);
            TryCreateShortcut(account);
        }
    }

    void RemoveAccount(AccountProfile account)
    {
        if (_current is null) return;
        var folder = account.IsDefault ? "" : $"\n\nIts data folder is kept:\n{AppPaths.Expand(account.DataDir)}";
        var answer = MessageBox.Show(this, $"Remove the account \"{account.Name}\" from AccountDeck?{folder}",
            "Remove account", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (answer != DialogResult.OK) return;
        SystemIntegration.DeleteShortcut(_current, account);
        _current.Accounts.Remove(account);
        Store.Save();
    }

    void MoveAccount(AccountProfile account, int delta)
    {
        if (_current is null) return;
        var list = _current.Accounts;
        var from = list.IndexOf(account);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= list.Count) return;
        list.RemoveAt(from);
        list.Insert(to, account);
        Store.Save();
    }

    void TryCreateShortcut(AccountProfile account)
    {
        if (_current is null) return;
        try { SystemIntegration.CreateShortcut(_current, account); }
        catch (Exception ex) { MessageBox.Show(this, "Could not create the shortcut: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    void ShowAccountMenu(AccountProfile account, Point screen)
    {
        if (_current is null) return;
        var app = _current;
        var menu = Theme.Menu();
        var running = _host.Running.ContainsKey(account.Id);
        menu.Items.Add(Theme.Item(running ? "Switch to window" : "Open", () => OpenAccount(account), Theme.Accent));
        menu.Items.Add(Theme.Item("Edit…", () => EditAccount(account)));

        var folder = SystemIntegration.DataFolderOnDisk(app, account);
        var openFolder = Theme.Item("Open data folder", () => SystemIntegration.OpenFolder(folder!));
        openFolder.Enabled = folder is not null;
        menu.Items.Add(openFolder);
        menu.Items.Add(Theme.Item("Create desktop shortcut", () =>
        {
            TryCreateShortcut(account);
        }));
        if (app.PresetId == "claude")
            menu.Items.Add(Theme.Item("Conversations (from export)…", () => new ConversationsForm(app, account).Show(this)));

        menu.Items.Add(new ToolStripSeparator());
        var index = app.Accounts.IndexOf(account);
        var left = Theme.Item("Move earlier", () => MoveAccount(account, -1));
        left.Enabled = index > 0;
        var right = Theme.Item("Move later", () => MoveAccount(account, 1));
        right.Enabled = index < app.Accounts.Count - 1;
        menu.Items.Add(left);
        menu.Items.Add(right);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Theme.Item("Remove", () => RemoveAccount(account), Theme.Danger));
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(screen);
    }

    void ShowAppMenu(Point screen)
    {
        if (_apps.SelectedItem is not AppEntry app) return;
        var menu = Theme.Menu();
        menu.Items.Add(Theme.Item("App settings…", () => EditApp(app)));
        var exe = AppPresets.ResolveExe(app);
        var folder = Theme.Item("Open program folder", () => SystemIntegration.OpenFolder(Path.GetDirectoryName(exe!)!));
        folder.Enabled = exe is not null;
        menu.Items.Add(folder);
        menu.Items.Add(new ToolStripSeparator());
        var index = Store.Config.Apps.IndexOf(app);
        var up = Theme.Item("Move up", () => MoveApp(app, -1));
        up.Enabled = index > 0;
        var down = Theme.Item("Move down", () => MoveApp(app, 1));
        down.Enabled = index < Store.Config.Apps.Count - 1;
        menu.Items.Add(up);
        menu.Items.Add(down);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Theme.Item("Remove app", () => RemoveApp(app), Theme.Danger));
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(screen);
    }

    // ----- painting --------------------------------------------------------

    void PaintBrand(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var size = Theme.S(28f);
        AppIconArt.Paint(g, new RectangleF(Theme.S(6f), (Theme.S(44) - size) / 2, size, size));
        TextRenderer.DrawText(g, "AccountDeck", Theme.Heading, new Point(Theme.S(44), Theme.S(11)), Theme.Text);
    }

    void PaintAppRow(Graphics g, Rectangle bounds, object item, bool selected, bool hover)
    {
        var app = (AppEntry)item;
        var r = new RectangleF(bounds.X + Theme.S(2f), bounds.Y + Theme.S(2f), bounds.Width - Theme.S(4f), bounds.Height - Theme.S(4f));
        if (selected || hover) PaintedControl.FillRound(g, r, Theme.S(9f), selected ? Theme.Selected : Theme.CardHover);
        if (selected) PaintedControl.FillRound(g, new RectangleF(r.X, r.Y + Theme.S(12f), Theme.S(3f), r.Height - Theme.S(24f)), Theme.S(1.5f), Theme.Accent);

        var iconSize = Theme.S(26);
        var iconRect = new Rectangle(bounds.X + Theme.S(14), bounds.Y + (bounds.Height - iconSize) / 2, iconSize, iconSize);
        DrawAppIcon(g, app, iconRect);

        var running = app.Accounts.Count(a => _host.Running.ContainsKey(a.Id));
        var badgeWidth = 0;
        if (running > 0)
        {
            var label = running.ToString();
            var size = TextRenderer.MeasureText(label, Theme.Small);
            badgeWidth = size.Width + Theme.S(14);
            var pill = new RectangleF(bounds.Right - badgeWidth - Theme.S(10), bounds.Y + (bounds.Height - Theme.S(20)) / 2f, badgeWidth, Theme.S(20));
            PaintedControl.FillRound(g, pill, Theme.S(10f), Color.FromArgb(40, Theme.Running));
            TextRenderer.DrawText(g, label, Theme.Small, Rectangle.Round(pill), Theme.Running,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        var textLeft = iconRect.Right + Theme.S(12);
        var textRect = new Rectangle(textLeft, bounds.Y, bounds.Right - textLeft - badgeWidth - Theme.S(14), bounds.Height);
        TextRenderer.DrawText(g, app.Name, selected ? Theme.BodyBold : Theme.Body, textRect, selected ? Theme.Text : Theme.Dim,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }

    internal static void DrawAppIcon(Graphics g, AppEntry app, Rectangle rect)
    {
        if (AppIcons.Get(app) is { } icon)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(icon, rect);
        }
        else
        {
            BadgeIcon.Paint(g, rect, BadgeIcon.ParseColor(app.Color, Theme.Faint), app.Name.Length > 0 ? app.Name[..1].ToUpperInvariant() : "?");
        }
    }

    void PaintHeader(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (_current is null)
        {
            TextRenderer.DrawText(g, "No app selected", Theme.Title, new Point(0, Theme.S(8)), Theme.Text);
            TextRenderer.DrawText(g, "Add an app such as Claude, Chrome or Telegram, then create an account for each sign-in.",
                Theme.Small, new Point(Theme.S(2), Theme.S(42)), Theme.Dim);
            return;
        }

        var icon = Theme.S(44);
        DrawAppIcon(g, _current, new Rectangle(0, Theme.S(8), icon, icon));
        var left = icon + Theme.S(16);
        TextRenderer.DrawText(g, _current.Name, Theme.Title, new Point(left, Theme.S(4)), Theme.Text);

        var exe = AppPresets.ResolveExe(_current);
        var running = _current.Accounts.Count(a => _host.Running.ContainsKey(a.Id));
        var detail = exe is null
            ? "Program not found — set it in App settings"
            : $"{_current.Accounts.Count} account(s) · {running} running · {exe}";
        var width = _header.Width - left - Theme.S(300);
        TextRenderer.DrawText(g, detail, Theme.Small, new Rectangle(left, Theme.S(38), Math.Max(50, width), Theme.S(18)),
            exe is null ? Theme.Danger : Theme.Dim, TextFormatFlags.PathEllipsis | TextFormatFlags.SingleLine);
    }
}
