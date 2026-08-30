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

namespace Lertaro.Plugins.DirectoryOpus.Favorites;

// Icons for the entries that are not a real filesystem path -- the root "Favorites" entry and the
// submenus inside it. Real files and folders need none of this: FavoritesNode.Path being a real property
// lets the host's generic path-based shell-icon loader (App/Services/ShellMenu/QuickNavigationMenu.cs)
// resolve their actual file-type icon for free.
//
// Same technique as TotalCommander's DirMenuIcon and FolderCascader's Helper: a WPF vector Geometry
// rasterized to an HBITMAP, because DynamicMenuItem carries an HBITMAP handle rather than a live element.
internal static class FavoritesIcon
{
    // A star -- what Opus itself uses for this feature, and what "favorites" reads as generally.
    // Standard Material "star" glyph, 24x24 viewBox.
    private const string StarPath = "M12,17.27L18.18,21L16.54,13.97L22,9.24L14.81,8.62L12,2L9.19,8.62L2,9.24L7.45,13.97L5.82,21L12,17.27Z";

    // Standard hamburger/menu glyph for a <folder> submenu: it is a menu category, not a filesystem
    // location, so a folder glyph would be misleading. Matches DirMenuIcon's own choice for the same case.
    private const string MenuGroupPath = "M3,6H21V8H3V6M3,11H21V13H3V11M3,16H21V18H3V16Z";

    [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    private static IntPtr _rootCached = IntPtr.Zero;
    private static IntPtr _menuGroupCached = IntPtr.Zero;

    public static IntPtr GetRootHBitmap()
    {
        var fresh = Render(StarPath, viewBoxSize: 24);
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
            Name = "FavoritesIconSta"
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
