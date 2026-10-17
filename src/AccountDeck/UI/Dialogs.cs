using AccountDeck.Core;

namespace AccountDeck.UI;

/// <summary>Shared layout for the dark edit dialogs: a column of fields and an OK/Cancel row.</summary>
internal abstract class DialogBase : Form
{
    protected readonly FlowLayoutPanel Fields;

    /// <summary>Panel the Add* helpers append to; defaults to <see cref="Fields"/>.</summary>
    protected FlowLayoutPanel Target;
    protected readonly PillButton Ok;
    protected readonly PillButton CancelPill;
    protected readonly int FieldWidth = Theme.S(440);
    readonly FlowLayoutPanel _footerLeft;

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
        var cancel = CancelPill = new PillButton("Cancel") { DialogResult = DialogResult.Cancel };
        Ok.Click += (_, _) =>
        {
            var problem = Validate();
            if (problem is null)
            {
                DialogResult = DialogResult.OK;
                return;
            }
            // An empty message means "stay open" (e.g. the user backed out of a confirmation).
            if (problem.Length > 0) MessageBox.Show(this, problem, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };
        // Footer: extra buttons on the left, Cancel/OK on the right, spanning the dialog's width.
        _footerLeft = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        var right = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        right.Controls.Add(Ok);
        right.Controls.Add(cancel);
        var footer = new Panel { Width = FieldWidth, Height = Ok.Height, Margin = new Padding(0, Theme.S(20), 0, 0) };
        footer.Controls.Add(_footerLeft);
        footer.Controls.Add(right);
        footer.Layout += (_, _) =>
        {
            _footerLeft.Location = Point.Empty;
            right.Location = new Point(footer.Width - right.Width, 0);
        };
        Fields.SizeChanged += (_, _) => footer.Width = Math.Max(FieldWidth, Fields.Width);

        root.Controls.Add(Fields);
        root.Controls.Add(footer);
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
        var field = new TextField(value, multiline: lines > 1, lines) { Width = FieldWidth, Margin = Padding.Empty };
        Target.Controls.Add(field);
        return field.Box;
    }

    /// <summary>Adds a button on the left side of the footer.</summary>
    protected void AddFooterButton(PillButton button)
    {
        button.Margin = new Padding(0, 0, Theme.S(8), 0);
        _footerLeft.Controls.Add(button);
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
        var field = new TextField(value);
        field.Width = FieldWidth - button.Width - Theme.S(8);
        field.Margin = Padding.Empty;
        button.Height = field.Height;
        button.Margin = new Padding(Theme.S(8), 0, 0, 0);
        button.Click += (_, _) => onClick(field.Box);
        row.Controls.Add(field);
        row.Controls.Add(button);
        Target.Controls.Add(row);
        return field.Box;
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

/// <summary>Edits how an app is started: name, program, and how its accounts are kept apart.</summary>
internal sealed class AppDialog : DialogBase
{
    readonly AppEntry _app;
    readonly TextBox _name, _exe, _args, _env;
    readonly DropDown _mode;
    readonly Label _modeHint;
    readonly LinkLabel _advancedToggle;
    readonly FlowLayoutPanel _advanced;

    public AppDialog(AppEntry app) : base($"{app.Name} settings", "Save")
    {
        _app = app;

        _name = AddText("Name", app.Name);
        _exe = AddTextWithButton("Program", app.ExePath, "Browse…", box =>
        {
            using var dialog = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe", Title = "Choose the app's program" };
            if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.FileName;
        });
        if (app.Preset?.Locate() is { } found) _exe.PlaceholderText = found;
        AddHint(app.Preset is null ? "The .exe that starts the app." : "Leave empty to find it automatically.");

        AddCaption("How accounts are kept apart");
        _mode = new DropDown { Width = FieldWidth, Margin = Padding.Empty };
        foreach (var m in IsolationMode.All) _mode.Items.Add(m.Name);
        Target.Controls.Add(_mode);
        _modeHint = AddHint("");

        _advancedToggle = new LinkLabel
        {
            AutoSize = true, LinkColor = Theme.Dim, ActiveLinkColor = Theme.Accent,
            LinkBehavior = LinkBehavior.HoverUnderline, Font = Theme.Small, Margin = new Padding(0, Theme.S(14), 0, 0),
        };
        _advancedToggle.LinkClicked += (_, _) => SetAdvancedVisible(!_advanced!.Visible);
        Fields.Controls.Add(_advancedToggle);

        _advanced = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty,
        };
        Fields.Controls.Add(_advanced);
        Target = _advanced;
        _args = AddText("Arguments for isolated accounts", app.ArgsTemplate);
        _env = AddText("Environment for isolated accounts (KEY=VALUE per line)", FormatEnv(app.EnvTemplate), lines: 3);
        AddHint("{dataDir} is replaced by each account's own folder.");
        Target = Fields;

        var current = IsolationMode.Detect(app);
        _mode.SelectedIndex = IndexOf(current);
        _modeHint.Text = current.Hint;
        _mode.SelectedIndexChanged += (_, _) => ApplyMode();
        SetAdvancedVisible(current == IsolationMode.Custom);
    }

    static int IndexOf(IsolationMode mode) => IsolationMode.All.ToList().IndexOf(mode);

    void SetAdvancedVisible(bool visible)
    {
        _advanced.Visible = visible;
        _advancedToggle.Text = visible ? "Hide advanced options ▴" : "Advanced options ▾";
    }

    void ApplyMode()
    {
        var mode = IsolationMode.All[_mode.SelectedIndex];
        _modeHint.Text = mode.Hint;
        if (mode == IsolationMode.Custom)
        {
            SetAdvancedVisible(true);
            return;
        }
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
            return "The advanced options must use {dataDir}, otherwise all accounts would share the same data.";

        AppIcons.Forget(_app);
        _app.Name = name;
        _app.ExePath = exe;
        _app.ArgsTemplate = args;
        _app.EnvTemplate = env;
        return null;
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
