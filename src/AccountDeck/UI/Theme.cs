using AccountDeck.Core;

namespace AccountDeck.UI;

/// <summary>Colours, fonts and small styling helpers for the dark UI.</summary>
internal static class Theme
{
    public static readonly Color Window = Color.FromArgb(0x15, 0x16, 0x19);
    public static readonly Color Sidebar = Color.FromArgb(0x1B, 0x1C, 0x20);
    public static readonly Color Card = Color.FromArgb(0x22, 0x24, 0x29);
    public static readonly Color CardHover = Color.FromArgb(0x2A, 0x2D, 0x33);
    public static readonly Color Selected = Color.FromArgb(0x2E, 0x31, 0x38);
    public static readonly Color InputBack = Color.FromArgb(0x1F, 0x21, 0x25);
    public static readonly Color Border = Color.FromArgb(0x33, 0x36, 0x3D);
    public static readonly Color Text = Color.FromArgb(0xEC, 0xED, 0xEF);
    public static readonly Color Dim = Color.FromArgb(0x93, 0x98, 0xA3);
    public static readonly Color Faint = Color.FromArgb(0x5E, 0x63, 0x6E);
    public static readonly Color Accent = Color.FromArgb(0xF0, 0x91, 0x5F);
    public static readonly Color AccentHover = Color.FromArgb(0xF5, 0xA4, 0x78);
    public static readonly Color OnAccent = Color.FromArgb(0x1A, 0x14, 0x10);
    public static readonly Color Running = Color.FromArgb(0x4C, 0xC3, 0x8A);
    public static readonly Color Danger = Color.FromArgb(0xE5, 0x48, 0x4D);

    public static readonly Font Body = new("Segoe UI", 9.75f);
    public static readonly Font BodyBold = new("Segoe UI Semibold", 9.75f);
    public static readonly Font Small = new("Segoe UI", 8.75f);
    public static readonly Font Heading = new("Segoe UI Semibold", 11f);
    public static readonly Font Title = new("Segoe UI Semibold", 16f);
    public static readonly Font Large = new("Segoe UI", 12f);

    /// <summary>Device pixels per logical pixel on the primary monitor.</summary>
    public static readonly float Scale = ReadScale();

    public static int S(int logical) => (int)Math.Round(logical * Scale);
    public static float S(float logical) => logical * Scale;

    public static void StyleForm(Form form)
    {
        form.AutoScaleMode = AutoScaleMode.None;
        form.BackColor = Window;
        form.ForeColor = Text;
        form.Font = Body;
        form.HandleCreated += (_, _) => DarkTitleBar(form.Handle);
    }

    public static void DarkTitleBar(IntPtr handle)
    {
        var on = 1;
        Native.DwmSetWindowAttribute(handle, Native.DwmUseImmersiveDarkMode, ref on, sizeof(int));
    }

    public static void RoundCorners(IntPtr handle)
    {
        var round = Native.DwmCornerRound;
        Native.DwmSetWindowAttribute(handle, Native.DwmWindowCornerPreference, ref round, sizeof(int));
    }

    public static TextBox Input(string text = "", bool multiline = false)
    {
        var box = new TextBox
        {
            Text = text,
            Multiline = multiline,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = InputBack,
            ForeColor = Text,
            Font = Body,
            ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
        };
        if (multiline) DarkScrollBars(box);
        return box;
    }

    /// <summary>Gives a native control (TextBox, RichTextBox…) Windows' dark scroll bars.</summary>
    public static void DarkScrollBars(Control control) =>
        control.HandleCreated += (_, _) => Native.SetWindowTheme(control.Handle, "DarkMode_Explorer", null);

    public static Label Caption(string text, bool dim = true) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = dim ? Dim : Text,
        Font = dim ? Small : BodyBold,
        Margin = new Padding(0, S(10), 0, S(4)),
    };

    public static ContextMenuStrip Menu() => new()
    {
        Renderer = new DarkMenuRenderer(),
        ShowImageMargin = true,
        BackColor = Card,
        ForeColor = Text,
        Font = Body,
    };

    public static ToolStripMenuItem Item(string text, Action onClick, Color? color = null)
    {
        var item = new ToolStripMenuItem(text) { ForeColor = color ?? Text, Padding = new Padding(0, S(3), 0, S(3)) };
        item.Click += (_, _) => onClick();
        return item;
    }

    static float ReadScale()
    {
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        return g.DpiX / 96f;
    }

    sealed class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Card;
        public override Color ImageMarginGradientBegin => Card;
        public override Color ImageMarginGradientMiddle => Card;
        public override Color ImageMarginGradientEnd => Card;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Selected;
        public override Color MenuItemSelected => Selected;
        public override Color MenuItemSelectedGradientBegin => Selected;
        public override Color MenuItemSelectedGradientEnd => Selected;
        public override Color MenuItemPressedGradientBegin => Selected;
        public override Color MenuItemPressedGradientEnd => Selected;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Border;
        public override Color CheckBackground => Selected;
        public override Color CheckSelectedBackground => Selected;
        public override Color CheckPressedBackground => Selected;
    }

    sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColors()) => RoundedEdges = false;

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? e.Item.ForeColor : Faint;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Dim;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            using var pen = new Pen(Accent, S(2f));
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.DrawLines(pen, new[]
            {
                new PointF(r.Left + r.Width * 0.22f, r.Top + r.Height * 0.52f),
                new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.72f),
                new PointF(r.Left + r.Width * 0.78f, r.Top + r.Height * 0.30f),
            });
        }
    }
}
