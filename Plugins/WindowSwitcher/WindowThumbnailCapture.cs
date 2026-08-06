using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Lertaro.Plugins.WindowSwitcher;

// Captures a window's current on-screen content into a small HBITMAP thumbnail -- used as the
// result icon instead of the owning app's static exe icon. PrintWindow (not BitBlt) is the only
// approach that reliably captures GPU-composited content (Chrome, Electron apps, games) as long as
// PW_RENDERFULLCONTENT is set; this can still occasionally come back blank for an app that ignores
// WM_PRINT/WM_PRINTCLIENT, which is a known, accepted limitation rather than something worth adding
// extra detection/fallback complexity for in this first pass.
internal static class WindowThumbnailCapture
{
    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    private const uint PW_RENDERFULLCONTENT = 0x00000002;

    // The search result icon slot is a small fixed square (see DataTemplates.xaml's Image element,
    // which defaults to WPF's own Stretch="Uniform") -- capturing much larger than this and letting
    // GDI+ downscale once here is cheaper than handing the host a full-resolution bitmap it would
    // just scale down on every render anyway.
    public const int MaxDimension = 64;

    // Returns null if capture failed (window closed between enumeration and this call, or PrintWindow
    // itself failed). Returns a plain managed Bitmap (not yet an HBITMAP) rather than a one-shot GDI
    // handle so WindowThumbnailCache can hold onto it and call GetHbitmap() fresh on every
    // GetInstantResults call -- GetHbitmap() always produces a brand new, independent handle, so
    // repeated calls on the same cached Bitmap never conflict with the host's own "delete the handle
    // you were handed" contract for any individual result. Caller owns and must Dispose the Bitmap.
    public static Bitmap? Capture(IntPtr hwnd)
    {
        try
        {
            if (!GetWindowRect(hwnd, out var rect))
                return null;

            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0)
                return null;

            using var fullBitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(fullBitmap))
            {
                var hdc = g.GetHdc();
                bool printed;
                try
                {
                    printed = PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT);
                }
                finally
                {
                    g.ReleaseHdc(hdc);
                }
                if (!printed)
                    return null;
            }

            var scale = Math.Min((double)MaxDimension / width, (double)MaxDimension / height);
            if (scale >= 1.0)
                return (Bitmap)fullBitmap.Clone();

            var scaledWidth = Math.Max(1, (int)Math.Round(width * scale));
            var scaledHeight = Math.Max(1, (int)Math.Round(height * scale));

            var thumbnail = new Bitmap(scaledWidth, scaledHeight, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(thumbnail))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(fullBitmap, 0, 0, scaledWidth, scaledHeight);
            }
            return thumbnail;
        }
        catch
        {
            return null;
        }
    }
}
