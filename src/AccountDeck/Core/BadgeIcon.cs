using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace AccountDeck.Core;

/// <summary>Draws the coloured letter badge used for accounts and packs it into .ico files.</summary>
public static class BadgeIcon
{
    static readonly int[] IcoSizes = { 256, 48, 32, 16 };

    public static Color ParseColor(string hex, Color fallback)
    {
        try { return ColorTranslator.FromHtml(hex); }
        catch { return fallback; }
    }

    public static void Paint(Graphics g, RectangleF box, Color fill, string letter)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using (var path = RoundedRect(box, box.Width * 0.28f))
        using (var brush = new LinearGradientBrush(box, Lighten(fill, 0.12f), fill, LinearGradientMode.Vertical))
            g.FillPath(brush, path);

        using var font = new Font("Segoe UI Semibold", box.Height * 0.46f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var text = new SolidBrush(Color.White);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(letter, font, text, box with { Y = box.Y + box.Height * 0.03f }, format);
    }

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        if (d <= 0)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Color Lighten(Color c, float amount) => Color.FromArgb(c.A,
        (int)(c.R + (255 - c.R) * amount), (int)(c.G + (255 - c.G) * amount), (int)(c.B + (255 - c.B) * amount));

    /// <summary>Builds a multi-size .ico of a letter badge.</summary>
    public static byte[] BuildIco(Color fill, string letter) =>
        BuildIco((g, box) => Paint(g, box, fill, letter));

    /// <summary>Builds a multi-size .ico (PNG-compressed entries) from any painter.</summary>
    public static byte[] BuildIco(Action<Graphics, RectangleF> paint)
    {
        var images = IcoSizes.Select(size => RenderPng(size, paint)).ToList();

        using var output = new MemoryStream();
        using var w = new BinaryWriter(output);
        w.Write((short)0);               // reserved
        w.Write((short)1);               // resource type: icon
        w.Write((short)images.Count);

        var offset = 6 + 16 * images.Count;
        for (var i = 0; i < images.Count; i++)
        {
            var edge = (byte)(IcoSizes[i] >= 256 ? 0 : IcoSizes[i]); // 0 stands for 256
            w.Write(edge);
            w.Write(edge);
            w.Write((byte)0);            // palette size
            w.Write((byte)0);            // reserved
            w.Write((short)1);           // colour planes
            w.Write((short)32);          // bits per pixel
            w.Write(images[i].Length);
            w.Write(offset);
            offset += images[i].Length;
        }
        foreach (var png in images) w.Write(png);
        w.Flush();
        return output.ToArray();
    }

    public static Icon CreateIcon(Color fill, string letter) => new(new MemoryStream(BuildIco(fill, letter)));

    /// <summary>Writes the account's badge to the icons folder and returns the file path.</summary>
    public static string WriteAccountIcon(AccountProfile account)
    {
        Directory.CreateDirectory(AppPaths.IconsDir);
        var path = Path.Combine(AppPaths.IconsDir, account.Id + ".ico");
        File.WriteAllBytes(path, BuildIco(ParseColor(account.Color, Color.Gray), account.Initial));
        return path;
    }

    static byte[] RenderPng(int size, Action<Graphics, RectangleF> paint)
    {
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            var inset = size * 0.04f;
            paint(g, new RectangleF(inset, inset, size - 2 * inset, size - 2 * inset));
        }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }
}
