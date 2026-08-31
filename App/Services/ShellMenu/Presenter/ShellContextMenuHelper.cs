using System.Runtime.InteropServices;

namespace Lertaro.App.Services.ShellMenu.Presenter;

public static class ShellContextMenuHelper
{
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214E4-0000-0000-C000-000000000046")]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFO lpici);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F4-0000-0000-C000-000000000046")]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFO lpici);
        void GetCommandString();
        [PreserveSig] int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("340E1B03-087E-11D1-9E99-00A0C91110C3")]
    private interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFO lpici);
        void GetCommandString();
        [PreserveSig] int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr plResult);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    private interface IShellFolder
    {
        void ParseDisplayName(); void EnumObjects(); void BindToObject(); void BindToStorage(); void CompareIDs(); void CreateViewObject(); void GetAttributesOf();
        void GetUIObjectOf(IntPtr hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, [In] ref Guid riid, ref uint rgfReserved, out IntPtr ppv);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CMINVOKECOMMANDINFO
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MENUITEMINFOW
    {
        public uint cbSize, fMask, fType, fState, wID;
        public IntPtr hSubMenu, hbmpChecked, hbmpUnchecked;
        public UIntPtr dwItemData;
        public IntPtr dwTypeData;
        public uint cch;
        public IntPtr hbmpItem;
    }

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName([MarshalAs(UnmanagedType.LPWStr)] string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);
    [DllImport("shell32.dll")] private static extern int SHBindToParent(IntPtr pidl, [In] ref Guid riid, out IntPtr ppv, ref IntPtr ppidlLast);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyMenu(IntPtr hMenu);
    [DllImport("user32.dll")] private static extern int TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out ulong lpPoint);
    [DllImport("ole32.dll")] private static extern void CoTaskMemFree(IntPtr pv);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc lpCallback, IntPtr uIdSubclass, IntPtr dwRefData);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc lpCallback, IntPtr uIdSubclass);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(IntPtr hMenu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMenuItemInfoW(IntPtr hMenu, uint uItem, [MarshalAs(UnmanagedType.Bool)] bool fByPosition, ref MENUITEMINFOW lpmii);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteMenu(IntPtr hMenu, uint uPosition, uint uFlags);

    private const uint TPM_RETURNCMD = 0x0100, TPM_LEFTBUTTON = 0x0000, CMD_FIRST = 1, CMD_LAST = 30000;

    [ThreadStatic] private static IContextMenu2? _currentContextMenu2;
    [ThreadStatic] private static IContextMenu3? _currentContextMenu3;

    private static readonly SubclassProc _subclassProc = HookWndProc;

    private static IntPtr HookWndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
    {
        const uint WM_INITMENUPOPUP = 0x0117;
        const uint WM_DRAWITEM = 0x002B;
        const uint WM_MEASUREITEM = 0x002C;
        const uint WM_MENUCHAR = 0x0120;

        if (uMsg == WM_INITMENUPOPUP || uMsg == WM_DRAWITEM || uMsg == WM_MEASUREITEM || uMsg == WM_MENUCHAR)
        {
            if (_currentContextMenu3 != null)
            {
                if (_currentContextMenu3.HandleMenuMsg2(uMsg, wParam, lParam, out var lResult) >= 0)
                {
                    return lResult;
                }
            }
            else if (_currentContextMenu2 != null)
            {
                if (_currentContextMenu2.HandleMenuMsg(uMsg, wParam, lParam) >= 0)
                {
                    return IntPtr.Zero;
                }
            }
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private static void DeduplicateMenu(IntPtr hMenu)
    {
        var count = GetMenuItemCount(hMenu);
        if (count <= 0) return;

        var seenTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const uint MIIM_STRING = 0x00000040;
        const uint MIIM_FTYPE = 0x00000100;
        const uint MFT_SEPARATOR = 0x00000800;
        const uint MF_BYPOSITION = 0x00000400;

        for (var i = count - 1; i >= 0; i--)
        {
            var mii = new MENUITEMINFOW();
            mii.cbSize = (uint)Marshal.SizeOf<MENUITEMINFOW>();
            mii.fMask = MIIM_FTYPE;
            if (!GetMenuItemInfoW(hMenu, (uint)i, true, ref mii)) continue;

            if ((mii.fType & MFT_SEPARATOR) != 0) continue;

            mii.fMask = MIIM_STRING;
            mii.dwTypeData = IntPtr.Zero;
            mii.cch = 0;
            if (!GetMenuItemInfoW(hMenu, (uint)i, true, ref mii)) continue;

            if (mii.cch > 0)
            {
                var length = mii.cch + 1;
                var buffer = Marshal.AllocHGlobal((int)length * 2);
                try
                {
                    mii.dwTypeData = buffer;
                    mii.cch = length;
                    if (GetMenuItemInfoW(hMenu, (uint)i, true, ref mii))
                    {
                        var text = Marshal.PtrToStringUni(buffer) ?? "";
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            var cleanText = text.Replace("&", "").Trim();
                            if (seenTexts.Contains(cleanText))
                            {
                                DeleteMenu(hMenu, (uint)i, MF_BYPOSITION);
                            }
                            else
                            {
                                seenTexts.Add(cleanText);
                            }
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }
    }

    public static void Show(string path, IntPtr hwndOwner)
    {
        if (string.IsNullOrEmpty(path)) return;

        var pidl = IntPtr.Zero;
        var parentFolderPtr = IntPtr.Zero;
        var contextMenuPtr = IntPtr.Zero;
        var hMenu = IntPtr.Zero;
        var pIContextMenu2 = IntPtr.Zero;
        var pIContextMenu3 = IntPtr.Zero;
        var subclassed = false;
        var lpDir = IntPtr.Zero;
        IShellFolder? parentFolder = null;
        IContextMenu? contextMenu = null;
        IContextMenu2? contextMenu2 = null;
        IContextMenu3? contextMenu3 = null;

        try
        {
            if (SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out _) < 0) return;

            var iidIShellFolder = new Guid("000214E6-0000-0000-C000-000000000046");
            var relativePidl = IntPtr.Zero;
            if (SHBindToParent(pidl, ref iidIShellFolder, out parentFolderPtr, ref relativePidl) < 0) return;

            parentFolder = (IShellFolder)Marshal.GetObjectForIUnknown(parentFolderPtr);
            var iidIContextMenu = new Guid("000214E4-0000-0000-C000-000000000046");
            uint reserved = 0;
            parentFolder.GetUIObjectOf(IntPtr.Zero, 1, new[] { relativePidl }, ref iidIContextMenu, ref reserved, out contextMenuPtr);

            contextMenu = (IContextMenu)Marshal.GetObjectForIUnknown(contextMenuPtr);
            hMenu = CreatePopupMenu();
            if (hMenu == IntPtr.Zero) return;

            contextMenu.QueryContextMenu(hMenu, 0, CMD_FIRST, CMD_LAST, 0x00000000);

            DeduplicateMenu(hMenu);

            var iidIContextMenu2 = new Guid("000214F4-0000-0000-C000-000000000046");
            var iidIContextMenu3 = new Guid("340E1B03-087E-11D1-9E99-00A0C91110C3");

            if (Marshal.QueryInterface(contextMenuPtr, in iidIContextMenu3, out pIContextMenu3) == 0)
            {
                contextMenu3 = (IContextMenu3)Marshal.GetObjectForIUnknown(pIContextMenu3);
                _currentContextMenu3 = contextMenu3;
            }
            else if (Marshal.QueryInterface(contextMenuPtr, in iidIContextMenu2, out pIContextMenu2) == 0)
            {
                contextMenu2 = (IContextMenu2)Marshal.GetObjectForIUnknown(pIContextMenu2);
                _currentContextMenu2 = contextMenu2;
            }

            if ((_currentContextMenu3 != null || _currentContextMenu2 != null) && hwndOwner != IntPtr.Zero)
            {
                subclassed = SetWindowSubclass(hwndOwner, _subclassProc, new IntPtr(1001), IntPtr.Zero);
            }

            if (!GetCursorPos(out var lpPoint)) return;
            var x = (int)(lpPoint & 0xFFFFFFFF);
            var y = (int)(lpPoint >> 32);

            var cmd = (uint)TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_LEFTBUTTON, x, y, hwndOwner, IntPtr.Zero);
            if (cmd >= CMD_FIRST && cmd <= CMD_LAST)
            {
                var workingDir = System.IO.Directory.Exists(path) ? path : System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(workingDir)) lpDir = Marshal.StringToHGlobalAnsi(workingDir);

                var info = new CMINVOKECOMMANDINFO
                {
                    cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFO>(),
                    hwnd = hwndOwner,
                    lpVerb = (IntPtr)(cmd - CMD_FIRST),
                    lpDirectory = lpDir,
                    nShow = 1
                };
                contextMenu.InvokeCommand(ref info);
            }
        }
        catch
        {
            // Fail silently
        }
        finally
        {
            if (lpDir != IntPtr.Zero) Marshal.FreeHGlobal(lpDir);
            if (subclassed) RemoveWindowSubclass(hwndOwner, _subclassProc, new IntPtr(1001));
            _currentContextMenu3 = null;
            _currentContextMenu2 = null;
            if (contextMenu3 != null) Marshal.ReleaseComObject(contextMenu3);
            if (contextMenu2 != null) Marshal.ReleaseComObject(contextMenu2);
            if (contextMenu != null) Marshal.ReleaseComObject(contextMenu);
            if (parentFolder != null) Marshal.ReleaseComObject(parentFolder);
            if (pIContextMenu3 != IntPtr.Zero) Marshal.Release(pIContextMenu3);
            if (pIContextMenu2 != IntPtr.Zero) Marshal.Release(pIContextMenu2);
            if (hMenu != IntPtr.Zero) DestroyMenu(hMenu);
            if (contextMenuPtr != IntPtr.Zero) Marshal.Release(contextMenuPtr);
            if (parentFolderPtr != IntPtr.Zero) Marshal.Release(parentFolderPtr);
            if (pidl != IntPtr.Zero) CoTaskMemFree(pidl);
        }
    }

}
