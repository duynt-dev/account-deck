using System.Drawing.Drawing2D;
using AccountDeck.Core;

namespace AccountDeck.UI;

/// <summary>Rounded checkbox with a label; replaces the stock CheckBox.</summary>
internal sealed class ToggleCheck : PaintedControl
{
    bool _checked;

    public ToggleCheck(string text, bool value)
    {
        Text = text;
        _checked = value;
        Cursor = Cursors.Hand;
        TabStop = true;
        Size = GetPreferredSize(Size.Empty);
        Margin = new Padding(0, Theme.S(12), 0, 0);
    }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? CheckedChanged;

    public override Size GetPreferredSize(Size proposedSize) =>
        new(Theme.S(28) + TextRenderer.MeasureText(Text, Font).Width, Theme.S(26));

    protected override void OnTextChanged(EventArgs e)
    {
        Size = GetPreferredSize(Size.Empty);
        base.OnTextChanged(e);
    }

    protected override void OnClick(EventArgs e)
    {
        if (Enabled) Checked = !Checked;
        Focus();
        base.OnClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space) Checked = !Checked;
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e);
        var size = Theme.S(18f);
        DrawBox(g, new RectangleF(1, (Height - size) / 2f, size, size), Checked, Hover || Focused, Enabled);
        TextRenderer.DrawText(g, Text, Font, new Point(Theme.S(28), (Height - Font.Height) / 2), Enabled ? Theme.Text : Theme.Faint);
    }

    public static void DrawBox(Graphics g, RectangleF r, bool on, bool hot, bool enabled = true)
    {
        var radius = r.Width * 0.28f;
        if (on)
        {
            FillRound(g, r, radius, enabled ? Theme.Accent : Theme.Faint);
            using var pen = new Pen(Theme.OnAccent, r.Width * 0.13f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(pen, new[]
            {
                new PointF(r.Left + r.Width * 0.25f, r.Top + r.Height * 0.52f),
                new PointF(r.Left + r.Width * 0.43f, r.Top + r.Height * 0.70f),
                new PointF(r.Left + r.Width * 0.76f, r.Top + r.Height * 0.32f),
            });
        }
        else
        {
            FillRound(g, r, radius, Theme.InputBack);
            StrokeRound(g, r, radius, hot && enabled ? Theme.Accent : Theme.Faint, Theme.S(1.4f));
        }
    }
}

/// <summary>Dark drop-down list; opens a themed menu instead of the stock ComboBox list.</summary>
internal sealed class DropDown : PaintedControl
{
    int _index = -1;
    bool _open;

