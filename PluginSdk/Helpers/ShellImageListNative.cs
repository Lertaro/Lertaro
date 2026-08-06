using System.Runtime.InteropServices;

namespace Lertaro.PluginSdk.Helpers;

/// <summary>
/// Produces crisp shell icons. Primary path is IShellItemImageFactory.GetImage, which scales
/// icons to the requested size correctly (unlike SHGetImageList Jumbo, which *centers* small
/// icons on a 256px transparent canvas — making minimal-icon exes render tiny). Falls back to
/// the system image list HICON (caller renders it) when the factory is unavailable.
/// Shared via the SDK so every provider gets the same behavior.
/// </summary>
internal static class ShellImageListNative
{
    // ---- IShellItemImageFactory: correct size-scaled icon as an HBITMAP ----
    private const int SIIGBF_ICONONLY = 0x4;
    private static Guid _iidImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx; public int cy; public SIZE(int c) { cx = c; cy = c; } }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("shell32.dll", PreserveSig = false)]
    private static extern void SHCreateItemFromIDList(IntPtr pidl, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    /// <summary>Correctly-scaled icon HBITMAP for a real path, or Zero. Caller owns the HBITMAP.</summary>
    public static IntPtr GetShellHBitmap(string path, int size)
    {
        try
        {
            var iid = _iidImageFactory;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
            try
            {
                if (factory.GetImage(new SIZE(size), SIIGBF_ICONONLY, out var hbmp) == 0 && hbmp != IntPtr.Zero)
                    return hbmp;
            }
            finally { Marshal.ReleaseComObject(factory); }
        }
        catch { }
        return IntPtr.Zero;
    }

    /// <summary>Correctly-scaled icon HBITMAP for a shell PIDL, or Zero. Caller owns the HBITMAP.</summary>
    public static IntPtr GetShellHBitmapFromPidl(IntPtr pidl, int size)
    {
        try
        {
            var iid = _iidImageFactory;
            SHCreateItemFromIDList(pidl, ref iid, out var factory);
            try
            {
                if (factory.GetImage(new SIZE(size), SIIGBF_ICONONLY, out var hbmp) == 0 && hbmp != IntPtr.Zero)
                    return hbmp;
            }
            finally { Marshal.ReleaseComObject(factory); }
        }
        catch { }
        return IntPtr.Zero;
    }

    // ---- Fallback: system image-list HICON (caller renders / DestroyIcon) ----
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_PIDL = 0x000000008;
    private const uint SHGFI_SYSICONINDEX = 0x000004000;
    private const int SHIL_EXTRALARGE = 2; // 48px
    private const int SHIL_JUMBO = 4;      // 256px
    private const int ILD_TRANSPARENT = 1;
    private static Guid _iidImageList = new("46EB5926-582E-4017-9FDF-E8998DAA0950");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfo", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfoPidl(IntPtr pidl, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll")]
    private static extern int SHGetImageList(int iImageList, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IImageList ppv);

    [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        [PreserveSig] int Add();
        [PreserveSig] int ReplaceIcon();
        [PreserveSig] int SetOverlayImage();
        [PreserveSig] int Replace();
        [PreserveSig] int AddMasked();
        [PreserveSig] int Draw();
        [PreserveSig] int Remove();
        [PreserveSig] int GetIcon(int i, int flags, out IntPtr picon);
    }

    public static IntPtr GetHiResHIcon(string path, int size)
    {
        var shfi = new SHFILEINFO();
        if (SHGetFileInfo(path, 0, ref shfi, (uint)Marshal.SizeOf(shfi), SHGFI_SYSICONINDEX) != IntPtr.Zero)
        {
            var h = FromImageList(shfi.iIcon, size);
            if (h != IntPtr.Zero) return h;
        }
        var fb = new SHFILEINFO();
        return SHGetFileInfo(path, 0, ref fb, (uint)Marshal.SizeOf(fb), SHGFI_ICON | SHGFI_LARGEICON) != IntPtr.Zero ? fb.hIcon : IntPtr.Zero;
    }

    public static IntPtr GetHiResHIcon(IntPtr pidl, int size)
    {
        if (pidl == IntPtr.Zero) return IntPtr.Zero;
        try
        {
            var shfi = new SHFILEINFO();
            if (SHGetFileInfoPidl(pidl, 0, ref shfi, (uint)Marshal.SizeOf(shfi), SHGFI_SYSICONINDEX | SHGFI_PIDL) != IntPtr.Zero)
            {
                var h = FromImageList(shfi.iIcon, size);
                if (h != IntPtr.Zero) return h;
            }
            var fb = new SHFILEINFO();
            return SHGetFileInfoPidl(pidl, 0, ref fb, (uint)Marshal.SizeOf(fb), SHGFI_ICON | SHGFI_LARGEICON | SHGFI_PIDL) != IntPtr.Zero ? fb.hIcon : IntPtr.Zero;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static IntPtr FromImageList(int iIcon, int size)
    {
        var shil = size <= 48 ? SHIL_EXTRALARGE : SHIL_JUMBO;
        IImageList? list = null;
        try
        {
            if (SHGetImageList(shil, ref _iidImageList, out list) < 0 || list == null)
                return IntPtr.Zero;
            return list.GetIcon(iIcon, ILD_TRANSPARENT, out var h) == 0 ? h : IntPtr.Zero;
        }
        catch { return IntPtr.Zero; }
        finally { if (list != null) Marshal.ReleaseComObject(list); }
    }
}
