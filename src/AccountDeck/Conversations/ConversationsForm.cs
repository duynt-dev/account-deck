using AccountDeck.Core;
using AccountDeck.UI;

namespace AccountDeck.Conversations;

/// <summary>Browse an imported Claude export and copy a conversation into another account.</summary>
internal sealed class ConversationsForm : Form
{
    readonly AccountProfile _account;
    readonly Label _info;
    readonly SearchBox _search;
    readonly ItemList _list;
    readonly RichTextBox _view;
    readonly PillButton[] _conversationActions;
    ExportLibrary? _library;

    public ConversationsForm(AppEntry app, AccountProfile account)
    {
        _account = account;
        Theme.StyleForm(this);
        Text = $"{app.Name} conversations — {account.Name}";
        Icon = BadgeIcon.CreateIcon(BadgeIcon.ParseColor(account.Color, Theme.Accent), account.Initial);
        Size = new Size(Theme.S(1100), Theme.S(720));
        MinimumSize = new Size(Theme.S(760), Theme.S(480));
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = true;

        // Top bar.
        var top = new Panel { Dock = DockStyle.Top, Height = Theme.S(64), Padding = new Padding(Theme.S(20), Theme.S(14), Theme.S(20), 0) };
        _info = new Label { Dock = DockStyle.Fill, ForeColor = Theme.Dim, Font = Theme.Small, TextAlign = ContentAlignment.MiddleLeft };
        var topButtons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false };
        var howTo = new PillButton("How to export");
        howTo.Click += (_, _) => ShowHowTo();
        var import = new PillButton("Import export…", primary: true);
        import.Click += (_, _) => ImportExport();
        topButtons.Controls.Add(howTo);
        topButtons.Controls.Add(import);
        top.Controls.Add(_info);
        top.Controls.Add(topButtons);

        // Left: search + list.
        var left = new Panel { Dock = DockStyle.Left, Width = Theme.S(360), Padding = new Padding(Theme.S(16), Theme.S(8), Theme.S(8), Theme.S(16)) };
        _search = new SearchBox("Search titles and messages") { Dock = DockStyle.Top };
        _search.TextChanged += (_, _) => Filter();
        var gap = new Panel { Dock = DockStyle.Top, Height = Theme.S(8) };
        _list = new ItemList(58) { Dock = DockStyle.Fill, BackColor = Theme.Window, Painter = PaintRow };
        _list.SelectionChanged += (_, _) => ShowSelected();
        left.Controls.Add(_list);
        left.Controls.Add(gap);
        left.Controls.Add(_search);

