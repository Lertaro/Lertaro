using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Lertaro.App.Services.ShellIcons;

/// <summary>
/// Fetches high-resolution shell icons from the system image list (48px ExtraLarge /
/// 256px Jumbo) instead of the fixed 32px SHGFI_LARGEICON, so result icons stay crisp
/// when displayed larger or on high-DPI displays. Returns null on failure so callers
/// can fall back to the legacy 32px path.
/// </summary>
internal static class ShellImageListInterop
{
    private const int SHIL_EXTRALARGE = 2; // 48px
    private const int SHIL_JUMBO = 4;      // 256px
    private const uint SHGFI_SYSICONINDEX = 0x4000;
    private const int ILD_TRANSPARENT = 0x1;
    private static Guid _iidImageList = new("46EB5926-582E-4017-9FDF-E8998DAA0950");

    [DllImport("shell32.dll")]
    private static extern int SHGetImageList(int iImageList, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IImageList ppv);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int PrivateExtractIconsW(string szFileName, int nIconIndex, int cxIcon, int cyIcon, IntPtr[] phicon, int[] piconid, int nIcons, uint flags);

    // Only slots up to GetIcon (index 7) are declared; the rest of the vtable is unused.
    // Order MUST match CommCtrl.h IImageList exactly or calls dispatch to the wrong method.
    [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        [PreserveSig] int Add();             // 0
        [PreserveSig] int ReplaceIcon();     // 1
        [PreserveSig] int SetOverlayImage(); // 2
        [PreserveSig] int Replace();         // 3
        [PreserveSig] int AddMasked();       // 4
        [PreserveSig] int Draw();            // 5
        [PreserveSig] int Remove();          // 6
        [PreserveSig] int GetIcon(int i, int flags, out IntPtr picon); // 7
    }

    private static double DpiScale
    {
        get { try { return GetDpiForSystem() / 96.0; } catch { return 1.0; } }
    }

    /// <summary>Target icon size in physical pixels for the current display scale. Takes the larger of
    /// the fixed main-window size and the quick window's (user-configurable, scale-applied) size, so
    /// whichever window ends up displaying icons largest still gets a crisp source bitmap.</summary>
    private static int TargetPixels() => (int)Math.Ceiling(Math.Max(UiMetrics.ResultIconSize, UiMetrics.ScaledResultIconSize) * DpiScale);

    private static int CurrentShil() => TargetPixels() <= 48 ? SHIL_EXTRALARGE : SHIL_JUMBO;

    /// <summary>Native pixel size of the currently selected image-list tier.</summary>
    public static int PreferredPixels() => CurrentShil() == SHIL_JUMBO ? 256 : 48;

    public static ImageSource? TryGetIcon(string path, uint attrs, uint extraFlags)
    {
        // Real paths: prefer IShellItemImageFactory (scales correctly; avoids Jumbo centering
        // tiny icons for exes that only ship a small icon). Skip for USEFILEATTRIBUTES lookups,
        // whose "path" is a fake dummy/extension the shell can't parse.
        if ((extraFlags & ShellIconNativeMethods.SHGFI_USEFILEATTRIBUTES) == 0)
        {
            var img = FromFactory(path);
            if (img != null) return img;
        }

        var shfi = new ShellIconNativeMethods.SHFILEINFOW();
        var r = ShellIconNativeMethods.SHGetFileInfoW(path, attrs, ref shfi, (uint)Marshal.SizeOf(shfi), SHGFI_SYSICONINDEX | extraFlags);
        return r == IntPtr.Zero ? null : FromImageList(shfi.iIcon);
    }

    public static ImageSource? TryGetIconPidl(IntPtr pidl)
    {
        var img = FromFactoryPidl(pidl);
        if (img != null) return img;

        var shfi = new ShellIconNativeMethods.SHFILEINFOW();
        var r = ShellIconNativeMethods.SHGetFileInfoW(pidl, 0, ref shfi, (uint)Marshal.SizeOf(shfi), SHGFI_SYSICONINDEX | ShellIconNativeMethods.SHGFI_PIDL);
        return r == IntPtr.Zero ? null : FromImageList(shfi.iIcon);
    }

    /// <summary>High-res icon extracted directly from a file/index (for shortcut icon locations).</summary>
    public static ImageSource? ExtractHiRes(string iconPath, int iconIndex)
    {
        var px = PreferredPixels();
        var hicons = new IntPtr[1];
        var ids = new int[1];
        var n = PrivateExtractIconsW(iconPath, iconIndex, px, px, hicons, ids, 1, 0);
        if (n <= 0 || hicons[0] == IntPtr.Zero) return null;
        try { return FromHIcon(hicons[0]); }
        finally { ShellIconNativeMethods.DestroyIcon(hicons[0]); }
    }

