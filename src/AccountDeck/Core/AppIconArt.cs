using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace AccountDeck.Core;

/// <summary>
/// The AccountDeck logo: a fanned stack of account cards on a warm gradient tile.
/// Drawn in code so the window, tray and exported .ico always match.
/// </summary>
public static class AppIconArt
{
    static readonly Color Top = Color.FromArgb(0xF6, 0xA2, 0x6E);
    static readonly Color Bottom = Color.FromArgb(0xD6, 0x54, 0x38);
    static readonly Color Ink = Color.FromArgb(0xC9, 0x4F, 0x34);

    public static void Paint(Graphics g, RectangleF box)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var s = box.Width;

        using (var tile = BadgeIcon.RoundedRect(box, s * 0.23f))
        using (var gradient = new LinearGradientBrush(box, Top, Bottom, 45f))
            g.FillPath(gradient, tile);

        // Card geometry, relative to the tile.
        var cardW = s * 0.50f;
        var cardH = s * 0.60f;
        var center = new PointF(box.X + s * 0.53f, box.Y + s * 0.53f);

        DrawCard(g, center, cardW, cardH, -20f, new PointF(-s * 0.10f, s * 0.01f), Color.FromArgb(85, Color.White), s, false);
        DrawCard(g, center, cardW, cardH, -9f, new PointF(-s * 0.045f, 0), Color.FromArgb(150, Color.White), s, false);
        DrawCard(g, center, cardW, cardH, 0f, new PointF(s * 0.02f, 0), Color.White, s, true);
    }

    static void DrawCard(Graphics g, PointF center, float w, float h, float angle, PointF shift, Color fill, float s, bool face)
    {
        var state = g.Save();
        g.TranslateTransform(center.X + shift.X, center.Y + shift.Y);
        g.RotateTransform(angle);
        var rect = new RectangleF(-w / 2, -h / 2, w, h);

        if (face)
        {
            // Soft shadow so the front card lifts off the stack.
            using var shadowPath = BadgeIcon.RoundedRect(rect with { X = rect.X + s * 0.012f, Y = rect.Y + s * 0.02f }, s * 0.07f);
            using var shadow = new SolidBrush(Color.FromArgb(60, 90, 30, 10));
            g.FillPath(shadow, shadowPath);
        }

        using (var path = BadgeIcon.RoundedRect(rect, s * 0.07f))
        using (var brush = new SolidBrush(fill))
            g.FillPath(brush, path);

        if (face)
        {
            using var ink = new SolidBrush(Ink);
            using var soft = new SolidBrush(Color.FromArgb(110, Ink));
            var head = w * 0.30f;
            g.FillEllipse(ink, -head / 2, rect.Top + h * 0.16f, head, head);
            var shoulders = new RectangleF(-w * 0.27f, rect.Top + h * 0.16f + head * 1.08f, w * 0.54f, h * 0.24f);
            using (var body = new GraphicsPath())
            {
                body.AddArc(shoulders.X, shoulders.Y, shoulders.Width, shoulders.Height * 2, 180, 180);
                body.CloseFigure();
                g.FillPath(ink, body);
            }
            var lineH = h * 0.06f;
            using (var line = BadgeIcon.RoundedRect(new RectangleF(-w * 0.30f, rect.Bottom - h * 0.22f, w * 0.60f, lineH), lineH / 2))
                g.FillPath(soft, line);
        }
        g.Restore(state);
    }

    public static Icon CreateIcon() => new(new MemoryStream(BadgeIcon.BuildIco(Paint)));

    /// <summary>Writes AccountDeck.ico and a 512 px PNG into <paramref name="folder"/> (used for the exe and README).</summary>
    public static void Export(string folder)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "AccountDeck.ico"), BadgeIcon.BuildIco(Paint));
        using var bmp = new Bitmap(512, 512, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            Paint(g, new RectangleF(8, 8, 496, 496));
        }
        bmp.Save(Path.Combine(folder, "AccountDeck.png"), ImageFormat.Png);
    }
}
