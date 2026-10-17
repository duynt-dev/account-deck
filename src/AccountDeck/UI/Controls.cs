using System.Drawing.Drawing2D;
using AccountDeck.Core;

namespace AccountDeck.UI;

/// <summary>Base for flicker-free owner-drawn controls that blend with their parent's background.</summary>
internal abstract class PaintedControl : Control
{
    protected bool Hover;
    protected bool Pressed;

    protected PaintedControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        ForeColor = Theme.Text;
        Font = Theme.Body;
    }

    protected Color ParentBack => Parent?.BackColor is { A: > 0 } c ? c : Theme.Window;

    protected Graphics Prepare(PaintEventArgs e)
    {
        e.Graphics.Clear(ParentBack);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        return e.Graphics;
    }

    protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hover = false; Pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { Pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { Pressed = false; Invalidate(); base.OnMouseUp(e); }

    internal static void FillRound(Graphics g, RectangleF r, float radius, Color color)
    {
        using var path = BadgeIcon.RoundedRect(r, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    internal static void StrokeRound(Graphics g, RectangleF r, float radius, Color color, float width = 1f)
    {
        using var path = BadgeIcon.RoundedRect(r, radius);
        using var pen = new Pen(color, width);
        g.DrawPath(pen, path);
    }
}

/// <summary>Rounded button; <see cref="Primary"/> uses the accent colour.</summary>
internal sealed class PillButton : PaintedControl, IButtonControl
{
    public PillButton(string text, bool primary = false)
    {
        Text = text;
        Primary = primary;
        Cursor = Cursors.Hand;
        TabStop = true;
        Font = Theme.BodyBold;
        Size = GetPreferredSize(Size.Empty);
        Margin = new Padding(Theme.S(6), 0, 0, 0);
    }

    public bool Primary { get; set; }
    public DialogResult DialogResult { get; set; }

    public void NotifyDefault(bool value) { }
    public void PerformClick() => OnClick(EventArgs.Empty);

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font);
        return new Size(text.Width + Theme.S(32), Theme.S(34));
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (DialogResult != DialogResult.None && FindForm() is { } form) form.DialogResult = DialogResult;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space) PerformClick();
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        var radius = Theme.S(8f);
        if (Primary)
        {
            FillRound(g, r, radius, !Enabled ? Theme.Faint : Hover ? Theme.AccentHover : Theme.Accent);
        }
        else
        {
            FillRound(g, r, radius, Pressed ? Theme.Selected : Hover ? Theme.CardHover : Theme.Card);
            StrokeRound(g, r, radius, Focused ? Theme.Accent : Theme.Border);
        }
        var color = Primary ? Theme.OnAccent : Enabled ? Theme.Text : Theme.Faint;
        TextRenderer.DrawText(g, Text, Font, Rectangle.Round(r), color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

/// <summary>Rounded search field with a magnifier glyph.</summary>
internal sealed class SearchBox : PaintedControl
{
    public readonly TextBox Box;

    public SearchBox(string placeholder)
    {
        Box = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.InputBack,
            ForeColor = Theme.Text,
            Font = Theme.Body,
            PlaceholderText = placeholder,
        };
        Box.GotFocus += (_, _) => Invalidate();
        Box.LostFocus += (_, _) => Invalidate();
        Box.TextChanged += (_, e) => OnTextChanged(e);
        Controls.Add(Box);
        Height = Theme.S(36);
        Cursor = Cursors.IBeam;
    }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => Box?.Text ?? "";
        set { if (Box is not null) Box.Text = value; }
    }

    protected override void OnClick(EventArgs e) { Box.Focus(); base.OnClick(e); }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (Box is null) return; // the base constructor lays out before Box exists
        var left = Theme.S(34);
        Box.SetBounds(left, (Height - Box.Height) / 2, Math.Max(10, Width - left - Theme.S(12)), Box.Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        FillRound(g, r, Theme.S(9f), Theme.InputBack);
        StrokeRound(g, r, Theme.S(9f), Box.Focused ? Theme.Accent : Theme.Border);

        var c = Theme.S(15f);
        var cy = Height / 2f;
        using var pen = new Pen(Theme.Dim, Theme.S(1.6f));
        g.DrawEllipse(pen, c - Theme.S(5f), cy - Theme.S(6f), Theme.S(10f), Theme.S(10f));
        g.DrawLine(pen, c + Theme.S(3.5f), cy + Theme.S(2.5f), c + Theme.S(7f), cy + Theme.S(6f));
    }
}

/// <summary>Card for one account: badge, name, note, status and an overflow menu.</summary>
internal sealed class AccountCard : PaintedControl
{
    public AccountCard(AppEntry app, AccountProfile account, int index)
    {
        App = app;
        Account = account;
        Index = index;
        Cursor = Cursors.Hand;
        Size = new Size(Theme.S(286), Theme.S(98));
        Margin = new Padding(0, 0, Theme.S(12), Theme.S(12));
    }

