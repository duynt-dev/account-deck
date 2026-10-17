using AccountDeck.Core;

namespace AccountDeck.UI;

/// <summary>Shared layout for the dark edit dialogs: a column of fields and an OK/Cancel row.</summary>
internal abstract class DialogBase : Form
{
    protected readonly FlowLayoutPanel Fields;

    /// <summary>Panel the Add* helpers append to; defaults to <see cref="Fields"/>.</summary>
    protected FlowLayoutPanel Target;
    protected readonly PillButton Ok;
    protected readonly int FieldWidth = Theme.S(440);

    protected DialogBase(string title, string okText)
    {
        Theme.StyleForm(this);
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(Theme.S(22), Theme.S(12), Theme.S(22), Theme.S(18));

        var root = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
        };
        Fields = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
        };

        Target = Fields;
        Ok = new PillButton(okText, primary: true);
        var cancel = new PillButton("Cancel") { DialogResult = DialogResult.Cancel };
        Ok.Click += (_, _) =>
        {
            var problem = Validate();
            if (problem is null)
            {
                DialogResult = DialogResult.OK;
                return;
            }
            MessageBox.Show(this, problem, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Width = FieldWidth,
            Margin = new Padding(0, Theme.S(20), 0, 0),
        };
        buttons.Controls.Add(Ok);
        buttons.Controls.Add(cancel);
        buttons.MinimumSize = new Size(FieldWidth, 0);

        root.Controls.Add(Fields);
        root.Controls.Add(buttons);
        Controls.Add(root);

        AcceptButton = Ok;
        CancelButton = cancel;
    }

    /// <summary>Returns an error message, or null when the input is acceptable.</summary>
    protected abstract new string? Validate();

    protected void AddCaption(string text) => Target.Controls.Add(Theme.Caption(text));

    protected TextBox AddText(string caption, string value, int lines = 1)
    {
        AddCaption(caption);
        var box = Theme.Input(value, multiline: lines > 1);
        box.Width = FieldWidth;
        if (lines > 1) box.Height = Theme.S(22) * lines;
        box.Margin = Padding.Empty;
        Target.Controls.Add(box);
        return box;
    }

    protected Label AddHint(string text)
    {
        var hint = new Label
        {
            Text = text,
            ForeColor = Theme.Faint,
            Font = Theme.Small,
            AutoSize = true,
            MaximumSize = new Size(FieldWidth, 0),
            Margin = new Padding(0, Theme.S(4), 0, 0),
        };
        Target.Controls.Add(hint);
        return hint;
    }

    /// <summary>A text box with a button on its right, e.g. "Browse…".</summary>
    protected TextBox AddTextWithButton(string caption, string value, string buttonText, Action<TextBox> onClick)
    {
        AddCaption(caption);
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        var button = new PillButton(buttonText);
        var box = Theme.Input(value);
        box.Width = FieldWidth - button.Width - Theme.S(8);
        box.Margin = new Padding(0, (button.Height - box.Height) / 2, 0, 0);
        button.Margin = new Padding(Theme.S(8), 0, 0, 0);
        button.Click += (_, _) => onClick(box);
        row.Controls.Add(box);
        row.Controls.Add(button);
        Target.Controls.Add(row);
        return box;
    }

    protected ToggleCheck AddCheck(string text, bool value)
    {
        var check = new ToggleCheck(text, value);
        Target.Controls.Add(check);
        return check;
    }

    protected static Dictionary<string, string> ParseEnv(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var eq = line.IndexOf('=');
            if (line.Length == 0 || line.StartsWith('#') || eq <= 0) continue;
            result[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        return result;
    }

    protected static string FormatEnv(IReadOnlyDictionary<string, string> env) =>
        string.Join(Environment.NewLine, env.Select(kv => $"{kv.Key}={kv.Value}"));

    protected static string? BrowseFolder(IWin32Window owner, string current)
    {
        using var dialog = new FolderBrowserDialog { UseDescriptionForTitle = true, Description = "Choose a folder", ShowNewFolderButton = true };
        if (!string.IsNullOrWhiteSpace(current)) dialog.InitialDirectory = AppPaths.Expand(current);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.SelectedPath : null;
    }
}

