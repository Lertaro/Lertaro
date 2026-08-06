using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Lertaro.PluginSdk.Helpers;

/// <summary>
/// Utility class for resolving Windows shell folders, localized paths, and virtual folders.
/// Shared with plugins via the SDK.
/// </summary>
public static class ShellPathHelper
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfo", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfoPidl(IntPtr pidl, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName([MarshalAs(UnmanagedType.LPWStr)] string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SHGetPathFromIDListW(IntPtr pidl, [Out] StringBuilder pszPath);

    // Gets the PIDL from any Shell COM object (FolderItem, IShellItem, etc.).
    [DllImport("shell32.dll")]
    private static extern int SHGetIDListFromObject([MarshalAs(UnmanagedType.IUnknown)] object punk, out IntPtr ppidl);

    // GDI/User32 for HICON → HBITMAP conversion
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr hIcon, int cx, int cy, uint step, IntPtr hbr, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, uint nIcons);

    private const uint SHGFI_DISPLAYNAME = 0x000000200;
    private const uint SHGFI_PIDL = 0x000000008;
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;

    private static readonly Environment.SpecialFolder[] _trackedSpecialFolders = new[]
    {
        Environment.SpecialFolder.Desktop,
        Environment.SpecialFolder.MyDocuments,
        Environment.SpecialFolder.MyPictures,
        Environment.SpecialFolder.MyMusic,
        Environment.SpecialFolder.MyVideos,
        Environment.SpecialFolder.UserProfile
    };

    /// <summary>
    /// Retrieves the localized user-friendly display name of a physical folder.
    /// </summary>
    public static string GetLocalizedFolderName(string physicalPath)
    {
        try
        {
            var shfi = new SHFILEINFO();
            var res = SHGetFileInfo(physicalPath, 0, ref shfi, (uint)Marshal.SizeOf(shfi), SHGFI_DISPLAYNAME);
            if (res != IntPtr.Zero && !string.IsNullOrEmpty(shfi.szDisplayName))
            {
                return shfi.szDisplayName.Trim();
            }
        }
        catch { }
        return Path.GetFileName(physicalPath) ?? string.Empty;
    }

    /// <summary>
    /// Resolves a localized folder name (e.g. "Desktop", "Downloads") to its absolute physical path.
    /// </summary>
    public static string ResolveSpecialFolder(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        name = name.Trim();
        foreach (var folderType in _trackedSpecialFolders)
        {
            try
            {
                var specialPath = Environment.GetFolderPath(folderType);
                if (!string.IsNullOrEmpty(specialPath) &&
                    (string.Equals(name, Path.GetFileName(specialPath), StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(name, GetLocalizedFolderName(specialPath), StringComparison.OrdinalIgnoreCase)))
                    return specialPath;
            }
            catch { }
        }
        try
        {
            var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (Directory.Exists(downloadsPath) &&
                (string.Equals(name, Path.GetFileName(downloadsPath), StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(name, GetLocalizedFolderName(downloadsPath), StringComparison.OrdinalIgnoreCase)))
                return downloadsPath;
        }
        catch { }
        return name;
    }

    /// <summary>
    /// Dynamically resolves a Windows shell virtual path (e.g. ::{450d8fba-...} or shell:::{...}) to its physical folder path.
    /// Returns the original path if it cannot be resolved.
    /// </summary>
    public static string TryResolveVirtualPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;

        if (path.StartsWith("::") || path.StartsWith("shell:"))
        {
            var pidl = IntPtr.Zero;
            try
            {
                var hr = SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out _);
                if (hr == 0 && pidl != IntPtr.Zero)
                {
                    var sb = new StringBuilder(260);
                    if (SHGetPathFromIDListW(pidl, sb))
                    {
                        var resolved = sb.ToString();
                        if (!string.IsNullOrEmpty(resolved) && (Directory.Exists(resolved) || File.Exists(resolved)))
                        {
                            return resolved;
                        }
                    }
                }
            }
            catch { }
            finally
            {
                if (pidl != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pidl);
                }
            }
        }
        return path;
    }

    /// <summary>
    /// Dynamically retrieves the localized user-friendly display name of a Windows shell virtual folder.
    /// </summary>
    public static string GetVirtualFolderDisplayName(string path, string fallback)
    {
        if (string.IsNullOrEmpty(path)) return fallback;
        var pidl = IntPtr.Zero;
        try
        {
            if (SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out _) == 0 && pidl != IntPtr.Zero)
            {
                var shfi = new SHFILEINFO();
                var res = SHGetFileInfoPidl(pidl, 0, ref shfi, (uint)Marshal.SizeOf(shfi), SHGFI_DISPLAYNAME | SHGFI_PIDL);
                if (res != IntPtr.Zero && !string.IsNullOrEmpty(shfi.szDisplayName))
                    return shfi.szDisplayName.Trim();
            }
        }
        catch { }
        finally { if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl); }
        return fallback;
    }

    private static IntPtr HIconToHBitmap(IntPtr hIcon, int size)
    {
        if (hIcon == IntPtr.Zero) return IntPtr.Zero;
        var hdc = IntPtr.Zero;
        var hMemDC = IntPtr.Zero;
        var hBmp = IntPtr.Zero;
        try
        {
            hdc = GetDC(IntPtr.Zero);
            hMemDC = CreateCompatibleDC(hdc);
            hBmp = CreateCompatibleBitmap(hdc, size, size);
            var hOld = SelectObject(hMemDC, hBmp);
            DrawIconEx(hMemDC, 0, 0, hIcon, size, size, 0, IntPtr.Zero, 0x0003 /* DI_NORMAL */);
            SelectObject(hMemDC, hOld);
            return hBmp;
        }
        catch
        {
            if (hBmp != IntPtr.Zero) DeleteObject(hBmp);
            return IntPtr.Zero;
        }
        finally
        {
            DestroyIcon(hIcon);
            if (hMemDC != IntPtr.Zero) DeleteDC(hMemDC);
            if (hdc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    /// <summary>
    /// Gets an icon HBITMAP for a Shell.Application FolderItem COM object via its PIDL.
    /// Works for virtual Shell namespace items (e.g. GodMode, Control Panel applets) whose
    /// path strings cannot be parsed by SHParseDisplayName.
    /// Caller must free the returned HBITMAP with DeleteObject when no longer needed.
    /// Returns IntPtr.Zero if the icon cannot be retrieved.
    /// </summary>
    public static IntPtr TryGetIconHBitmapForShellItem(object comObj, int size = 96)
    {
        if (comObj == null) return IntPtr.Zero;
        var pidl = IntPtr.Zero;
        try
        {
            if (SHGetIDListFromObject(comObj, out pidl) != 0 || pidl == IntPtr.Zero) return IntPtr.Zero;
            var direct = ShellImageListNative.GetShellHBitmapFromPidl(pidl, size);
            if (direct != IntPtr.Zero) return direct;
            return HIconToHBitmap(ShellImageListNative.GetHiResHIcon(pidl, size), size);
        }
        catch { return IntPtr.Zero; }
        finally { if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl); }
    }

    /// <summary>
    /// Gets an icon HBITMAP for a physical file or directory path.
    /// Caller must free the returned HBITMAP with DeleteObject when no longer needed.
    /// </summary>
    public static IntPtr GetIconHBitmapForPath(string path, int size = 96)
    {
        if (string.IsNullOrEmpty(path)) return IntPtr.Zero;
        if (path.EndsWith(".msc", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
        {
            try
            {
                var hIcon = ExtractMscHIcon(path, size);
                if (hIcon != IntPtr.Zero)
                    return HIconToHBitmap(hIcon, size);
            }
            catch { }
        }
        var direct = ShellImageListNative.GetShellHBitmap(path, size);
        if (direct != IntPtr.Zero) return direct;
        try
        {
            var hIcon = ShellImageListNative.GetHiResHIcon(path, size);
            return HIconToHBitmap(hIcon, size);
        }
        catch { return IntPtr.Zero; }
    }

    public static IntPtr ExtractMscHIcon(string mscPath, int size)
    {
        try
        {
            var content = File.ReadAllText(mscPath);
            var start = content.IndexOf("<Icon ", StringComparison.OrdinalIgnoreCase);
            if (start < 0) return IntPtr.Zero;
            var end = content.IndexOf(">", start);
            if (end < 0) return IntPtr.Zero;
            var tag = content.Substring(start, end - start);
            var fileMatch = System.Text.RegularExpressions.Regex.Match(tag, @"File\s*=\s*""([^""]+)""", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var indexMatch = System.Text.RegularExpressions.Regex.Match(tag, @"Index\s*=\s*""([^""]+)""", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (fileMatch.Success)
            {
                var iconPath = Environment.ExpandEnvironmentVariables(fileMatch.Groups[1].Value);
                if (!File.Exists(iconPath))
                {
                    var winIdx = iconPath.IndexOf(@"\Windows\", StringComparison.OrdinalIgnoreCase);
                    if (winIdx >= 0)
                    {
                        var sysRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
                        var altPath = Path.Combine(sysRoot, iconPath.Substring(winIdx + 9));
                        if (File.Exists(altPath)) iconPath = altPath;
                    }
                }
                var iconIndex = (indexMatch.Success && int.TryParse(indexMatch.Groups[1].Value, out var parsed)) ? parsed : 0;
                if (File.Exists(iconPath))
                {
                    var largeIcons = new IntPtr[1];
                    if (ExtractIconEx(iconPath, iconIndex, largeIcons, null, 1) > 0 && largeIcons[0] != IntPtr.Zero)
                        return largeIcons[0];
                }
            }
        }
        catch { }
        return IntPtr.Zero;
    }
}
