using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Lertaro.App.Services;

/// <summary>
/// The one place this app talks to a live Explorer window: which window, which of its tabs, and driving
/// that tab's own Shell.Application object.
/// </summary>
/// <remarks>
/// Consolidated from ExplorerLocateHelper and ExplorerTabLocator, which each carried a private copy of the
/// same three steps (walk the ShellWindows collection, match an item to a tab window, call Navigate2 /
/// SelectItem on it). Two copies meant two chances to get the tab match wrong, which is exactly what
/// happened -- see <see cref="GetActiveTabHandle"/> for the lookup that had been silently failing.
/// </remarks>
internal static class ExplorerShellWindowsHelper
{
    private const string ExplorerWindowClass = "CabinetWClass";
    private const string TabWindowClass = "ShellTabWindowClass";
    private const int MatchPollMs = 50;
    private static readonly TimeSpan ShellMatchTimeout = TimeSpan.FromSeconds(2);
    private static readonly Guid ShellWindowsClsid = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid ShellBrowserService = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid ShellBrowserInterface = new("000214E2-0000-0000-C000-000000000046");

    /// <summary>
    /// The tab window an Explorer window is currently showing, or <see cref="IntPtr.Zero"/>.
    /// </summary>
    /// <remarks>
    /// Two things this must not do. It must not look for a DIRECT child: a current Windows 11 Explorer
    /// window has no direct children at all (the tab windows sit under a DesktopChildSiteBridge), so a
    /// FindWindowEx from the top-level window finds nothing and reports "no tabs" for a window full of
    /// them -- that is why "open in a new tab" quietly degraded to opening a new window on those builds.
    /// And it must not pick arbitrarily: EnumChildWindows returns the ACTIVE tab's window first, verified
    /// against a live two-tab window by switching tabs through UI Automation and watching the order flip.
    /// </remarks>
    public static IntPtr GetActiveTabHandle(IntPtr explorerHwnd) => FindDescendants(explorerHwnd, TabWindowClass).FirstOrDefault();

    /// <summary>
    /// Every tab window of an Explorer window, so a caller can diff a before/after pair and pick up the
    /// tab it just asked for.
    /// </summary>
    public static HashSet<IntPtr> GetTabHandles(IntPtr explorerHwnd) => [.. FindDescendants(explorerHwnd, TabWindowClass)];

    public static bool IsExplorerWindow(IntPtr hwnd) => hwnd != IntPtr.Zero && HasClassName(hwnd, ExplorerWindowClass);

    /// <summary>
    /// The Explorer window a tab request should target: the one it was handed, else the foreground one,
    /// else the first Explorer window that actually has a tab.
    /// </summary>
    public static IntPtr FindExplorerWindowHandle(IntPtr preferredHwnd)
    {
        if (IsExplorerWindow(preferredHwnd)) return preferredHwnd;

        var foreground = GetForegroundWindow();
        if (IsExplorerWindow(foreground)) return foreground;

        var window = IntPtr.Zero;
        while (true)
        {
            window = FindWindowEx(IntPtr.Zero, window, ExplorerWindowClass, null);
            if (window == IntPtr.Zero) return IntPtr.Zero;
            if (GetActiveTabHandle(window) != IntPtr.Zero) return window;
        }
    }

    /// <summary>
    /// The Shell.Application window object for one Explorer tab, or null when there is none.
    /// </summary>
    /// <remarks>
    /// ShellWindows exposes one COM item per TAB, and every item of the same window reports that same
    /// top-level HWND -- so a window handle alone cannot tell two tabs apart, and matching on it alone
    /// returns whichever tab happens to enumerate first. The item's IShellBrowser tab handle is what
    /// identifies the tab. A zero <paramref name="tabHwnd"/> means "any tab of that window" (Explorer
    /// builds without tabs), and a zero <paramref name="cabinetHwnd"/> means "that tab, in whichever
    /// window owns it". A tab that was just created needs <paramref name="waitForMatch"/>: its
    /// Shell.Application object appears a beat after its window handle does.
    ///
    /// Wants a thread free to make an outgoing COM call: IShellBrowser::GetWindow answers with
    /// RPC_E_CANTCALLOUT_ININPUTSYNCCALL when it is called from a thread already inside an
    /// input-synchronous call (a UI thread handling a message it was sent) -- measured, not assumed, and
    /// one more reason every caller reaches this from a ShellThread.
    /// </remarks>
    public static object? FindShellWindowForTab(IntPtr tabHwnd, IntPtr cabinetHwnd = default, bool waitForMatch = false)
    {
        var deadline = waitForMatch ? Stopwatch.GetTimestamp() + Stopwatch.Frequency * 2 : 0;
        while (true)
        {
            var match = FindShellWindowForTabOnce(tabHwnd, cabinetHwnd);
            if (match != null || deadline == 0 || Stopwatch.GetTimestamp() >= deadline) return match;
            Thread.Sleep(MatchPollMs);
        }
    }

