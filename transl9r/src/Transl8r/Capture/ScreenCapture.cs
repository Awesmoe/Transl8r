using System.Drawing;
using System.Drawing.Imaging;
using Region = Transl8r.Config.Region;

namespace Transl8r.Capture;

/// <summary>GDI screen grab of a region in PHYSICAL pixels.</summary>
internal static class ScreenCapture
{
    /// <summary>
    /// Capture a region. The process is PerMonitorV2 DPI-aware (app.manifest), so
    /// GDI coordinates are physical pixels — matching the stored region coords
    /// (HANDOVER #8).
    /// </summary>
    public static Bitmap Grab(Region r)
    {
        var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(r.Left, r.Top, 0, 0,
            new Size(r.Width, r.Height), CopyPixelOperation.SourceCopy);
        return bmp;
    }
}
