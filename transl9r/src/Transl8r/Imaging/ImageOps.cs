using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace Transl8r.Imaging;

/// <summary>Pixel-buffer helpers: frame diffing and PNG/base64 encoding.</summary>
internal static class ImageOps
{
    public static byte[] GetBgraBytes(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            int len = Math.Abs(data.Stride) * bmp.Height;
            var buf = new byte[len];
            Marshal.Copy(data.Scan0, buf, 0, len);
            return buf;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    /// <summary>
    /// True if more than <paramref name="ratio"/> of color-channel bytes differ
    /// by &gt; 24 levels. Mirrors the Python similarity check (HANDOVER #1) so a
    /// blinking arrow / textbox shimmer doesn't count as a change but new
    /// dialogue does. Alpha is ignored (constant for screen grabs).
    /// </summary>
    public static bool FrameChanged(byte[]? a, byte[]? b, double ratio)
    {
        if (a == null || b == null || a.Length != b.Length)
        {
            return true;
        }

        long changed = 0;
        long total = 0;
        for (int i = 0; i + 3 < a.Length; i += 4)
        {
            if (Math.Abs(a[i] - b[i]) > 24) changed++;       // B
            if (Math.Abs(a[i + 1] - b[i + 1]) > 24) changed++; // G
            if (Math.Abs(a[i + 2] - b[i + 2]) > 24) changed++; // R
            total += 3;
        }
        if (total == 0)
        {
            return true;
        }
        return (double)changed / total > ratio;
    }

    /// <summary>
    /// PNG-encode to base64, capping the longest side at <paramref name="maxSide"/>
    /// (vision token cost scales with resolution; a textbox crop stays legible
    /// well below native res — the Python `_shrink`).
    /// </summary>
    public static string ToPngBase64(Bitmap bmp, int maxSide = 1024)
    {
        Bitmap toEncode = bmp;
        bool dispose = false;

        int w = bmp.Width, h = bmp.Height, longest = Math.Max(w, h);
        if (longest > maxSide)
        {
            double scale = (double)maxSide / longest;
            int nw = Math.Max(1, (int)(w * scale));
            int nh = Math.Max(1, (int)(h * scale));
            var resized = new Bitmap(nw, nh, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(resized))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(bmp, 0, 0, nw, nh);
            }
            toEncode = resized;
            dispose = true;
        }

        try
        {
            using var ms = new MemoryStream();
            toEncode.Save(ms, ImageFormat.Png);
            return Convert.ToBase64String(ms.ToArray());
        }
        finally
        {
            if (dispose)
            {
                toEncode.Dispose();
            }
        }
    }
}