        // Right: transcript + actions.
        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(8), Theme.S(8), Theme.S(20), Theme.S(16)) };
        var viewFrame = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(Theme.S(16)) };
        _view = new RichTextBox
        {
            Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None,
            BackColor = Theme.Card, ForeColor = Theme.Text, Font = Theme.Body, DetectUrls = true,
        };
        Theme.DarkScrollBars(_view);
        viewFrame.Controls.Add(_view);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, Theme.S(10), 0, 0) };
        var copyPrompt = new PillButton("Copy to continue in another account", primary: true);
        copyPrompt.Click += (_, _) => CopySelected(ClaudeExport.ToContinuationPrompt, "Copied. Paste it as the first message of a new chat in the other account.");
        var copyMd = new PillButton("Copy as Markdown");
        copyMd.Click += (_, _) => CopySelected(ClaudeExport.ToMarkdown, "Copied as Markdown.");
        var save = new PillButton("Save .md…");
        save.Click += (_, _) => SaveSelected();
        actions.Controls.Add(copyPrompt);
        actions.Controls.Add(copyMd);
        actions.Controls.Add(save);
        _conversationActions = new[] { copyPrompt, copyMd, save };

        right.Controls.Add(viewFrame);
        right.Controls.Add(actions);

        Controls.Add(right);
        Controls.Add(left);
        Controls.Add(top);

        LoadLibrary();
    }

    ExportConversation? Selected => _list.SelectedItem as ExportConversation;

    void LoadLibrary()
    {
        try
        {
            _library = ClaudeExport.Load(_account);
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            _library = null;
            MessageBox.Show(this, "The saved export could not be read: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        _info.Text = _library is null
            ? "No export imported yet. Claude keeps chats on its servers, so request a data export from this account, then import the .zip here."
            : $"{_library.Conversations.Count} conversations" +
              (_library.AccountEmail is { } email ? $" · {email}" : "") +
              $" · imported {_library.ImportedAt:yyyy-MM-dd HH:mm}";
        Filter();
    }

    void Filter()
    {
        var query = _search.Text.Trim().ToLowerInvariant();
        var items = _library?.Conversations.Where(c => query.Length == 0 || c.SearchText.Contains(query)) ?? Enumerable.Empty<ExportConversation>();
        _list.SetItems(items, Selected);
        ShowSelected();
    }

    void ShowSelected()
    {
        foreach (var button in _conversationActions) button.Enabled = Selected is not null;
        _view.Clear();
        if (Selected is not { } conversation)
        {
            _view.SelectionColor = Theme.Dim;
            _view.AppendText(_library is null ? "Import an export to browse its conversations." : "No conversation matches.");
            return;
        }

        _view.SuspendLayout();
        Append(conversation.Title + "\n", Theme.Title, Theme.Text);
        if (conversation.Created is { } created)
            Append($"Started {created.LocalDateTime:yyyy-MM-dd HH:mm} · {conversation.Messages.Count} messages\n\n", Theme.Small, Theme.Dim);

        foreach (var message in conversation.Messages)
        {
            var color = message.Sender == "You" ? Theme.Accent : Color.FromArgb(0x8A, 0xB4, 0xF8);
            var when = message.At is { } at ? $"   {at.LocalDateTime:yyyy-MM-dd HH:mm}" : "";
            Append(message.Sender, Theme.BodyBold, color);
            Append(when + "\n", Theme.Small, Theme.Faint);
            Append(message.Text.Trim() + "\n", Theme.Body, Theme.Text);
            foreach (var file in message.Files) Append($"📎 {file}\n", Theme.Small, Theme.Dim);
            Append("\n", Theme.Body, Theme.Text);
        }
        _view.SelectionStart = 0;
        _view.ScrollToCaret();
        _view.ResumeLayout();
    }

    void Append(string text, Font font, Color color)
    {
        _view.SelectionStart = _view.TextLength;
        _view.SelectionLength = 0;
        _view.SelectionFont = font;
        _view.SelectionColor = color;
        _view.AppendText(text);
    }

    void ImportExport()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose the Claude data export",
            Filter = "Claude export (*.zip;conversations.json)|*.zip;conversations.json|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            ClaudeExport.Import(_account, dialog.FileName);
            LoadLibrary();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Import failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    void CopySelected(Func<ExportConversation, string> format, string done)
    {
        if (Selected is not { } conversation) return;
        Clipboard.SetText(format(conversation));
        _info.Text = done;
    }

    void SaveSelected()
    {
        if (Selected is not { } conversation) return;
        using var dialog = new SaveFileDialog
        {
            FileName = AppPaths.SafeFileName(conversation.Title) + ".md",
            Filter = "Markdown (*.md)|*.md",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            File.WriteAllText(dialog.FileName, ClaudeExport.ToMarkdown(conversation));
    }

    void ShowHowTo() => MessageBox.Show(this,
        "Claude stores chats on its servers, not on this PC, so they are read from Claude's official data export:\n\n" +
        "1. Open this account in Claude.\n" +
        "2. Go to Settings → Privacy → Export data.\n" +
        "3. Claude e-mails you a download link; save the .zip.\n" +
        "4. Click \"Import export…\" here and pick that .zip.\n\n" +
        "Then select a conversation and use \"Copy to continue in another account\" to carry it over.",
        "How to export", MessageBoxButtons.OK, MessageBoxIcon.Information);

    void PaintRow(Graphics g, Rectangle bounds, object item, bool selected, bool hover)
    {
        var c = (ExportConversation)item;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        if (selected || hover)
            PaintedControl.FillRound(g, new RectangleF(bounds.X, bounds.Y + Theme.S(2f), bounds.Width - Theme.S(2f), bounds.Height - Theme.S(4f)),
                Theme.S(8f), selected ? Theme.Selected : Theme.CardHover);

        var flags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
        var x = bounds.X + Theme.S(12);
        var width = bounds.Width - Theme.S(24);
        TextRenderer.DrawText(g, c.Title, Theme.BodyBold, new Rectangle(x, bounds.Y + Theme.S(10), width, Theme.S(20)), Theme.Text, flags);
        var date = (c.Updated ?? c.Created)?.LocalDateTime.ToString("yyyy-MM-dd") ?? "";
        TextRenderer.DrawText(g, $"{date}  ·  {c.Messages.Count} messages", Theme.Small,
            new Rectangle(x, bounds.Y + Theme.S(32), width, Theme.S(18)), Theme.Dim, flags);
    }
}
