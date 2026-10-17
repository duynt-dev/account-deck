using System.Drawing.Drawing2D;
using AccountDeck.Core;

namespace AccountDeck.UI;

/// <summary>Spotlight-style palette: type part of an app or account name, press Enter.</summary>
internal sealed class QuickSwitcher : Form
{
    sealed record Entry(AppEntry App, AccountProfile Account, string Haystack);

    const int VisibleRows = 7;

    readonly DeckHost _host;
    readonly SearchBox _search;
    readonly ItemList _list;
    List<Entry> _all = new();

    public QuickSwitcher(DeckHost host)
    {
        _host = host;
        Theme.StyleForm(this);
        Text = "AccountDeck quick switch";
        BackColor = Theme.Card;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        KeyPreview = true;
        Padding = new Padding(Theme.S(12));
        Width = Theme.S(580);

        _search = new SearchBox("Switch to…  type an app or account") { Dock = DockStyle.Top, Height = Theme.S(42) };
        _search.Box.Font = Theme.Large;
        _search.TextChanged += (_, _) => Filter();

        var gap = new Panel { Dock = DockStyle.Top, Height = Theme.S(8) };
        _list = new ItemList(56) { Dock = DockStyle.Fill, BackColor = Theme.Card, Painter = PaintEntry, TabStop = false };
        _list.ItemActivated += (_, _) => OpenSelected();

        var hint = new Label
        {
            Dock = DockStyle.Bottom, Height = Theme.S(24), Font = Theme.Small, ForeColor = Theme.Faint,
            TextAlign = ContentAlignment.BottomLeft, Text = "↑ ↓ select   ·   Enter open   ·   Esc close",
        };

        Controls.Add(_list);
        Controls.Add(gap);
        Controls.Add(_search);
        Controls.Add(hint);

        HandleCreated += (_, _) => Theme.RoundCorners(Handle);
        Deactivate += (_, _) => Hide();
        _host.RunningChanged += (_, _) => _list.Invalidate();
    }

    public void Present()
    {
        _all = _host.Store.Apps
            .SelectMany(app => app.Accounts.Select(a => new Entry(app, a, $"{app.Name} {a.Name} {a.Note}".ToLowerInvariant())))
            .ToList();
        _search.Text = "";
        Filter();

        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + area.Height / 5);
        Show();
        Activate();
        _search.Box.Focus();
        _host.RefreshRunning();
    }

    void Filter()
    {
        var tokens = _search.Text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matches = _all
            .Where(e => tokens.All(t => e.Haystack.Contains(t)))
            .OrderBy(e => tokens.Length > 0 && e.Account.Name.StartsWith(tokens[0], StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ToList();
        _list.SetItems(matches);

        var rows = Math.Clamp(matches.Count, 1, VisibleRows);
        Height = Padding.Vertical + _search.Height + Theme.S(8) + rows * _list.ItemHeight + Theme.S(28);
    }

    void OpenSelected()
    {
        if (_list.SelectedItem is not Entry entry) return;
        Hide();
        _host.Open(entry.App, entry.Account, null);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Up: _list.MoveBy(-1); return true;
            case Keys.Down: _list.MoveBy(1); return true;
            case Keys.Enter: OpenSelected(); return true;
            case Keys.Escape: Hide(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    void PaintEntry(Graphics g, Rectangle bounds, object item, bool selected, bool hover)
    {
        var entry = (Entry)item;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (selected || hover)
            PaintedControl.FillRound(g, new RectangleF(bounds.X, bounds.Y + Theme.S(2f), bounds.Width, bounds.Height - Theme.S(4f)),
                Theme.S(9f), selected ? Theme.Selected : Theme.CardHover);

        var badge = Theme.S(38f);
        var badgeRect = new RectangleF(Theme.S(10f), bounds.Y + (bounds.Height - badge) / 2f, badge, badge);
        BadgeIcon.Paint(g, badgeRect, BadgeIcon.ParseColor(entry.Account.Color, Theme.Accent), entry.Account.Initial);

        // Small app icon tucked into the badge's corner.
        var mini = Theme.S(18);
        var miniRect = new Rectangle((int)(badgeRect.Right - mini + Theme.S(5)), (int)(badgeRect.Bottom - mini + Theme.S(5)), mini, mini);
        using (var ring = new SolidBrush(selected ? Theme.Selected : Theme.Card))
            g.FillEllipse(ring, Rectangle.Inflate(miniRect, Theme.S(2), Theme.S(2)));
        MainForm.DrawAppIcon(g, entry.App, miniRect);

        var left = (int)badgeRect.Right + Theme.S(18);
        var running = _host.Running.ContainsKey(entry.Account.Id);
        var right = bounds.Right - Theme.S(110);
        var flags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
        TextRenderer.DrawText(g, entry.Account.Name, Theme.Heading, new Rectangle(left, bounds.Y + Theme.S(9), right - left, Theme.S(22)), Theme.Text, flags);
        var sub = entry.Account.Note.Length > 0 ? $"{entry.App.Name}  ·  {entry.Account.Note}" : entry.App.Name;
        TextRenderer.DrawText(g, sub, Theme.Small, new Rectangle(left, bounds.Y + Theme.S(32), right - left, Theme.S(18)), Theme.Dim, flags);

        if (running)
        {
            var y = bounds.Y + bounds.Height / 2f - Theme.S(3.5f);
            using var dot = new SolidBrush(Theme.Running);
            g.FillEllipse(dot, right + Theme.S(14f), y, Theme.S(7f), Theme.S(7f));
            TextRenderer.DrawText(g, "Running", Theme.Small, new Point(right + Theme.S(26), (int)y - Theme.S(5)), Theme.Running);
        }
        else if (selected)
        {
            TextRenderer.DrawText(g, "Enter ↵", Theme.Small, new Point(right + Theme.S(36), bounds.Y + bounds.Height / 2 - Theme.S(8)), Theme.Faint);
        }
    }
}