/// <summary>
/// Create or edit an account. The common case needs only a name; the data folder is derived
/// from it, and everything else sits in a collapsed "Advanced" section.
/// </summary>
internal sealed class AccountDialog : DialogBase
{
    readonly ConfigStore _store;
    readonly AppEntry _app;
    readonly AccountProfile _account;
    readonly bool _isNew;
    readonly TextBox _name, _note, _folder, _args, _env;
    readonly ToggleCheck _useDefault, _shortcut;
    readonly SwatchPicker _color;
    readonly Label _summary;
    readonly LinkLabel _advancedToggle;
    readonly FlowLayoutPanel _advanced;
    bool _folderTouched;

    public AccountDialog(ConfigStore store, AppEntry app, AccountProfile? existing)
        : base(existing is null ? $"New {app.Name} account" : $"Edit {existing.Name}", existing is null ? "Create" : "Save")
    {
        _store = store;
        _app = app;
        _isNew = existing is null;
        _account = existing ?? new AccountProfile { Color = store.NextColor(app.Accounts.Count) };
        _folderTouched = !_isNew;

        _name = AddText("Name", _account.Name);
        _name.PlaceholderText = "e.g. Work, Personal, Client A";
        _name.Font = Theme.Large;
        _note = AddText("Note (optional)", _account.Note);
        _note.PlaceholderText = "e.g. name@company.com";

        AddCaption("Colour");
        _color = new SwatchPicker(_account.Color);
        Fields.Controls.Add(_color);

        _summary = AddHint("");
        _summary.Margin = new Padding(0, Theme.S(14), 0, 0);

        _shortcut = AddCheck("Also create a desktop shortcut", false);
        _shortcut.Visible = _isNew;

        _advancedToggle = new LinkLabel
        {
            AutoSize = true,
            LinkColor = Theme.Dim,
            ActiveLinkColor = Theme.Accent,
            LinkBehavior = LinkBehavior.HoverUnderline,
            Font = Theme.Small,
            Margin = new Padding(0, Theme.S(14), 0, 0),
        };
        _advancedToggle.LinkClicked += (_, _) => SetAdvancedVisible(!_advanced!.Visible);
        Fields.Controls.Add(_advancedToggle);

        // Everything below is only needed for unusual setups.
        _advanced = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
        };
        Fields.Controls.Add(_advanced);
        Target = _advanced;

        _useDefault = AddCheck($"Use {app.Name}'s normal sign-in (not isolated)", !_isNew && _account.IsDefault);
        _folder = AddTextWithButton("Data folder", _account.DataDir, "Browse…", box =>
        {
            if (BrowseFolder(this, box.Text) is { } chosen) { box.Text = chosen; _folderTouched = true; }
        });
        _args = AddText("Extra arguments", _account.ExtraArgs);
        _env = AddText("Extra environment, one KEY=VALUE per line", FormatEnv(_account.Environment), lines: 3);
        if (app.PresetId == "claude")
            AddHint(@"CLAUDE_CONFIG_DIR=%USERPROFILE%\.claude-work keeps Claude Code settings separate.");
        Target = Fields;

        _folder.TextChanged += (_, _) =>
        {
            if (_folder.Focused) _folderTouched = true;
            UpdateSummary();
        };
        _name.TextChanged += (_, _) =>
        {
            if (!_folderTouched) _folder.Text = SuggestedFolder();
            UpdateSummary();
        };
        _useDefault.CheckedChanged += (_, _) =>
        {
            _folder.Enabled = !_useDefault.Checked;
            UpdateSummary();
        };

