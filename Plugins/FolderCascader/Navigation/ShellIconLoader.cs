using System.Runtime.InteropServices;

namespace Lertaro.Plugins.FolderCascader.Navigation;

/// <summary>
/// Loads shell icons as HBITMAPs for the navigation menu. Fetches a high-resolution
/// icon from the system image list (256px Jumbo / 48px ExtraLarge) and renders it down
/// to a fixed 64px premultiplied bitmap so it stays crisp and matches the plugin's other
/// 64px menu glyphs (star/clock). Own copy — plugins ship as independent DLLs.
/// </summary>
internal static class ShellIconLoader
{
    private const uint SHGFI_SYSICONINDEX = 0x000004000;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

    private const int SHIL_SMALL = 1;
    private const int SHIL_EXTRALARGE = 2; // 48px
    private const int SHIL_JUMBO = 4;      // 256px
    private const int ILD_TRANSPARENT = 1;
    private const int RenderSize = 64;     // match the plugin's custom 64px menu glyphs

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFOW
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [ComImport]
    [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);
        [PreserveSig] int ReplaceIcon(int i, IntPtr hIcon, ref int pi);
        [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
        [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
        [PreserveSig] int AddMasked(IntPtr hbmImage, uint crMask, ref int pi);
        [PreserveSig] int Draw(IntPtr pimldp);
        [PreserveSig] int Remove(int i);
        [PreserveSig] int GetIcon(int i, int flags, out IntPtr picon);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfoW(string pszPath, uint dwFileAttributes, ref SHFILEINFOW pszFileInfo, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList ppv);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static readonly Guid IID_IImageList = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");

    public static IntPtr GetIconHBitmap(string path, bool isDir)
    {
        var shfi = new SHFILEINFOW();
        var flags = SHGFI_SYSICONINDEX | SHGFI_USEFILEATTRIBUTES;
        var attributes = isDir ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;

        var res = SHGetFileInfoW(path, attributes, ref shfi, (uint)Marshal.SizeOf(shfi), flags);
        if (res == IntPtr.Zero)
            return IntPtr.Zero;

        var iid = IID_IImageList;
        // Prefer the highest-resolution image list available, degrade gracefully.
        foreach (var shil in new[] { SHIL_JUMBO, SHIL_EXTRALARGE, SHIL_SMALL })
        {
            if (SHGetImageList(shil, ref iid, out var imageList) != 0 || imageList == null)
                continue;

            if (imageList.GetIcon(shfi.iIcon, ILD_TRANSPARENT, out var hIcon) == 0 && hIcon != IntPtr.Zero)
            {
                try { return RenderHIconToHBitmap(hIcon, RenderSize); }
                catch { return IntPtr.Zero; }
                finally { DestroyIcon(hIcon); }
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>Renders an HICON (any native size) into a fixed-size premultiplied HBITMAP.</summary>
    private static IntPtr RenderHIconToHBitmap(IntPtr hIcon, int size)
    {
        var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
            hIcon, System.Windows.Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());

        var visual = new System.Windows.Media.DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            System.Windows.Media.RenderOptions.SetBitmapScalingMode(visual, System.Windows.Media.BitmapScalingMode.HighQuality);
            dc.DrawImage(src, new System.Windows.Rect(0, 0, size, size));
        }

        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(size, size, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(visual);

        var stride = size * 4;
        var pixels = new byte[size * stride];
        rtb.CopyPixels(pixels, stride, 0);

        using var bmp = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        var rect = new System.Drawing.Rectangle(0, 0, size, size);
        var bmpData = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);
        Marshal.Copy(pixels, 0, bmpData.Scan0, pixels.Length);
        bmp.UnlockBits(bmpData);

        return bmp.GetHbitmap();
    }
}
