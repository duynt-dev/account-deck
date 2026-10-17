using System.Drawing.Drawing2D;
using AccountDeck.Core;

namespace AccountDeck.UI;

/// <summary>One app in the "Manage apps" grid, either already added or found on the PC.</summary>
internal sealed class AppChoice
{
    public required string Name { get; init; }
    public string? ExePath { get; init; }
    public required AppSupport Support { get; init; }
    public required AppCategory Category { get; init; }

    /// <summary>
    /// The app's entry in AccountDeck, kept even after it is unticked so ticking it again
    /// restores the same accounts. Null for a found app that was never added.
    /// </summary>
    public AppEntry? Entry { get; set; }

    /// <summary>Set for apps found on the PC (or browsed to); used to create <see cref="Entry"/>.</summary>
    public FoundApp? Found { get; init; }

    public Bitmap? Icon => Entry is not null ? AppIcons.Get(Entry) : ExePath is null ? null : AppIcons.ForExe(ExePath);
}

/// <summary>Square, tickable tile: big icon, name, and a dot showing how well multi-account works.</summary>
internal sealed class AppTile : PaintedControl
{
    static readonly Color Likely = Color.FromArgb(0xD9, 0xA4, 0x41);

    readonly Bitmap? _icon;
    bool _checked;

    public AppTile(AppChoice choice, bool isChecked)
    {
        Choice = choice;
        _checked = isChecked;
        _icon = choice.Icon;
        Size = new Size(Theme.S(92), Theme.S(90));
        Margin = new Padding(0, 0, Theme.S(8), Theme.S(8));
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    public AppChoice Choice { get; }

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

    protected override void OnClick(EventArgs e)
    {
        Checked = !Checked;
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

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        var radius = Theme.S(10f);
        FillRound(g, r, radius, Checked ? Theme.Selected : Hover ? Theme.CardHover : Theme.Card);
        StrokeRound(g, r, radius, Checked ? Theme.Accent : Focused ? Theme.Faint : Theme.Border, Checked ? Theme.S(1.6f) : 1f);

        // Icon.
        var size = Theme.S(32);
        var iconRect = new Rectangle((Width - size) / 2, Theme.S(13), size, size);
        if (_icon is not null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(_icon, iconRect);
        }
        else
        {
            BadgeIcon.Paint(g, iconRect, Theme.Faint, Choice.Name.Length > 0 ? Choice.Name[..1].ToUpperInvariant() : "?");
        }
        if (!Checked && !Hover)
        {
            // Unticked apps look slightly faded so the selection stands out.
            using var veil = new SolidBrush(Color.FromArgb(70, Theme.Card));
            g.FillRectangle(veil, iconRect);
        }

        // Name, up to two lines.
        var textRect = new Rectangle(Theme.S(5), iconRect.Bottom + Theme.S(6), Width - Theme.S(10), Theme.S(32));
        TextRenderer.DrawText(g, Choice.Name, Theme.Small, textRect, Checked ? Theme.Text : Theme.Dim,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        // Support dot, top-left.
        var dot = Theme.S(7f);
        var dotColor = Choice.Support switch
        {
            AppSupport.Preset => Theme.Running,
            AppSupport.Electron => Likely,
            _ => Theme.Faint,
        };
        using (var brush = new SolidBrush(dotColor))
            g.FillEllipse(brush, Theme.S(8f), Theme.S(8f), dot, dot);

        // Check mark, top-right.
        var box = Theme.S(16f);
        var boxRect = new RectangleF(Width - box - Theme.S(6f), Theme.S(6f), box, box);
        if (Checked || Hover || Focused) ToggleCheck.DrawBox(g, boxRect, Checked, Hover || Focused);
    }

    public static string SupportText(AppSupport support) => support switch
    {
        AppSupport.Preset => "Supported",
        AppSupport.Electron => "Probably works",
        _ => "May not work",
    };
}