        if (_isNew) _folder.Text = SuggestedFolder();
        _folder.Enabled = !_useDefault.Checked;
        SetAdvancedVisible(!_isNew && (_account.IsDefault || _account.ExtraArgs.Length > 0 || _account.Environment.Count > 0));
        UpdateSummary();
        Shown += (_, _) => _name.Focus();
    }

    public AccountProfile Result => _account;
    public bool CreateShortcut => _isNew && _shortcut.Checked;

    string SuggestedFolder() => _store.SuggestDataDir(_app, _name.Text.Trim().Length > 0 ? _name.Text : "account");

    void SetAdvancedVisible(bool visible)
    {
        _advanced.Visible = visible;
        _advancedToggle.Text = visible ? "Hide advanced options ▴" : "Advanced options ▾";
    }

    void UpdateSummary()
    {
        _summary.Text = _useDefault.Checked
            ? $"Opens {_app.Name} with its normal sign-in."
            : $"Signs in separately. Data is kept in:\n{_folder.Text}";
    }

    protected override string? Validate()
    {
        var name = _name.Text.Trim();
        if (name.Length == 0) return "Please give the account a name.";

        var folder = _useDefault.Checked ? "" : _folder.Text.Trim();
        if (!_useDefault.Checked)
        {
            if (folder.Length == 0) folder = SuggestedFolder();
            try { _ = AppPaths.NormalizeDir(folder); }
            catch { return "The data folder path is not valid."; }
            if (ConfigStore.IsDataDirTaken(_app, folder, _account.Id)) return "Another account of this app already uses that folder.";
        }
        else if (_app.Accounts.Any(a => a.Id != _account.Id && a.IsDefault))
        {
            return $"Another account already uses {_app.Name}'s normal sign-in.";
        }

        _account.Name = name;
        _account.Note = _note.Text.Trim();
        _account.Color = _color.Value;
        _account.DataDir = folder;
        _account.ExtraArgs = _args.Text.Trim();
        _account.Environment = ParseEnv(_env.Text);
        return null;
    }
}

/// <summary>Add an app (from a preset or any .exe) or edit how it is launched.</summary>
internal sealed class AppDialog : DialogBase
{
    const string CustomChoice = "Other program…";

    readonly AppEntry _app;
    readonly bool _isNew;
    readonly DropDown? _preset;
    readonly TextBox _name, _exe, _args, _env;
    readonly DropDown _mode;
    readonly Label _modeHint;
    bool _syncing;

    public AppDialog(AppEntry? existing)
        : base(existing is null ? "Add app" : $"{existing.Name} settings", existing is null ? "Add" : "Save")
    {
        _isNew = existing is null;
        _app = existing ?? new AppEntry { Color = ConfigStore.Palette[^1] };

        if (_isNew)
        {
            AddCaption("App");
            _preset = new DropDown();
            _preset.Width = FieldWidth;
            foreach (var p in AppPresets.All)
                _preset.Items.Add(p.Locate() is null ? $"{p.Name}  (not found)" : p.Name);
            _preset.Items.Add(CustomChoice);
            _preset.Margin = Padding.Empty;
            Fields.Controls.Add(_preset);
        }

        _name = AddText("Display name", _app.Name);
        _exe = AddTextWithButton("Program", _app.ExePath, "Browse…", box =>
        {
            using var dialog = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe", Title = "Choose the app's program" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            box.Text = dialog.FileName;
            if (_name.Text.Trim().Length == 0) _name.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
        });
        AddHint("Leave empty for known apps to find them automatically.");

        AddCaption("How accounts are kept apart");
        _mode = new DropDown();
        _mode.Width = FieldWidth;
        _mode.Margin = Padding.Empty;
        foreach (var m in IsolationMode.All) _mode.Items.Add(m.Name);
        Fields.Controls.Add(_mode);
        _modeHint = AddHint("");

        _args = AddText("Arguments for isolated accounts ({dataDir} = account folder)", _app.ArgsTemplate);
        _env = AddText("Environment for isolated accounts, one KEY=VALUE per line", FormatEnv(_app.EnvTemplate), lines: 3);

        _mode.SelectedIndexChanged += (_, _) => ApplyMode();
        _mode.SelectedIndex = IsolationModeIndex(IsolationMode.Detect(_app));
        _modeHint.Text = IsolationMode.All[_mode.SelectedIndex].Hint;

        if (_preset is not null)
        {
            _preset.SelectedIndexChanged += (_, _) => ApplyPreset();
            var firstFound = AppPresets.All.ToList().FindIndex(p => p.Locate() is not null);
            _preset.SelectedIndex = firstFound >= 0 ? firstFound : _preset.Items.Count - 1;
        }
    }