    public DropDown()
    {
        Height = Theme.S(34);
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    public List<string> Items { get; } = new();

    public int SelectedIndex
    {
        get => _index;
        set
        {
            if (_index == value) return;
            _index = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? SelectedIndexChanged;

    protected override void OnClick(EventArgs e)
    {
        Focus();
        ShowList();
        base.OnClick(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Up when _index > 0: SelectedIndex = _index - 1; break;
            case Keys.Down when _index < Items.Count - 1: SelectedIndex = _index + 1; break;
            case Keys.Space or Keys.F4: ShowList(); break;
        }
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    void ShowList()
    {
        var menu = Theme.Menu();
        menu.ShowImageMargin = false;
        menu.ShowCheckMargin = true;
        menu.MinimumSize = new Size(Width, 0);
        for (var i = 0; i < Items.Count; i++)
        {
            var index = i;
            var item = Theme.Item(Items[i], () => SelectedIndex = index);
            item.Checked = i == _index;
            menu.Items.Add(item);
        }
        menu.Closed += (_, _) =>
        {
            _open = false;
            Invalidate();
            BeginInvoke(menu.Dispose);
        };
        _open = true;
        Invalidate();
        menu.Show(this, new Point(0, Height + Theme.S(4)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        FillRound(g, r, Theme.S(8f), Hover ? Theme.Card : Theme.InputBack);
        StrokeRound(g, r, Theme.S(8f), _open || Focused ? Theme.Accent : Hover ? Theme.Faint : Theme.Border);

        var text = _index >= 0 && _index < Items.Count ? Items[_index] : "";
        TextRenderer.DrawText(g, text, Font, new Rectangle(Theme.S(12), 0, Width - Theme.S(44), Height), Enabled ? Theme.Text : Theme.Faint,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

        // Chevron, pointing up while the list is open.
        var cx = Width - Theme.S(18f);
        var cy = Height / 2f + (_open ? -Theme.S(1.5f) : Theme.S(1.5f));
        var d = Theme.S(4f) * (_open ? -1 : 1);
        using var pen = new Pen(Theme.Dim, Theme.S(1.6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLines(pen, new[] { new PointF(cx - Theme.S(4f), cy - d), new PointF(cx, cy), new PointF(cx + Theme.S(4f), cy - d) });
    }
}

/// <summary>Vertically scrolling container with a slim, themed scroll bar. Add children to <see cref="Content"/>.</summary>
internal sealed class ScrollView : PaintedControl
{
    readonly int _barArea = Theme.S(14);
    int _offset;
    int? _dragFrom;
    int _dragStartOffset;
    bool _barHot;

    public ScrollView(bool wrap = false)
    {
        Content = new FlowLayoutPanel
        {
            FlowDirection = wrap ? FlowDirection.LeftToRight : FlowDirection.TopDown,
            WrapContents = wrap,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
            Location = Point.Empty,
        };
        Content.SizeChanged += (_, _) => Apply();
        Content.MouseWheel += (_, e) => ScrollBy(e);
        Controls.Add(Content);
    }

    public FlowLayoutPanel Content { get; }

    /// <summary>Width available to children; the scroll bar keeps the rest.</summary>
    public int ContentWidth => Math.Max(0, Width - _barArea);

    public void ScrollToTop()
    {
        _offset = 0;
        Apply();
    }

    int Max => Math.Max(0, Content.Height - Height);

    void Apply()
    {
        _offset = Math.Clamp(_offset, 0, Max);
        Content.Location = new Point(0, -_offset);
        Invalidate();
    }

    void ScrollBy(MouseEventArgs e)
    {
        _offset -= e.Delta * Theme.S(64) / 120;
        Apply();
        if (e is HandledMouseEventArgs handled) handled.Handled = true;
    }

    // The parent's colour is often only known once the whole window is assembled, so follow it.
    protected override void OnParentChanged(EventArgs e)
    {
        SyncContentBack();
        base.OnParentChanged(e);
    }

    protected override void OnParentBackColorChanged(EventArgs e)
    {
        SyncContentBack();
        base.OnParentBackColorChanged(e);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        SyncContentBack();
        base.OnHandleCreated(e);
    }

    void SyncContentBack() => Content.BackColor = ParentBack;

    protected override void OnResize(EventArgs e)
    {
        if (Content.WrapContents)
        {
            Content.MaximumSize = new Size(ContentWidth, 0);
            Content.MinimumSize = new Size(ContentWidth, 0);
        }
        Apply();
        base.OnResize(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ScrollBy(e);
        base.OnMouseWheel(e);
    }

    RectangleF Thumb()
    {
        var track = Height - Theme.S(8f);
        var size = Math.Max(Theme.S(36f), track * Height / Math.Max(1f, Content.Height));
        var top = Theme.S(4f) + (Max == 0 ? 0 : (track - size) * _offset / Max);
        var width = _barHot || _dragFrom is not null ? Theme.S(7f) : Theme.S(4f);
        return new RectangleF(Width - Theme.S(5f) - width, top, width, size);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragFrom is { } from && Max > 0)
        {
            var track = Height - Theme.S(8f) - Thumb().Height;
            _offset = _dragStartOffset + (int)((e.Y - from) * Max / Math.Max(1f, track));
            Apply();
        }
        var hot = e.X >= Width - _barArea;
        if (hot != _barHot) { _barHot = hot; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && Max > 0 && e.X >= Width - _barArea)
        {
            var thumb = Thumb();
            if (e.Y < thumb.Top || e.Y > thumb.Bottom)
            {
                // A click on the track jumps by one page.
                _offset += e.Y < thumb.Top ? -Height : Height;
                Apply();
            }
            _dragFrom = e.Y;
            _dragStartOffset = _offset;
            Capture = true;
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragFrom = null;
        Capture = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _barHot = false;
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e);
        if (Max == 0) return;
        var thumb = Thumb();
        FillRound(g, thumb, thumb.Width / 2, _dragFrom is not null ? Theme.Dim : _barHot ? Theme.Faint : Theme.Border);
    }
}

/// <summary>Rounded text input that matches the search box; the real TextBox is <see cref="Box"/>.</summary>
internal sealed class TextField : PaintedControl
{
    public TextField(string text, bool multiline = false, int lines = 1)
    {
        Box = new TextBox
        {
            Text = text,
            Multiline = multiline,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.InputBack,
            ForeColor = Theme.Text,
            Font = Theme.Body,
            ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
        };
        if (multiline) Theme.DarkScrollBars(Box);
        Box.GotFocus += (_, _) => Invalidate();
        Box.LostFocus += (_, _) => Invalidate();
        Box.EnabledChanged += (_, _) => { Box.BackColor = Box.Enabled ? Theme.InputBack : Theme.Window; Invalidate(); };
        Box.FontChanged += (_, _) => FitHeight();
        Controls.Add(Box);
        _lines = Math.Max(1, lines);
        Cursor = Cursors.IBeam;
        FitHeight();
    }

    readonly int _lines;

    public TextBox Box { get; }

    void FitHeight() =>
        Height = Box.Multiline ? Box.Font.Height * _lines + Theme.S(18) : Math.Max(Theme.S(36), Box.PreferredHeight + Theme.S(16));

    protected override void OnClick(EventArgs e) { Box.Focus(); base.OnClick(e); }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (Box is null) return; // the base constructor lays out before Box exists
        var pad = Theme.S(11);
        if (Box.Multiline)
            Box.SetBounds(pad, Theme.S(8), Width - pad - Theme.S(4), Height - Theme.S(14));
        else
            Box.SetBounds(pad, (Height - Box.Height) / 2, Width - 2 * pad, Box.Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        FillRound(g, r, Theme.S(8f), Box.Enabled ? Theme.InputBack : Theme.Window);
        StrokeRound(g, r, Theme.S(8f), Box.Focused ? Theme.Accent : Theme.Border);
    }
}
