using System.Runtime.InteropServices;
using System.Text;

namespace Lertaro.Plugins.CoreExtensions.Shell.ContextMenu;

internal static class ShellContextMenuNativeMethods
{
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214E4-0000-0000-C000-000000000046")]
    public interface IContextMenu
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

        [PreserveSig]
        int InvokeCommand(ref CMINVOKECOMMANDINFO lpici);

        [PreserveSig]
        int GetCommandString(UIntPtr idCmd, uint uFlags, IntPtr pwzReserved, StringBuilder pszName, uint cchMax);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F4-0000-0000-C000-000000000046")]
    public interface IContextMenu2
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

        [PreserveSig]
        int InvokeCommand(ref CMINVOKECOMMANDINFO lpici);

        [PreserveSig]
        int GetCommandString(UIntPtr idCmd, uint uFlags, IntPtr pwzReserved, StringBuilder pszName, uint cchMax);

        [PreserveSig]
        int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("340E1B03-087E-11D1-9E99-00A0C91110C3")]
    public interface IContextMenu3
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

        [PreserveSig]
        int InvokeCommand(ref CMINVOKECOMMANDINFO lpici);

        [PreserveSig]
        int GetCommandString(UIntPtr idCmd, uint uFlags, IntPtr pwzReserved, StringBuilder pszName, uint cchMax);

        [PreserveSig]
        int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);

        [PreserveSig]
        int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr plResult);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    public interface IShellFolder
    {
        void ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName, out uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);
        void EnumObjects(IntPtr hwnd, uint grfFlags, out IntPtr ppenumIDList);
        void BindToObject(IntPtr pidl, IntPtr pbc, [In] ref Guid riid, out IntPtr ppv);
        void BindToStorage(IntPtr pidl, IntPtr pbc, [In] ref Guid riid, out IntPtr ppv);
        void CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        void CreateViewObject(IntPtr hwndOwner, [In] ref Guid riid, out IntPtr ppv);
        void GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref uint rgfInOut);
        void GetUIObjectOf(IntPtr hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, [In] ref Guid riid, ref uint rgfReserved, out IntPtr ppv);
        void GetDisplayNameOf(IntPtr pidl, uint uFlags, out STRRET pName);
        void SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CMINVOKECOMMANDINFO
    {
        public int cbSize;
        public int fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public int dwHotKey;
        public IntPtr hIcon;
    }

    [StructLayout(LayoutKind.Explicit, Size = 264)]
    public struct STRRET
    {
        [FieldOffset(0)]
        public uint uType;
        [FieldOffset(4)]
        public IntPtr pOleStr;
        [FieldOffset(4)]
        public uint uOffset;
        [FieldOffset(4)]
        public IntPtr pString;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MENUITEMINFOW
    {
        public uint cbSize;
        public uint fMask;
        public uint fType;
        public uint fState;
        public uint wID;
        public IntPtr hSubMenu;
        public IntPtr hbmpChecked;
        public IntPtr hbmpUnchecked;
        public UIntPtr dwItemData;
        public IntPtr dwTypeData;
        public uint cch;
        public IntPtr hbmpItem;
    }

    public const uint MIIM_STATE = 0x00000001;
    public const uint MIIM_ID = 0x00000002;
    public const uint MIIM_SUBMENU = 0x00000004;
    public const uint MIIM_STRING = 0x00000040;
    public const uint MIIM_BITMAP = 0x00000080;
    public const uint MIIM_FTYPE = 0x00000100;
    public const uint MFT_SEPARATOR = 0x00000800;
    public const uint MFS_DISABLED = 0x00000003;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHParseDisplayName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszName,
        IntPtr pbc,
        out IntPtr ppidl,
        uint sfgaoIn,
        out uint psfgaoOut
    );

    [DllImport("shell32.dll")]
    public static extern int SHBindToParent(
        IntPtr pidl,
        [In] ref Guid riid,
        out IntPtr ppv,
        ref IntPtr ppidlLast
    );

    [DllImport("user32.dll")]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    public static extern int TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern int GetMenuItemCount(IntPtr hMenu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMenuItemInfoW(
        IntPtr hMenu, uint uItem,
        [MarshalAs(UnmanagedType.Bool)] bool fByPosition,
        ref MENUITEMINFOW lpmii);

    [DllImport("ole32.dll")]
    public static extern void CoTaskMemFree(IntPtr pv);
}
