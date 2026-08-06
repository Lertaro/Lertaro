using Lertaro.PluginSdk;

namespace Lertaro.Plugins.FolderCascader.Navigation;

// Renders and caches the small set of GDI HBITMAPs the cascader menu draws (folder/file/favorites/
// history icons). Kept separate from history lookups and Explorer-window enumeration -- icon rendering
// changes for theming reasons, not for either of those.
public static class IconBitmapCache
{
    public static IntPtr FolderHBitmap { get; private set; } = IntPtr.Zero;
    public static IntPtr FileHBitmap { get; private set; } = IntPtr.Zero;
    public static IntPtr FavoritesHBitmap { get; private set; } = IntPtr.Zero;
    public static IntPtr HistoryHBitmap { get; private set; } = IntPtr.Zero;
    public static IntPtr CategoryHBitmap { get; private set; } = IntPtr.Zero;
    public static IntPtr AddHBitmap { get; private set; } = IntPtr.Zero;

    private static readonly object _iconLock = new();

    [System.Runtime.InteropServices.DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    public static void EnsureIcons()
    {
        if (FolderHBitmap == IntPtr.Zero)
        {
            lock (_iconLock)
            {
                if (FolderHBitmap == IntPtr.Zero)
                {
                    try
                    {
                        FolderHBitmap = ShellIconLoader.GetIconHBitmap("dummy_folder", isDir: true);
                        FileHBitmap = ShellIconLoader.GetIconHBitmap("dummy_file.txt", isDir: false);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"[FolderCascader] Failed to create cached shell HBITMAP icons: {ex.Message}", LogLevel.Warn);
                    }
                }
            }
        }

        lock (_iconLock)
        {
            try
            {
                if (FavoritesHBitmap != IntPtr.Zero)
                {
                    DeleteObject(FavoritesHBitmap);
                }
                if (HistoryHBitmap != IntPtr.Zero)
                {
                    DeleteObject(HistoryHBitmap);
                }
                if (CategoryHBitmap != IntPtr.Zero)
                {
                    DeleteObject(CategoryHBitmap);
                }
                if (AddHBitmap != IntPtr.Zero)
                {
                    DeleteObject(AddHBitmap);
                }

                FavoritesHBitmap = CreateStarHBitmap();
                HistoryHBitmap = CreateClockHBitmap();
                CategoryHBitmap = CreateCategoryHBitmap();
                AddHBitmap = CreateAddHBitmap();
            }
            catch (Exception ex)
            {
                Logger.Log($"[FolderCascader] Failed to update themed icons: {ex.Message}", LogLevel.Warn);
            }
        }
    }

    // scale defaults to 4.0 (a 64px bitmap over the star/clock paths' own ~16-unit viewBox); the
    // category hamburger path below is authored in a 24-unit viewBox (matching QuickNavIcon's own
    // copy of it) and needs a correspondingly smaller scale, or it renders oversized/clipped.
    private static IntPtr CreateHBitmapFromWpfPath(string pathData, System.Windows.Media.Brush? fill, System.Windows.Media.Pen? stroke, double scale = 4.0)
    {
        var geometry = System.Windows.Media.Geometry.Parse(pathData);
        var visual = new System.Windows.Media.DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new System.Windows.Media.ScaleTransform(scale, scale));
            dc.DrawGeometry(fill, stroke, geometry);
            dc.Pop();
        }

        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(64, 64, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(visual);

        var stride = 64 * 4;
        var pixels = new byte[64 * stride];
        rtb.CopyPixels(pixels, stride, 0);

        using var bmp = new System.Drawing.Bitmap(64, 64, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        var rect = new System.Drawing.Rectangle(0, 0, 64, 64);
        var bmpData = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bmpData.Scan0, pixels.Length);
        bmp.UnlockBits(bmpData);

        return bmp.GetHbitmap();
    }

    private static IntPtr CreateStarHBitmap()
    {
        var path = "M 8,1.5 L 10.2,6 L 15,6.5 L 11.3,9.7 L 12.5,14.5 L 8,12 L 3.5,14.5 L 4.7,9.7 L 1,6.5 L 5.8,6 Z";
        var warningBrush = System.Windows.Application.Current?.TryFindResource("WarningBrush") as System.Windows.Media.SolidColorBrush;
        var fill = warningBrush ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 193, 7));
        var stroke = new System.Windows.Media.Pen(fill, 1.0);
        return CreateHBitmapFromWpfPath(path, fill, stroke);
    }

    private static IntPtr CreateClockHBitmap()
    {
        var path = "M 8,2 A 6,6 0 1,0 8.001,2 M 8,5 L 8,8 L 11,8";
        var accentBrush = System.Windows.Application.Current?.TryFindResource("AccentBlue") as System.Windows.Media.SolidColorBrush;
        var strokeBrush = accentBrush ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243));
        var stroke = new System.Windows.Media.Pen(strokeBrush, 1.5);
        return CreateHBitmapFromWpfPath(path, null, stroke);
    }

    // Hamburger/menu glyph for a submenu category node (a grouping created by a folder's own SubMenu
    // field, not a real filesystem location) -- same glyph and theming (AccentBlue) as
    // Plugins/CustomCommands/QuickNavIcon.cs's own GetCategoryHBitmap, for a consistent look between
    // the two plugins' cascading menus.
    private static IntPtr CreateCategoryHBitmap()
    {
        var path = "M3,6H21V8H3V6M3,11H21V13H3V11M3,16H21V18H3V16Z";
        var accentBrush = System.Windows.Application.Current?.TryFindResource("AccentBlue") as System.Windows.Media.SolidColorBrush;
        var fill = accentBrush ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243));
        return CreateHBitmapFromWpfPath(path, fill, null, scale: 64.0 / 24.0);
    }

    // Plus glyph for "Add Current Folder" -- same 24-unit viewBox and AccentBlue theming as the
    // category hamburger above, for a consistent look among this plugin's own structural (non-shell)
    // menu icons.
    private static IntPtr CreateAddHBitmap()
    {
        var path = "M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z";
        var accentBrush = System.Windows.Application.Current?.TryFindResource("AccentBlue") as System.Windows.Media.SolidColorBrush;
        var fill = accentBrush ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243));
        return CreateHBitmapFromWpfPath(path, fill, null, scale: 64.0 / 24.0);
    }
}
