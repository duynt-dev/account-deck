using AccountDeck.Core;

namespace AccountDeck.UI;

/// <summary>
/// One screen to add and remove apps: a grid of icon tiles grouped by category. Apps already in
/// AccountDeck start ticked. Every tick or untick is saved straight away.
/// </summary>
internal sealed class ManageAppsDialog : DialogBase
{
    const int Columns = 8;

    readonly ConfigStore _store;
    readonly Func<List<FoundApp>> _scan;
    readonly SearchBox _search;
    readonly ScrollView _grid;
    readonly Label _status;
    readonly ToolTip _tips = new();
    readonly List<AppTile> _tiles = new();
    readonly List<Control> _headers = new();
    bool _reverting;

    public ManageAppsDialog(ConfigStore store, Func<List<FoundApp>> scan) : base("Manage apps", "Done")
    {
        _store = store;
        _scan = scan;
        CancelPill.Visible = false;
        var width = Columns * Theme.S(100) + Theme.S(16);

        var top = new Panel { Width = width, Height = Theme.S(40), Margin = new Padding(0, Theme.S(6), 0, Theme.S(6)) };
        _search = new SearchBox("Search apps") { Width = Theme.S(300), Location = Point.Empty };
        _search.TextChanged += (_, _) => RenderGrid();
        top.Controls.Add(_search);
        top.Controls.Add(Legend(width - Theme.S(310)));
        Target.Controls.Add(top);

        _status = new Label
        {
            Text = "Tick an app to add it, untick to remove it. Changes are saved automatically. Looking for more apps…",
            AutoSize = true, MaximumSize = new Size(width, 0), ForeColor = Theme.Faint, Font = Theme.Small,
            Margin = new Padding(0, 0, 0, Theme.S(8)),
        };
        Target.Controls.Add(_status);

        _grid = new ScrollView(wrap: true) { Width = width, Height = Theme.S(440), Margin = Padding.Empty };
        Target.Controls.Add(_grid);

        foreach (var app in store.Apps)
        {
            var mode = IsolationMode.Detect(app);
            AddTile(new AppChoice
            {
                Name = app.Name,
                ExePath = AppPresets.ResolveExe(app),
                Entry = app,
                Support = app.Preset is not null ? AppSupport.Preset : mode == IsolationMode.Chromium ? AppSupport.Electron : AppSupport.Other,
                Category = AppCategories.Classify(app.Name, AppPresets.ResolveExe(app), app.PresetId),
            }, isChecked: true);
        }

        var browse = new PillButton("Browse for a program…");
        browse.Click += (_, _) => BrowseProgram();
        AddFooterButton(browse);

        Shown += async (_, _) =>
        {
            RenderGrid();
            await LoadFoundAsync();
        };
    }

    /// <summary>The app added most recently in this dialog, to select it afterwards.</summary>
    public AppEntry? LastAdded { get; private set; }

    protected override string? Validate() => null;

    AppTile AddTile(AppChoice choice, bool isChecked)
    {
        var tile = new AppTile(choice, isChecked);
        var tip = $"{choice.Name}\n{AppTile.SupportText(choice.Support)}" + (choice.ExePath is { } exe ? $"\n{exe}" : "");
        _tips.SetToolTip(tile, tip);
        tile.CheckedChanged += (_, _) => Apply(tile);
        _tiles.Add(tile);
        return tile;
    }

