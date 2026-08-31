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

namespace Lertaro.Plugins.TotalCommander.DirMenu;

// Custom icons for entries that aren't a real filesystem path -- the root "Total Commander" entry, and
// static ini submenu groups ("-Name" in the hotlist). Real files/folders reached while browsing need
// none of this: DirMenuNode.Path being a real property lets the host's generic path-based shell-icon
// loader (App/Services/ShellMenu/QuickNavigationMenu.cs) resolve their actual file-type icon for free.
//
// Rendered as a WPF vector Geometry rasterized to an HBITMAP -- the same technique Plugins/FolderCascader/
// Navigation/Helper.cs uses for its own Favorites/History icons -- since DynamicMenuItem only carries an
// HBITMAP handle, not a live WPF element.
internal static class DirMenuIcon
{
    // A floppy disk -- Total Commander's own app icon is built around this exact motif, so it reads as
    // "Total Commander" far more directly than a generic folder would. Standard Material "save" glyph,
    // 24x24 viewBox.
    private const string FloppyDiskPath = "M17,3H5C3.89,3 3,3.9 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V7L17,3M12,19A3,3 0 0,1 9,16A3,3 0 0,1 12,13A3,3 0 0,1 15,16A3,3 0 0,1 12,19M15,9H5V5H15V9Z";

    // Standard hamburger/menu glyph (three bars) for a static ini submenu group -- it's a menu category,
    // not a filesystem location, so this reads better than reusing the folder glyph above. 24x24 viewBox.
    private const string MenuGroupPath = "M3,6H21V8H3V6M3,11H21V13H3V11M3,16H21V18H3V16Z";

    [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    private static IntPtr _rootCached = IntPtr.Zero;
    private static IntPtr _menuGroupCached = IntPtr.Zero;

    public static IntPtr GetRootHBitmap()
    {
        var fresh = Render(FloppyDiskPath, viewBoxSize: 24);
        if (_rootCached != IntPtr.Zero) DeleteObject(_rootCached);
        _rootCached = fresh;
        return _rootCached;
    }

    public static IntPtr GetMenuGroupHBitmap()
    {
        var fresh = Render(MenuGroupPath, viewBoxSize: 24);
        if (_menuGroupCached != IntPtr.Zero) DeleteObject(_menuGroupCached);
        _menuGroupCached = fresh;
        return _menuGroupCached;
    }

    private static IntPtr RunOnSta(Func<IntPtr> render)
    {
        using var done = new ManualResetEventSlim(false);
        Exception? error = null;
        var result = IntPtr.Zero;
        var thread = new Thread(() =>
        {
            try { result = render(); }
            catch (Exception ex) { error = ex; }
            finally { done.Set(); }
        })
        {
            IsBackground = true,
            Name = "DirMenuIconSta"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        done.Wait();
        if (error != null) throw error;
        return result;
    }

    private static Brush AccentBrush() =>
        (Application.Current?.TryFindResource("AccentBlue") as SolidColorBrush)
        ?? new SolidColorBrush(Color.FromRgb(33, 150, 243));

    // Re-rendered on every call (cheap, one 64x64 bitmap) so it tracks the current theme's accent color;
    // the previous handle is freed first to avoid leaking a GDI object per popup.
    private static IntPtr Render(string pathData, double viewBoxSize)
    {
        Func<IntPtr> render = () => RenderCore(pathData, viewBoxSize);
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && dispatcher.CheckAccess())
            return render();
        if (dispatcher != null)
            return dispatcher.Invoke(render);
        return RunOnSta(render);
    }

    private static IntPtr RenderCore(string pathData, double viewBoxSize)
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