    // ---- IShellItemImageFactory: correctly size-scaled icon (no Jumbo small-icon centering) ----
    private const int SIIGBF_ICONONLY = 0x4;
    private const int FactorySize = 96;
    private static Guid _iidImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx; public int cy; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);
    [DllImport("shell32.dll", PreserveSig = false)]
    private static extern void SHCreateItemFromIDList(IntPtr pidl, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm); }

    private static ImageSource? FromFactory(string path)
    {
        try
        {
            var iid = _iidImageFactory;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var f);
            try { return ImageFromFactory(f); } finally { Marshal.ReleaseComObject(f); }
        }
        catch { return null; }
    }

    private static ImageSource? FromFactoryPidl(IntPtr pidl)
    {
        try
        {
            var iid = _iidImageFactory;
            SHCreateItemFromIDList(pidl, ref iid, out var f);
            try { return ImageFromFactory(f); } finally { Marshal.ReleaseComObject(f); }
        }
        catch { return null; }
    }

    private static ImageSource? ImageFromFactory(IShellItemImageFactory f)
    {
        if (f.GetImage(new SIZE { cx = FactorySize, cy = FactorySize }, SIIGBF_ICONONLY, out var hbmp) != 0 || hbmp == IntPtr.Zero)
            return null;
        try
        {
            var bmp = Imaging.CreateBitmapSourceFromHBitmap(hbmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bmp.Freeze();
            // Shell namespace items can return a small icon centered in a much larger transparent
            // canvas through IShellItemImageFactory. Apply the same padding trim used by the image-list
            // path so virtual-folder icons do not appear tiny in menus and search results.
            return TrimCenteredPadding(bmp);
        }
        finally { DeleteObject(hbmp); }
    }

    private const int SIIGBF_BIGGERSIZEOK = 0x1; // allow a larger bitmap than requested (avoids upscaling)

    /// <summary>
    /// Fetches a large real thumbnail (video frame, document page, image) at up to <paramref name="size"/>
    /// pixels for the preview pane — no ICONONLY, so the shell returns the actual content thumbnail when it
    /// has one, and its native icon otherwise. Uncached; returns null on failure.
    /// </summary>
    public static ImageSource? TryGetPreviewThumbnail(string path, int size)
    {
        try
        {
            var iid = _iidImageFactory;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var f);
            try
            {
                if (f.GetImage(new SIZE { cx = size, cy = size }, SIIGBF_BIGGERSIZEOK, out var hbmp) != 0 || hbmp == IntPtr.Zero)
                    return null;
                try
                {
                    var bmp = Imaging.CreateBitmapSourceFromHBitmap(hbmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    bmp.Freeze();
                    return bmp;
                }
                finally { DeleteObject(hbmp); }
            }
            finally { Marshal.ReleaseComObject(f); }
        }
        catch { return null; }
    }

    private static ImageSource? FromImageList(int iIcon)
    {
        IImageList? list = null;
        try
        {
            if (SHGetImageList(CurrentShil(), ref _iidImageList, out list) < 0 || list == null)
                return null;
            if (list.GetIcon(iIcon, ILD_TRANSPARENT, out var hicon) < 0 || hicon == IntPtr.Zero)
                return null;
            try { return TrimCenteredPadding(FromHIcon(hicon)); }
            finally { ShellIconNativeMethods.DestroyIcon(hicon); }
        }
        catch (Exception ex)
        {
            Core.Logger.Log($"[ShellImageListInterop] Image list icon failed: {ex.Message}", Core.LogLevel.Warn);
            return null;
        }
        finally
        {
            if (list != null) Marshal.ReleaseComObject(list);
        }
    }

    /// <summary>Some icon resources registered for SHIL_JUMBO (256px) only actually ship a smaller
    /// resolution (e.g. 48px, as with dnSpy's .dll association -- see GitHub issue #102): Windows
    /// centers that smaller bitmap in the full 256px canvas rather than upscaling it, so once this
    /// app scales the result down to display size the icon renders as a tiny blob surrounded by
    /// transparent space. Cropping to the actual opaque content here lets normal image scaling fill
    /// the display size properly instead. Only trims when the real content is well short of filling
    /// the canvas (a legitimately full-size icon's anti-aliased edges still reach close to the
    /// border), so ordinary jumbo icons pass through unchanged.</summary>
    private static ImageSource TrimCenteredPadding(ImageSource source)
    {
        if (source is not BitmapSource bitmap) return source;
        try
        {
            // CreateBitmapSourceFromHIcon (the only caller) typically already yields Bgra32 or
            // Pbgra32 for an icon's alpha-having bitmap -- alpha lives at the same byte offset in
            // both regardless of premultiplication, so only convert for some other, unlikely format.
            var isAlreadyAlphaFormat = bitmap.Format == PixelFormats.Bgra32 || bitmap.Format == PixelFormats.Pbgra32;
            var converted = isAlreadyAlphaFormat ? bitmap : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            int w = converted.PixelWidth, h = converted.PixelHeight;
            if (w <= 0 || h <= 0) return source;

            var stride = w * 4;
            var pixels = new byte[stride * h];
            converted.CopyPixels(pixels, stride, 0);

            int left = w, right = -1, top = h, bottom = -1;
            for (var y = 0; y < h; y++)
            {
                var rowOffset = y * stride;
                for (var x = 0; x < w; x++)
                {
                    if (pixels[rowOffset + x * 4 + 3] <= 8) continue; // ignore near-invisible anti-aliasing noise
                    if (x < left) left = x;
                    if (x > right) right = x;
                    if (y < top) top = y;
                    if (y > bottom) bottom = y;
                }
            }

            if (right < left || bottom < top) return source; // fully transparent, nothing to trim

            var contentWidth = right - left + 1;
            var contentHeight = bottom - top + 1;
            if (contentWidth >= w * 0.85 && contentHeight >= h * 0.85) return source;

            var cropped = new CroppedBitmap(converted, new Int32Rect(left, top, contentWidth, contentHeight));
            cropped.Freeze();
            return cropped;
        }
        catch
        {
            return source;
        }
    }

    private static ImageSource FromHIcon(IntPtr hicon)
    {
        var bmp = Imaging.CreateBitmapSourceFromHIcon(hicon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        bmp.Freeze();
        return bmp;
    }
}
