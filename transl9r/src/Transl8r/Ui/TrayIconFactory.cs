using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Transl8r.Interop;

namespace Transl8r.Ui;

/// <summary>
/// Builds the system-tray icon at runtime — a comic-sticker "8" (the transl[8]r
/// brand mark): heavy cyan digit with a dark outline on a dark rounded square,
/// so it reads on both light and dark taskbars. Generated rather than shipped as
/// an .ico so there's no asset file and we can retint it later (e.g. per state).
/// </summary>
internal static class TrayIconFactory
{
    private static readonly Color Cyan = Color.FromArgb(255, 0, 200, 255);   // edit-mode accent
    private static readonly Color Outline = Color.FromArgb(255, 6, 26, 38);  // deep teal-black
    private static readonly Color Square = Color.FromArgb(255, 12, 12, 14);  // near-black tile

    /// <summary>Renders the brand "8" to an <see cref="Icon"/>. Caller owns it
    /// (dispose on shutdown). 32px downscales cleanly to the 16/20/24px tray sizes.</summary>
    public static Icon BuildEight(int size = 32)
    {
        using var bmp = new Bitmap(size, size);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            // dark rounded tile for contrast on any taskbar colour
            using (GraphicsPath tile = RoundedRect(new RectangleF(0.5f, 0.5f, size - 1f, size - 1f), size * 0.22f))
            using (var bg = new SolidBrush(Square))
            {
                g.FillPath(bg, tile);
            }

            // the "8" as a path so we can fill AND stroke it (the comic outline)
            using FontFamily family = HeavyFamily();
            using var fmt = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            using var glyph = new GraphicsPath();
            // nudge up a hair: digits sit optically low when vertically centered
            var layout = new RectangleF(0, -size * 0.04f, size, size);
            glyph.AddString("8", family, (int)FontStyle.Bold, size * 0.82f, layout, fmt);

            using (var pen = new Pen(Outline, size * 0.10f) { LineJoin = LineJoin.Round })
            {
                g.DrawPath(pen, glyph); // outline first, fill on top
            }
            using (var fill = new SolidBrush(Cyan))
            {
                g.FillPath(fill, glyph);
            }
        }

        IntPtr hicon = bmp.GetHicon();
        try
        {
            // FromHandle doesn't own the handle; clone to a managed icon, then free it
            using var temp = Icon.FromHandle(hicon);
            return (Icon)temp.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(hicon);
        }
    }

    /// <summary>A heavy/black font for the chunky look, with graceful fallback.</summary>
    private static FontFamily HeavyFamily()
    {
        foreach (string name in new[] { "Segoe UI Black", "Arial Black" })
        {
            try { return new FontFamily(name); }
            catch (ArgumentException) { /* not installed — try the next */ }
        }
        return new FontFamily(GenericFontFamilies.SansSerif);
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