    /// <summary>
    /// Points one Explorer tab at <paramref name="folder"/> and, when an item name is given, selects that
    /// item once the navigation has landed.
    /// </summary>
    /// <remarks>
    /// Navigate2 returns as soon as the navigation STARTS, so the selection is polled for rather than
    /// assumed. This used to be a fixed 250 ms sleep, which wasted that time on a folder that was already
    /// there and was not always enough on a slow or network one. Blocking here is fine: callers run on a
    /// ShellThread, a thread-per-call worker built for exactly this kind of shell work, which also keeps
    /// every COM call on the STA the objects were created on.
    /// </remarks>
    public static void NavigateAndSelect(object shellWindow, string folder, string? itemName)
    {
        dynamic window = shellWindow;
        window.Navigate2(folder);

        var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 2;
        while (Stopwatch.GetTimestamp() < deadline)
        {
            try
            {
                dynamic document = window.Document;
                dynamic shellFolder = document.Folder;
                if (string.IsNullOrEmpty(itemName))
                {
                    if (PathsEqual(shellFolder.Self.Path as string, folder)) return;
                }
                else
                {
                    var item = shellFolder.ParseName(itemName);
                    if (item != null)
                    {
                        const int select = 0x1;
                        const int deselectOthers = 0x4;
                        const int ensureVisible = 0x8;
                        document.SelectItem(item, select | deselectOthers | ensureVisible);
                        return;
                    }
                }
            }
            catch
            {
                // The document is mid-navigation and not answering yet; poll again until the deadline.
            }

            Thread.Sleep(MatchPollMs);
        }
    }

    public static void ReleaseComObject(object? comObject)
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

    private static object? FindShellWindowForTabOnce(IntPtr tabHwnd, IntPtr cabinetHwnd)
    {
        object? shellWindows = null;
        try
        {
            var shellWindowsType = Type.GetTypeFromCLSID(ShellWindowsClsid);
            if (shellWindowsType == null) return null;
            shellWindows = Activator.CreateInstance(shellWindowsType);
            if (shellWindows == null) return null;

            dynamic windows = shellWindows;
            var count = (int)windows.Count;
            for (var i = 0; i < count; i++)
            {
                object? window = null;
                try
                {
                    window = windows.Item(i);
                    if (window == null) continue;

                    dynamic candidate = window;
                    if (cabinetHwnd != IntPtr.Zero && (IntPtr)candidate.HWND != cabinetHwnd) continue;
                    if (tabHwnd != IntPtr.Zero && (!TryGetTabHandle(window, out var handle) || handle != tabHwnd)) continue;

                    var match = window;
                    window = null; // Ownership moves to the caller, so the finally below must not release it.
                    return match;
                }
                catch
                {
                    // One item that cannot be read or matched is not a reason to give up on the rest.
                }
                finally
                {
                    ReleaseComObject(window);
                }
            }

            return null;
        }
        finally
        {
            ReleaseComObject(shellWindows);
        }
    }

    private static bool TryGetTabHandle(object window, out IntPtr tabHwnd)
    {
        tabHwnd = IntPtr.Zero;
        if (window is not IComServiceProvider serviceProvider) return false;

        var serviceGuid = ShellBrowserService;
        var interfaceGuid = ShellBrowserInterface;
        if (serviceProvider.QueryService(ref serviceGuid, ref interfaceGuid, out var shellBrowserPtr) != 0 || shellBrowserPtr == IntPtr.Zero)
            return false;

        try
        {
            var shellBrowser = (IShellBrowser)Marshal.GetObjectForIUnknown(shellBrowserPtr);
            try
            {
                return shellBrowser.GetWindow(out tabHwnd) == 0 && tabHwnd != IntPtr.Zero;
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

    // EnumChildWindows walks every descendant, which is the whole point: the tab windows are not children
    // of the window that owns them. FindWindowEx, used here before, only ever looks one level down.
    private static List<IntPtr> FindDescendants(IntPtr parent, string className)
    {
        var found = new List<IntPtr>();
        if (parent == IntPtr.Zero) return found;

        EnumChildWindows(parent, (child, _) =>
        {
            if (HasClassName(child, className)) found.Add(child);
            return true;
        }, IntPtr.Zero);

        return found;
    }

    private static bool HasClassName(IntPtr hwnd, string className)
    {
        var buffer = new StringBuilder(64);
        return GetClassName(hwnd, buffer, buffer.Capacity) > 0
               && string.Equals(buffer.ToString(), className, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether this is Windows Explorer's own folder window -- the only kind that has the tab strip the
    /// new-tab route drives. A third-party manager's window and the desktop are both <see langword="false"/>.
    /// </summary>
    internal static bool TargetsWindowsExplorer(IntPtr hwnd) => hwnd != IntPtr.Zero && HasClassName(hwnd, ExplorerWindowClass);

    private static bool PathsEqual(string? left, string? right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(left ?? string.Empty),
            Path.TrimEndingDirectorySeparator(right ?? string.Empty),
            StringComparison.OrdinalIgnoreCase);

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    private interface IComServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid serviceGuid, ref Guid interfaceGuid, out IntPtr service);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214E2-0000-0000-C000-000000000046")]
    private interface IShellBrowser
    {
        [PreserveSig]
        int GetWindow(out IntPtr hwnd);
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(IntPtr hwndParent, EnumWindowsProc callback, IntPtr lParam);
}