    public AppEntry App { get; }
    public AccountProfile Account { get; }
    public int Index { get; }

    bool _running;
    public bool Running
    {
        get => _running;
        set { if (_running != value) { _running = value; Invalidate(); } }
    }

    public event EventHandler? OpenRequested;
    public event EventHandler<Point>? MenuRequested;

    Rectangle MoreGlyph => new(Width - Theme.S(40), Theme.S(10), Theme.S(28), Theme.S(28));

    protected override void OnMouseMove(MouseEventArgs e) { Invalidate(MoreGlyph); base.OnMouseMove(e); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right || (e.Button == MouseButtons.Left && MoreGlyph.Contains(e.Location)))
            MenuRequested?.Invoke(this, e.Location);
        else if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))
            OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        var radius = Theme.S(12f);
        FillRound(g, r, radius, Pressed ? Theme.Selected : Hover ? Theme.CardHover : Theme.Card);
        StrokeRound(g, r, radius, Hover ? Theme.Faint : Theme.Border);

        var accent = BadgeIcon.ParseColor(Account.Color, Theme.Accent);
        var badge = Theme.S(48f);
        BadgeIcon.Paint(g, new RectangleF(Theme.S(16f), (Height - badge) / 2f, badge, badge), accent, Account.Initial);

        var x = Theme.S(78);
        var width = Width - x - Theme.S(44);
        var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        TextRenderer.DrawText(g, Account.Name, Theme.Heading, new Rectangle(x, Theme.S(17), width, Theme.S(22)), Theme.Text, flags);

        var note = Account.Note.Length > 0 ? Account.Note : Account.IsDefault ? "Normal sign-in" : "Isolated account";
        TextRenderer.DrawText(g, note, Theme.Small, new Rectangle(x, Theme.S(41), width, Theme.S(18)), Theme.Dim, flags);

        // Status line.
        var dotY = Theme.S(70f);
        using (var dot = new SolidBrush(Running ? Theme.Running : Theme.Faint))
            g.FillEllipse(dot, x, dotY, Theme.S(7f), Theme.S(7f));
        var status = Running ? "Running" : "Not running";
        TextRenderer.DrawText(g, status, Theme.Small, new Point(x + Theme.S(12), Theme.S(64)), Running ? Theme.Running : Theme.Faint);

        if (Index < 9)
        {
            var hint = $"Alt+{Index + 1}";
            var size = TextRenderer.MeasureText(hint, Theme.Small);
            TextRenderer.DrawText(g, hint, Theme.Small, new Point(Width - size.Width - Theme.S(14), Theme.S(64)), Theme.Faint);
        }

        // "More" glyph: three dots, highlighted on hover.
        var more = MoreGlyph;
        if (Hover && more.Contains(PointToClient(Cursor.Position)))
            FillRound(g, more, Theme.S(6f), Theme.Selected);
        using var dots = new SolidBrush(Hover ? Theme.Text : Theme.Dim);
        var d = Theme.S(3.6f);
        for (var i = -1; i <= 1; i++)
            g.FillEllipse(dots, more.X + more.Width / 2f + i * Theme.S(6f) - d / 2, more.Y + more.Height / 2f - d / 2, d, d);
    }
}

/// <summary>Dashed "add" tile placed after the account cards.</summary>
internal sealed class AddTile : PaintedControl
{
    public AddTile(string text)
    {
        Text = text;
        Cursor = Cursors.Hand;
        Size = new Size(Theme.S(286), Theme.S(98));
        Margin = new Padding(0, 0, Theme.S(12), Theme.S(12));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e);
        var r = new RectangleF(1, 1, Width - 3, Height - 3);
        if (Hover) FillRound(g, r, Theme.S(12f), Theme.Card);
        using (var path = BadgeIcon.RoundedRect(r, Theme.S(12f)))
        using (var pen = new Pen(Hover ? Theme.Accent : Theme.Border, Theme.S(1.4f)) { DashStyle = DashStyle.Dash })
            g.DrawPath(pen, path);