    public AppEntry Result => _app;

    static int IsolationModeIndex(IsolationMode mode) => IsolationMode.All.ToList().IndexOf(mode);

    void ApplyPreset()
    {
        var preset = _preset!.SelectedIndex < AppPresets.All.Count ? AppPresets.All[_preset.SelectedIndex] : null;
        _syncing = true;
        _app.PresetId = preset?.Id;
        _name.Text = preset?.Name ?? "";
        _exe.Text = "";
        _exe.PlaceholderText = preset?.Locate() ?? "";
        _args.Text = preset?.ArgsTemplate ?? IsolationMode.Chromium.ArgsTemplate;
        _env.Text = preset is null ? "" : FormatEnv(preset.Env);
        _app.Color = preset?.Color ?? ConfigStore.Palette[^1];
        _mode.SelectedIndex = IsolationModeIndex(preset is null ? IsolationMode.Chromium
            : IsolationMode.Detect(new AppEntry { ArgsTemplate = preset.ArgsTemplate, EnvTemplate = new(preset.Env) }));
        _syncing = false;
    }

    void ApplyMode()
    {
        var mode = IsolationMode.All[_mode.SelectedIndex];
        _modeHint.Text = mode.Hint;
        if (_syncing || mode == IsolationMode.Custom) return;
        _args.Text = mode.ArgsTemplate;
        _env.Text = FormatEnv(mode.Env);
    }

    protected override string? Validate()
    {
        var name = _name.Text.Trim();
        if (name.Length == 0) return "Please give the app a name.";

        var exe = _exe.Text.Trim();
        if (exe.Length > 0 && !File.Exists(AppPaths.Expand(exe))) return "That program file does not exist.";
        if (exe.Length == 0 && _app.Preset?.Locate() is null) return "Choose the app's program (.exe).";

        var env = ParseEnv(_env.Text);
        var args = _args.Text.Trim();
        if (!args.Contains("{dataDir}", StringComparison.OrdinalIgnoreCase) &&
            !env.Values.Any(v => v.Contains("{dataDir}", StringComparison.OrdinalIgnoreCase)))
            return "Arguments or environment must use {dataDir}, otherwise accounts would share the same data.";

        AppIcons.Forget(_app);
        _app.Name = name;
        _app.ExePath = exe;
        _app.ArgsTemplate = args;
        _app.EnvTemplate = env;
        if (_isNew)
            _app.Accounts.Add(new AccountProfile { Name = "Default", Note = "Normal sign-in", Color = _app.Color });
        return null;
    }
}

/// <summary>Shows installed apps that are not in AccountDeck yet, grouped by how well they support several accounts.</summary>
internal sealed class ScanAppsDialog : DialogBase
{
    readonly List<AppChoiceRow> _rows = new();

    static readonly (AppSupport Support, string Title, string Hint)[] Groups =
    {
        (AppSupport.Preset, "Supported", "Known to run several accounts side by side."),
        (AppSupport.Electron, "Probably works", "Built on Electron, which usually accepts a separate data folder per account."),
        (AppSupport.Other, "Other apps — may not work",
            "Each account gets its own AppData folder. Some apps still allow only one copy at a time, and launchers " +
            "or games with anti-cheat (Steam, Riot Client) may refuse."),
    };

