using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Lertaro.Plugins.CoreExtensions.InlineSearch;

internal static class ExplorerAdapterHelpers
{
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public static string GetProcessName(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != 0)
            {
                using var proc = Process.GetProcessById((int)pid);
                return proc.ProcessName;
            }
        }
        catch { }
        return "Unknown";
    }

    public static dynamic? FindExplorerWindow(IntPtr explorerHwnd)
    {
        var shellWindowsType = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"));
        if (shellWindowsType == null) return null;

        // 1. Find the active tab HWND in Z-order
        var activeTabHwnd = IntPtr.Zero;
        EnumChildWindows(explorerHwnd, (childHwnd, lParam) =>
        {
            var sbChildClass = new StringBuilder(256);
            GetClassName(childHwnd, sbChildClass, sbChildClass.Capacity);
            var childClass = sbChildClass.ToString();

            if (childClass.Equals("ShellTabWindowClass", StringComparison.OrdinalIgnoreCase))
            {
                activeTabHwnd = childHwnd;
                return false; // Stop enumeration immediately
            }
            return true;
        }, IntPtr.Zero);

        object? shellWindows = null;
        try
        {
            shellWindows = Activator.CreateInstance(shellWindowsType);
            if (shellWindows == null) return null;

            dynamic dShellWindows = shellWindows;
            int count = dShellWindows.Count;
            object? match = null;
            for (var i = 0; i < count; i++)
            {
                object? window = null;
                try
                {
                    window = dShellWindows.Item(i);
                    if (window == null) continue;

                    dynamic dWindow = window;
                    if ((IntPtr)dWindow.HWND == explorerHwnd)
                    {
                        // 2. Verify if this COM window matches the active tab HWND
                        if (activeTabHwnd != IntPtr.Zero)
                        {
                            if (window is IComServiceProvider serviceProvider)
                            {
                                var serviceId = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837"); // SID_STopLevelBrowser
                                var interfaceId = new Guid("000214E2-0000-0000-C000-000000000046"); // IID_IShellBrowser

                                var hr = serviceProvider.QueryService(ref serviceId, ref interfaceId, out var shellBrowserPtr);
                                if (hr == 0 && shellBrowserPtr != IntPtr.Zero)
                                {
                                    try
                                    {
                                        var shellBrowser = (IShellBrowser)Marshal.GetObjectForIUnknown(shellBrowserPtr);
                                        try
                                        {
                                            shellBrowser.GetWindow(out var tabHwnd);
                                            if (tabHwnd != activeTabHwnd)
                                                continue; // Skip inactive tab
                                        }
                                        finally
                                        {
                                            Marshal.ReleaseComObject(shellBrowser);
                                        }
                                    }
                                    finally
                                    {
                                        Marshal.Release(shellBrowserPtr);
                                    }
                                }
                            }
                        }
                        match = window;
                        window = null;
                        break;
                    }
                }
                catch { }
                finally
                {
                    ReleaseComObject(window);
                }
            }

            return match;
        }
        finally
        {
            ReleaseComObject(shellWindows);
        }
    }

    // Synchronous (Thread.Sleep, not await Task.Delay) and meant to be called from the same dedicated STA
    // thread InlineAdapterCommandHandler.RunOnSta already spins up per call -- these Shell.Application COM
    // objects are STA-affine, and that thread never pumps a message loop (no Application.Run/Dispatcher.Run
    // on it), so a Task.Delay continuation would silently resume on a ThreadPool thread instead of the
    // original STA thread, and every call on `window`/`folder`/`item` after that await would then be a
    // cross-apartment call with no proxy to marshal it -- which fails and gets swallowed by the catch below,
    // so navigation would visibly work while selection silently never happened.
    public static void SelectItemInExplorerLater(string path, IntPtr explorerHwnd)
    {
        Thread.Sleep(250);
        dynamic? window = null;
        dynamic? folder = null;
        object? item = null;
        try
        {
            window = FindExplorerWindow(explorerHwnd);
            if (window == null) return;

            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) return;

            folder = window.Document.Folder;
            if (folder == null) return;
            item = folder.ParseName(name);
            if (item == null) return;

            const int svsiSelect = 0x1;
            const int svsiDeselectOthers = 0x4;
            const int svsiEnsureVisible = 0x8;
            window.Document.SelectItem(item, svsiSelect | svsiDeselectOthers | svsiEnsureVisible);
        }
        catch { }
        finally
        {
            ReleaseComObject(item);
            ReleaseComObject(folder);
            ReleaseComObject(window);
        }
    }

    private static void ReleaseComObject(object? comObject)
    {
        try
        {
            if (comObject != null && Marshal.IsComObject(comObject))
                Marshal.ReleaseComObject(comObject);
        }
        catch
        {
            // Best-effort cleanup; the RCW will still be reclaimed by the GC finalizer.
        }
    }

    #region COM Interfaces for Tab Resolution
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    private interface IComServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214E2-0000-0000-C000-000000000046")]
    private interface IShellBrowser
    {
        [PreserveSig]
        int GetWindow(out IntPtr phwnd);
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(IntPtr hwndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);
    #endregion
}