    /// <summary>Adds or removes the tile's app in the config and saves.</summary>
    void Apply(AppTile tile)
    {
        if (_reverting) return;
        var choice = tile.Choice;
        var apps = _store.Config.Apps;

        if (tile.Checked)
        {
            choice.Entry ??= choice.Found?.ToAppEntry();
            if (choice.Entry is null || apps.Contains(choice.Entry)) return;
            apps.Add(choice.Entry);
            LastAdded = choice.Entry;
        }
        else
        {
            var entry = choice.Entry;
            if (entry is null || !apps.Contains(entry)) return;
            var isolated = entry.Accounts.Count(a => !a.IsDefault);
            if (isolated > 0)
            {
                var answer = MessageBox.Show(this,
                    $"Remove {entry.Name} and its {entry.Accounts.Count} account(s) from AccountDeck?\n\n" +
                    "Nothing is deleted from your PC; each account's sign-in folder stays where it is.",
                    "Remove app", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (answer != DialogResult.OK)
                {
                    _reverting = true;
                    tile.Checked = true;
                    _reverting = false;
                    return;
                }
            }
            apps.Remove(entry);
            if (LastAdded == entry) LastAdded = null;
        }
        _store.Save();
    }

    async Task LoadFoundAsync()
    {
        List<FoundApp> found;
        try
        {
            found = await Task.Run(_scan);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            _status.Text = "Could not scan this PC: " + ex.Message;
            return;
        }
        if (IsDisposed) return;

        foreach (var app in found)
            AddTile(new AppChoice
            {
                Name = app.Name,
                ExePath = app.ExePath,
                Found = app,
                Support = app.Support,
                Category = AppCategories.Classify(app.Name, app.ExePath, app.Preset?.Id),
            }, isChecked: false);

        _status.Text = "Tick an app to add it, untick to remove it. Changes are saved automatically.";
        RenderGrid();
    }

    void RenderGrid()
    {
        var query = _search.Text.Trim();
        var content = _grid.Content;
        content.SuspendLayout();
        content.Controls.Clear();
        foreach (var header in _headers) header.Dispose();
        _headers.Clear();

        var visible = _tiles.Where(t => query.Length == 0 ||
                                        t.Choice.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                        (t.Choice.ExePath?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));

        foreach (var group in visible.GroupBy(t => t.Choice.Category).OrderBy(g => g.Key))
        {
            var header = GroupHeader(AppCategories.Title(group.Key), group.Count(), _grid.ContentWidth, first: _headers.Count == 0);
            _headers.Add(header);
            content.Controls.Add(header);
            foreach (var tile in group
                         .OrderByDescending(t => t.Checked)
                         .ThenBy(t => t.Choice.Support)
                         .ThenBy(t => t.Choice.Name, StringComparer.CurrentCultureIgnoreCase))
                content.Controls.Add(tile);
        }

        if (_headers.Count == 0)
        {
            var empty = new Label
            {
                Text = query.Length > 0 ? $"No app matches \"{query}\"." : "No apps yet.",
                AutoSize = true, UseMnemonic = false, ForeColor = Theme.Dim, Margin = new Padding(0, Theme.S(20), 0, 0),
            };
            _headers.Add(empty);
            content.Controls.Add(empty);
        }

        content.ResumeLayout();
        _grid.ScrollToTop();
    }

    void BrowseProgram()
    {
        using var dialog = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe", Title = "Choose a program to add" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var exe = dialog.FileName;
        var existing = _tiles.FirstOrDefault(t => string.Equals(t.Choice.ExePath, exe, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Checked = true;
            _search.Text = existing.Choice.Name;
            return;
        }

        var name = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).FileDescription is { Length: > 0 } description
            ? description
            : Path.GetFileNameWithoutExtension(exe);
        var found = new FoundApp(name, exe, AppScanner.IsElectron(exe) ? AppSupport.Electron : AppSupport.Other);
        var tile = AddTile(new AppChoice
        {
            Name = name, ExePath = exe, Found = found, Support = found.Support,
            Category = AppCategories.Classify(name, exe, null),
        }, isChecked: false);
        tile.Checked = true; // adds and saves
        _search.Text = "";
        RenderGrid();
    }

    static Control GroupHeader(string title, int count, int width, bool first)
    {
        // Full width so the flow layout starts a new row for each group.
        var header = new Panel { Width = width - Theme.S(4), Height = Theme.S(28), Margin = new Padding(0, first ? 0 : Theme.S(8), 0, Theme.S(4)) };
        var label = new Label { Text = title, AutoSize = true, UseMnemonic = false, Font = Theme.Heading, ForeColor = Theme.Text, Location = new Point(0, Theme.S(3)) };
        var pill = new Label
        {
            Text = count.ToString(), AutoSize = true, Font = Theme.Small, ForeColor = Theme.Dim, BackColor = Theme.Card,
            Padding = new Padding(Theme.S(6), Theme.S(1), Theme.S(6), Theme.S(1)),
        };
        header.Controls.Add(label);
        header.Controls.Add(pill);
        header.Layout += (_, _) => pill.Location = new Point(label.Right + Theme.S(8), Theme.S(6));
        return header;
    }

    /// <summary>"● Supported  ● Probably works  ● May not work" key for the tile dots.</summary>
    static Control Legend(int width)
    {
        var legend = new Panel { Width = width, Height = Theme.S(36), Location = new Point(Theme.S(310), 0) };
        legend.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var items = new[]
            {
                (Theme.Running, AppTile.SupportText(AppSupport.Preset)),
                (Color.FromArgb(0xD9, 0xA4, 0x41), AppTile.SupportText(AppSupport.Electron)),
                (Theme.Faint, AppTile.SupportText(AppSupport.Other)),
            };
            var x = legend.Width;
            foreach (var (color, text) in items.Reverse())
            {
                var size = TextRenderer.MeasureText(text, Theme.Small);
                x -= size.Width + Theme.S(26);
                using var brush = new SolidBrush(color);
                g.FillEllipse(brush, x, (legend.Height - Theme.S(7)) / 2f, Theme.S(7f), Theme.S(7f));
                TextRenderer.DrawText(g, text, Theme.Small, new Point(x + Theme.S(12), (legend.Height - size.Height) / 2), Theme.Dim);
            }
        };
        return legend;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tips.Dispose();
            // Tiles hidden by the search filter have no parent, so dispose them here.
            foreach (var tile in _tiles.Where(t => t.Parent is null)) tile.Dispose();
        }
        base.Dispose(disposing);
    }
}