    public ScanAppsDialog(IReadOnlyList<FoundApp> found) : base("Apps found on this PC", "Add selected")
    {
        if (found.Count == 0)
        {
            AddHint("Every app found on this PC is already in AccountDeck. Use \"Add app\" to add any other program by hand.");
            Ok.Text = "Close";
            Ok.Size = Ok.GetPreferredSize(Size.Empty);
            return;
        }

        var scroll = new ScrollView
        {
            Width = FieldWidth + Theme.S(30),
            Height = Math.Min(Theme.S(460), Theme.S(70) * found.Count + Theme.S(90) * Groups.Length),
            Margin = new Padding(0, Theme.S(4), 0, 0),
        };
        var rowWidth = scroll.ContentWidth - Theme.S(2);

        foreach (var (support, title, hint) in Groups)
        {
            var apps = found.Where(f => f.Support == support).ToList();
            if (apps.Count == 0) continue;

            var rows = apps.Select(app => new AppChoiceRow(app, support == AppSupport.Preset, rowWidth)).ToList();
            scroll.Content.Controls.Add(GroupHeader(title, apps.Count, rows, rowWidth, first: scroll.Content.Controls.Count == 0));
            scroll.Content.Controls.Add(new Label
            {
                Text = hint,
                AutoSize = true,
                MaximumSize = new Size(rowWidth, 0),
                ForeColor = Theme.Faint,
                Font = Theme.Small,
                Margin = new Padding(0, 0, 0, Theme.S(8)),
            });
            foreach (var row in rows) scroll.Content.Controls.Add(row);
            _rows.AddRange(rows);
        }
        Target.Controls.Add(scroll);
    }

    public IReadOnlyList<FoundApp> Selected => _rows.Where(r => r.Checked).Select(r => r.App).ToList();

    protected override string? Validate() => null;

    /// <summary>Group title, a count pill, and a "select all / none" link.</summary>
    static Control GroupHeader(string title, int count, List<AppChoiceRow> rows, int width, bool first)
    {
        var header = new Panel { Width = width, Height = Theme.S(30), Margin = new Padding(0, first ? 0 : Theme.S(18), 0, Theme.S(2)) };
        var titleLabel = new Label { Text = title, AutoSize = true, Font = Theme.Heading, ForeColor = Theme.Text, Location = new Point(0, Theme.S(4)) };
        var countLabel = new Label
        {
            Text = count.ToString(), AutoSize = true, Font = Theme.Small, ForeColor = Theme.Dim, BackColor = Theme.Card,
            Padding = new Padding(Theme.S(6), Theme.S(1), Theme.S(6), Theme.S(1)),
        };
        var toggle = new LinkLabel
        {
            AutoSize = true, Font = Theme.Small, LinkColor = Theme.Accent, ActiveLinkColor = Theme.AccentHover,
            LinkBehavior = LinkBehavior.HoverUnderline,
        };
        void SyncToggle() => toggle.Text = rows.All(r => r.Checked) ? "Select none" : "Select all";
        toggle.LinkClicked += (_, _) =>
        {
            var check = !rows.All(r => r.Checked);
            foreach (var row in rows) row.Checked = check;
            SyncToggle();
        };
        foreach (var row in rows) row.Click += (_, _) => SyncToggle();
        SyncToggle();

        header.Controls.Add(titleLabel);
        header.Controls.Add(countLabel);
        header.Controls.Add(toggle);
        header.Layout += (_, _) =>
        {
            countLabel.Location = new Point(titleLabel.Right + Theme.S(8), Theme.S(7));
            toggle.Location = new Point(width - toggle.Width - Theme.S(4), Theme.S(8));
        };
        return header;
    }
}

/// <summary>Asks which Claude account a downloaded data export belongs to.</summary>
internal sealed class ImportTargetDialog : DialogBase
{
    readonly DropDown _accounts;
    readonly IReadOnlyList<(AppEntry App, AccountProfile Account)> _options;

    public ImportTargetDialog(string zipPath, string? email, IReadOnlyList<(AppEntry App, AccountProfile Account)> options)
        : base("Claude export found", "Import")
    {
        _options = options;
        AddHint($"File: {Path.GetFileName(zipPath)}" + (email is null ? "" : $"\nExported from: {email}"));
        AddCaption("Which account does it belong to?");
        _accounts = new DropDown();
        _accounts.Width = FieldWidth;
        _accounts.Margin = Padding.Empty;
        foreach (var (app, account) in options)
            _accounts.Items.Add(account.Note.Length > 0 ? $"{app.Name} · {account.Name}  ({account.Note})" : $"{app.Name} · {account.Name}");
        _accounts.SelectedIndex = 0;
        Target.Controls.Add(_accounts);
        AddHint("Tip: put the account's e-mail in its Note and future exports are imported automatically.");
    }

    public (AppEntry App, AccountProfile Account) Selected => _options[_accounts.SelectedIndex];

    protected override string? Validate() => null;
}
