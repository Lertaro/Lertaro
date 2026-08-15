using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Lertaro.Core;

namespace Lertaro.App.Services;

// Windows exposes no supported Explorer-tab API. This narrowly contains the undocumented Explorer
// integration so LocateInExplorer can fall back to the documented shell API whenever it changes.
internal static class ExplorerTabLocator
{
    private const uint WmCommand = 0x0111;
    private const nint NewTabCommand = 0xA21B;
    private const string ExplorerWindowClass = "CabinetWClass";
    private const string TabWindowClass = "ShellTabWindowClass";
    private static readonly Guid ShellBrowserGuid = new("000214E2-0000-0000-C000-000000000046");
    // ponytail: Explorer has no stable identifier for a newly created tab, so concurrent requests
    // cannot safely associate their target with the right tab. Serialize them; a public tab API is the upgrade path.
    private static readonly SemaphoreSlim TabOpenGate = new(1, 1);

    public static bool TryLocateInNewTab(string path) => TryLocateInNewTab(path, IntPtr.Zero);

    public static bool TryLocateInNewTab(string path, IntPtr preferredExplorerWindow)
    {
        var targetFolder = Path.GetDirectoryName(path);
        return !string.IsNullOrWhiteSpace(targetFolder) && TryOpenInNewTab(targetFolder, Path.GetFileName(path), path, preferredExplorerWindow);
    }

    public static bool TryOpenFolderInNewTab(string path) => TryOpenInNewTab(path, string.Empty, path, IntPtr.Zero);

    private static bool TryOpenInNewTab(string targetFolder, string itemName, string sourcePath, IntPtr preferredExplorerWindow)
    {
        TabOpenGate.Wait();
        try
        {
            var explorerWindow = FindExplorerWindow(preferredExplorerWindow);
            if (explorerWindow == IntPtr.Zero) return false;

            var activeTab = FindWindowEx(explorerWindow, IntPtr.Zero, TabWindowClass, null);
            if (activeTab == IntPtr.Zero) return false;

            var tabsBefore = GetExplorerTabs(explorerWindow);
            if (!PostMessage(activeTab, WmCommand, NewTabCommand, IntPtr.Zero)) return false;

            var newTab = WaitForNewTab(explorerWindow, tabsBefore);
            if (newTab == IntPtr.Zero) return false;

            var tabExplorer = FindTabExplorer(newTab);
            if (tabExplorer == null) return false;

            try
            {
                NavigateAndSelect(tabExplorer, targetFolder, itemName);
                return true;
            }
            finally
            {
                if (Marshal.IsComObject(tabExplorer))
                    Marshal.ReleaseComObject(tabExplorer);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[ExplorerTabLocator] New-tab locate failed for '{sourcePath}': {ex.Message}", LogLevel.Error);
            return false;
        }
        finally
        {
            TabOpenGate.Release();
        }
    }

    public static bool HasAvailableExplorerWindow() => FindExplorerWindow(IntPtr.Zero) != IntPtr.Zero;

    public static bool WaitForAvailableExplorerWindow()
    {
        var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 2;
        while (Stopwatch.GetTimestamp() < deadline)
        {
            if (HasAvailableExplorerWindow()) return true;
            Thread.Sleep(50);
        }

        return false;
    }

    private static IntPtr FindExplorerWindow(IntPtr preferredExplorerWindow)
    {
        if (IsExplorerWindow(preferredExplorerWindow)) return preferredExplorerWindow;

        var foreground = GetForegroundWindow();
        if (IsExplorerWindow(foreground)) return foreground;

        var window = IntPtr.Zero;
        while (true)
        {
            window = FindWindowEx(IntPtr.Zero, window, ExplorerWindowClass, null);
            if (window == IntPtr.Zero) return IntPtr.Zero;
            if (FindWindowEx(window, IntPtr.Zero, TabWindowClass, null) != IntPtr.Zero) return window;
        }
    }

    private static bool IsExplorerWindow(IntPtr window)
    {
        if (window == IntPtr.Zero) return false;
        var className = new System.Text.StringBuilder(64);
        return GetClassName(window, className, className.Capacity) > 0 &&
               string.Equals(className.ToString(), ExplorerWindowClass, StringComparison.Ordinal);
    }

    private static HashSet<IntPtr> GetExplorerTabs(IntPtr explorerWindow)
    {
        var tabs = new HashSet<IntPtr>();
        var tab = IntPtr.Zero;
        while (true)
        {
            tab = FindWindowEx(explorerWindow, tab, TabWindowClass, null);
            if (tab == IntPtr.Zero) return tabs;
            tabs.Add(tab);
        }
    }

    private static IntPtr WaitForNewTab(IntPtr explorerWindow, HashSet<IntPtr> tabsBefore)
    {
        var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 2;
        while (Stopwatch.GetTimestamp() < deadline)
        {
            var newTab = GetExplorerTabs(explorerWindow).FirstOrDefault(tab => !tabsBefore.Contains(tab));
            if (newTab != IntPtr.Zero) return newTab;
            Thread.Sleep(50);
        }

        return IntPtr.Zero;
    }

    private static object? FindTabExplorer(IntPtr tab)
    {
        var shellWindowsType = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"));
        if (shellWindowsType == null || Activator.CreateInstance(shellWindowsType) is not object shellWindows) return null;

        try
        {
            dynamic windows = shellWindows;
            var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 2;
            while (Stopwatch.GetTimestamp() < deadline)
            {
                var count = (int)windows.Count;
                for (var i = 0; i < count; i++)
                {
                    object? window = null;
                    try
                    {
                        window = windows.Item(i);
                        if (window != null && TryGetTabHandle(window, out var handle) && handle == tab)
                            return window;
                    }
                    catch { }

                    if (window != null && Marshal.IsComObject(window))
                        Marshal.ReleaseComObject(window);
                }

                Thread.Sleep(50);
            }
        }
        finally
        {
            if (Marshal.IsComObject(shellWindows))
                Marshal.ReleaseComObject(shellWindows);
        }

        return null;
    }

    private static bool TryGetTabHandle(object window, out IntPtr handle)
    {
        handle = IntPtr.Zero;
        if (window is not IExplorerServiceProvider serviceProvider) return false;

        var serviceGuid = ShellBrowserGuid;
        var interfaceGuid = ShellBrowserGuid;
        if (serviceProvider.QueryService(ref serviceGuid, ref interfaceGuid, out var shellBrowser) != 0 || shellBrowser == null)
            return false;

        try
        {
            return shellBrowser.GetWindow(out handle) == 0 && handle != IntPtr.Zero;
        }
        finally
        {
            Marshal.ReleaseComObject(shellBrowser);
        }
    }

    private static void NavigateAndSelect(object explorer, string folder, string itemName)
    {
        dynamic window = explorer;
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
                    var currentPath = shellFolder.Self.Path as string;
                    if (string.Equals(Path.TrimEndingDirectorySeparator(currentPath ?? string.Empty), Path.TrimEndingDirectorySeparator(folder), StringComparison.OrdinalIgnoreCase))
                        return;
                }
                else
                {
                    object? item = shellFolder.ParseName(itemName);
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
            catch { }

            Thread.Sleep(50);
        }
    }

    [ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IExplorerServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid serviceGuid, ref Guid interfaceGuid, [MarshalAs(UnmanagedType.Interface)] out IExplorerShellBrowser? shellBrowser);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IExplorerShellBrowser
    {
        [PreserveSig]
        int GetWindow(out IntPtr handle);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, System.Text.StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