        var color = Hover ? Theme.Accent : Theme.Dim;
        var text = "+  " + Text;
        TextRenderer.DrawText(g, text, Theme.BodyBold, ClientRectangle, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>Scrollable, owner-painted list with hover and keyboard selection.</summary>
internal sealed class ItemList : PaintedControl
{
    readonly List<object> _items = new();
    int _hover = -1;
    int _scroll;

    public ItemList(int itemHeight)
    {
        ItemHeight = Theme.S(itemHeight);
        TabStop = true;
    }

    public int ItemHeight { get; }
    public int SelectedIndex { get; private set; } = -1;
    public object? SelectedItem => SelectedIndex >= 0 && SelectedIndex < _items.Count ? _items[SelectedIndex] : null;
    public IReadOnlyList<object> Items => _items;

    /// <summary>Paints one row: graphics, bounds, item, selected, hovered.</summary>
    public Action<Graphics, Rectangle, object, bool, bool>? Painter { get; set; }

    public event EventHandler? SelectionChanged;
    public event EventHandler? ItemActivated;
    public event EventHandler<Point>? ItemMenuRequested;

    public void SetItems(IEnumerable<object> items, object? keepSelected = null)
    {
        _items.Clear();
        _items.AddRange(items);
        var index = keepSelected is null ? -1 : _items.IndexOf(keepSelected);
        _scroll = Math.Clamp(_scroll, 0, MaxScroll);
        Select(index >= 0 ? index : _items.Count > 0 ? 0 : -1, raise: true);
        Invalidate();
    }

    public void Select(int index, bool raise = true)
    {
        index = _items.Count == 0 ? -1 : Math.Clamp(index, 0, _items.Count - 1);
        var changed = index != SelectedIndex;
        SelectedIndex = index;
        EnsureVisible(index);
        Invalidate();
        if (changed && raise) SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MoveBy(int delta) => Select(SelectedIndex < 0 ? 0 : SelectedIndex + delta);

    public void Activate()
    {
        if (SelectedItem is not null) ItemActivated?.Invoke(this, EventArgs.Empty);
    }

    int MaxScroll => Math.Max(0, _items.Count * ItemHeight - Height);

    void EnsureVisible(int index)
    {
        if (index < 0) return;
        var top = index * ItemHeight;
        if (top < _scroll) _scroll = top;
        else if (top + ItemHeight > _scroll + Height) _scroll = top + ItemHeight - Height;
    }

    int IndexAt(Point p)
    {
        var i = (p.Y + _scroll) / ItemHeight;
        return i >= 0 && i < _items.Count ? i : -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e);
        if (Painter is null) return;
        var first = _scroll / ItemHeight;
        for (var i = first; i < _items.Count; i++)
        {
            var bounds = new Rectangle(0, i * ItemHeight - _scroll, Width, ItemHeight);
            if (bounds.Top > Height) break;
            Painter(g, bounds, _items[i], i == SelectedIndex, i == _hover);
        }

        // Slim scroll indicator, only when the list overflows.
        if (MaxScroll > 0)
        {
            var total = _items.Count * ItemHeight;
            var size = Math.Max(Theme.S(30f), Height * (float)Height / total);
            var top = (Height - size) * _scroll / MaxScroll;
            FillRound(g, new RectangleF(Width - Theme.S(5f), top, Theme.S(3f), size), Theme.S(1.5f), Theme.Border);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var i = IndexAt(e.Location);
        if (i != _hover) { _hover = i; Invalidate(); }
        Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = -1;
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var i = IndexAt(e.Location);
        if (i < 0) return;
        Select(i);
        if (e.Button == MouseButtons.Right) ItemMenuRequested?.Invoke(this, e.Location);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left && IndexAt(e.Location) == SelectedIndex) Activate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        _scroll = Math.Clamp(_scroll - Math.Sign(e.Delta) * ItemHeight, 0, MaxScroll);
        Invalidate();
        base.OnMouseWheel(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Up: MoveBy(-1); e.Handled = true; break;
            case Keys.Down: MoveBy(1); e.Handled = true; break;
            case Keys.Enter: Activate(); e.Handled = true; break;
        }
        base.OnKeyDown(e);
    }

    protected override void OnResize(EventArgs e)
    {
        _scroll = Math.Clamp(_scroll, 0, MaxScroll);
        base.OnResize(e);
    }
}

/// <summary>Row of colour swatches for picking an account colour.</summary>
internal sealed class SwatchPicker : PaintedControl
{
    public SwatchPicker(string selected)
    {
        Value = selected;
        Size = new Size(Theme.S(30) * ConfigStore.Palette.Length, Theme.S(30));
        Cursor = Cursors.Hand;
    }

    public string Value { get; private set; }

    public event EventHandler? ValueChanged;

    protected override void OnMouseClick(MouseEventArgs e)
    {
        var i = e.X / Theme.S(30);
        if (i >= 0 && i < ConfigStore.Palette.Length)
        {
            Value = ConfigStore.Palette[i];
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
        base.OnMouseClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e);
        var cell = Theme.S(30f);
        for (var i = 0; i < ConfigStore.Palette.Length; i++)
        {
            var hex = ConfigStore.Palette[i];
            var r = new RectangleF(i * cell + Theme.S(4f), Theme.S(4f), cell - Theme.S(8f), cell - Theme.S(8f));
            using (var brush = new SolidBrush(BadgeIcon.ParseColor(hex, Color.Gray)))
                g.FillEllipse(brush, r);
            if (hex.Equals(Value, StringComparison.OrdinalIgnoreCase))
            {
                using var pen = new Pen(Theme.Text, Theme.S(2f));
                r.Inflate(Theme.S(2.5f), Theme.S(2.5f));
                g.DrawEllipse(pen, r);
            }
        }
    }
}
