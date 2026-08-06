using System.Runtime.InteropServices;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Geometry = System.Windows.Media.Geometry;
using DrawingVisual = System.Windows.Media.DrawingVisual;
using ScaleTransform = System.Windows.Media.ScaleTransform;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Color = System.Windows.Media.Color;
using RenderTargetBitmap = System.Windows.Media.Imaging.RenderTargetBitmap;
using PixelFormats = System.Windows.Media.PixelFormats;

namespace Lertaro.Plugins.CustomCommands;

// Icons for the quick-navigation menu manifestation of custom commands, which only carries an HBITMAP
// handle (DynamicMenuItem), not a live WPF element -- same "vector Geometry rasterized to HBITMAP"
// technique Plugins/TotalCommander/DirMenu/DirMenuIcon.cs and Plugins/FolderCascader/Navigation/Helper.cs
// already use for their own synthetic (non-filesystem) entries. Per-command icons (CommandItem.Icon) are
// NOT rendered here -- every quick-nav command shares this one default icon for now.
//
// Unlike DirMenuIcon's own "delete the previous handle, render a fresh one" pattern -- which is safe
// there because each of its icons (the root entry, a static ini group) only needs ONE live consumer at a
// time -- this icon is requested once per command/category row, and every row in a menu is built in the
// same pass before any of them are actually rendered/materialized into a WPF Image. Deleting the
// previous handle on every call was invalidating an earlier row's HBITMAP before WPF ever got around to
// converting it, so only the last row built ended up with a surviving icon. Caching and reusing the same
// handle for as long as the handle stays valid -- only re-rendering once per quick-nav session
// (Invalidate, called from ClearSession) to still pick up a theme/accent-color change between popup
// opens -- fixes that without needing every row to independently own a handle.
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

    private static IntPtr _commandCached = IntPtr.Zero;
    private static IntPtr _categoryCached = IntPtr.Zero;

    public static IntPtr GetCommandHBitmap()
    {
        if (_commandCached == IntPtr.Zero) _commandCached = Render(CommandPath, viewBoxSize: 24);
        return _commandCached;
    }

    public static IntPtr GetCategoryHBitmap()
    {
        if (_categoryCached == IntPtr.Zero) _categoryCached = Render(CategoryPath, viewBoxSize: 24);
        return _categoryCached;
    }

    // Called once per quick-nav popup session (CustomCommandsQuickNavProvider.ClearSession), a point at
    // which no row from the just-closed (or not-yet-built) session can still be relying on the old
    // handle -- safe to free and force a fresh render next time either getter is called.
    public static void Invalidate()
    {
        if (_commandCached != IntPtr.Zero) { DeleteObject(_commandCached); _commandCached = IntPtr.Zero; }
        if (_categoryCached != IntPtr.Zero) { DeleteObject(_categoryCached); _categoryCached = IntPtr.Zero; }
    }

    private static Brush AccentBrush() =>
        (Application.Current?.TryFindResource("AccentBlue") as SolidColorBrush)
        ?? new SolidColorBrush(Color.FromRgb(33, 150, 243));

    private static IntPtr Render(string pathData, double viewBoxSize)
    {
        var geometry = Geometry.Parse(pathData);
        var scale = 64.0 / viewBoxSize;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale, scale));
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
