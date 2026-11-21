using System.Runtime.InteropServices;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Geometry = System.Windows.Media.Geometry;
using DrawingVisual = System.Windows.Media.DrawingVisual;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Color = System.Windows.Media.Color;
using RenderTargetBitmap = System.Windows.Media.Imaging.RenderTargetBitmap;
using PixelFormats = System.Windows.Media.PixelFormats;

namespace Lertaro.Plugins.CustomCommands;

// Rasterizes custom-command SVG paths to HBITMAP for quick-navigation rows, falling back to the terminal
// glyph. Cache handles for the menu session: deleting an earlier handle while building a later row can
// invalidate it before WPF creates the image. ClearSession invalidates the cache so the next menu uses
// the current theme.
internal static class QuickNavIcon
{
    // Same default "Command Terminal" glyph CustomCommandsInstantProvider falls back to when a command
    // has no Icon of its own, for visual consistency between the two surfaces.
    private const string CommandPath = "M20 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2zm-8 12H8v-2h4v2zm6-4h-6V8h6v4z";

    // Hamburger/menu glyph for a submenu category node -- matches DirMenuIcon's own MenuGroupPath choice
    // for the same "this is a menu category, not a filesystem location" case.
    private const string CategoryPath = "M3,6H21V8H3V6M3,11H21V13H3V11M3,16H21V18H3V16Z";

    [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    private static readonly object Sync = new();
    private static readonly Dictionary<string, IntPtr> CommandCache = new(StringComparer.Ordinal);
    private static IntPtr _categoryCached = IntPtr.Zero;

    public static IntPtr GetCommandHBitmap(string? pathData)
    {
        var normalized = string.IsNullOrWhiteSpace(pathData) ? CommandPath : pathData.Trim();
        lock (Sync)
        {
            if (CommandCache.TryGetValue(normalized, out var cached)) return cached;
        }

        // Rendered OUTSIDE the lock, deliberately. RenderOnUiThread marshals onto the UI thread, and a caller
        // holding Sync while waiting for the UI thread deadlocks against the UI thread taking Sync -- which is
        // reachable because this is called both from the quick-navigation submenu loader's Task.Run and from
        // the UI thread's own action-menu build. The same shape was fixed in FolderCascader's IconBitmapCache.
        IntPtr rendered;
        try
        {
            rendered = RenderOnUiThread(normalized);
        }
        catch
        {
            rendered = RenderOnUiThread(CommandPath);
            normalized = CommandPath;
        }

        lock (Sync)
        {
            // Another thread may have rendered the same glyph (or the fallback did just now) while this one
            // was rendering: the cache's rule is one handle per glyph, so the earlier winner is kept.
            if (CommandCache.TryGetValue(normalized, out var existing))
            {
                DeleteObject(rendered);
                return existing;
            }

            CommandCache[normalized] = rendered;
            return rendered;
        }
    }

    public static IntPtr GetCategoryHBitmap()
    {
        lock (Sync)
        {
            if (_categoryCached != IntPtr.Zero) return _categoryCached;
        }

        // Same shape as GetCommandHBitmap above, and for the same reason: the render marshals onto the UI
        // thread, so it happens outside the lock, and two threads racing on a cold cache each used to make a
        // bitmap and then overwrite the other's -- leaving the loser's GDI handle with nothing pointing at it.
        var rendered = RenderOnUiThread(CategoryPath);
        lock (Sync)
        {
            if (_categoryCached != IntPtr.Zero)
            {
                DeleteObject(rendered);
                return _categoryCached;
            }

            return _categoryCached = rendered;
        }
    }

    // Called once per quick-nav popup session (CustomCommandsQuickNavProvider.ClearSession), a point at
    // which no row from the just-closed (or not-yet-built) session can still be relying on the old
    // handle -- safe to free and force a fresh render next time either getter is called.
    public static void Invalidate()
    {
        lock (Sync)
        {
            foreach (var handle in CommandCache.Values) DeleteObject(handle);
            CommandCache.Clear();
            if (_categoryCached != IntPtr.Zero) { DeleteObject(_categoryCached); _categoryCached = IntPtr.Zero; }
        }
    }

    private static Brush AccentBrush() =>
        (Application.Current?.TryFindResource("AccentBlue") as SolidColorBrush)
        ?? new SolidColorBrush(Color.FromRgb(33, 150, 243));

    private static IntPtr RenderOnUiThread(string pathData)
    {
        var dispatcher = Application.Current?.Dispatcher;
        return dispatcher == null || dispatcher.CheckAccess()
            ? Render(pathData)
            : dispatcher.Invoke(() => Render(pathData));
    }

    private static IntPtr Render(string pathData)
    {
        var geometry = Geometry.Parse(pathData);
        var bounds = geometry.Bounds;
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
            throw new InvalidOperationException("The SVG path has no renderable bounds.");

        // Search results let WPF arrange the geometry into the Image control's bounds. The native
        // menu receives only a bitmap, so a fixed 24-unit scale clipped paths authored with another
        // viewBox (or with negative coordinates). Normalize by the actual geometry bounds to match
        // the search-result appearance regardless of the path's source coordinate system.
        var targetSize = 48.0;
        var scale = Math.Min(targetSize / bounds.Width, targetSize / bounds.Height);
        var offsetX = (64.0 - bounds.Width * scale) / 2.0 - bounds.X * scale;
        var offsetY = (64.0 - bounds.Height * scale) / 2.0 - bounds.Y * scale;
        var transform = new System.Windows.Media.MatrixTransform(scale, 0, 0, scale, offsetX, offsetY);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(transform);
            dc.DrawGeometry(AccentBrush(), null, geometry);
            dc.Pop();
        }

        var rtb = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var stride = 64 * 4;
        var pixels = new byte[64 * stride];
        rtb.CopyPixels(pixels, stride, 0);

        using var bmp = new System.Drawing.Bitmap(64, 64, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        var rect = new System.Drawing.Rectangle(0, 0, 64, 64);
        var bmpData = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);
        Marshal.Copy(pixels, 0, bmpData.Scan0, pixels.Length);
        bmp.UnlockBits(bmpData);

        return bmp.GetHbitmap();
    }
}
