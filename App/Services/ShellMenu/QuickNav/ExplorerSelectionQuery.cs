using Native = Lertaro.Core.Hook.ExplorerNativeHooks;

namespace Lertaro.App.Services.ShellMenu.QuickNav;

// Reads Explorer's current selection. Missing/failed reads are unknown, never evidence of empty
// space. This is only the selection gate; the caller must also validate the clicked host and view.
internal static class ExplorerSelectionQuery
{
    private const uint GA_ROOT = 2;

    public static bool IsActiveWindowFolderEmptySpace(IntPtr hwnd)
    {
        object? shell = null;
        object? windows = null;
        try
        {
            var rootHwnd = Native.GetAncestor(hwnd, GA_ROOT);
            if (rootHwnd == IntPtr.Zero || !Native.IsWindow(rootHwnd)) return false;
            var isActiveDesktop = Native.IsDesktopWindow(rootHwnd, out _);
            // ShellWindows exposes one item per tab and they all report the window's HWND, so the tab
            // handle is what says which of them the user is looking at.
            var activeTab = isActiveDesktop ? IntPtr.Zero : ExplorerShellWindowsHelper.GetActiveTabHandle(rootHwnd);

            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return false;

            shell = Activator.CreateInstance(shellType);
            if (shell == null) return false;

            dynamic dShell = shell;
            windows = dShell.Windows();
            if (windows == null) return false;

            dynamic dWindows = windows;
            int count = dWindows.Count;
            for (var i = 0; i < count; i++)
            {
                object? window = null;
                object? doc = null;
                try
                {
                    window = dWindows.Item(i);
                    if (window == null) continue;

                    dynamic w = window;
                    var wHwnd = new IntPtr(w.HWND);

                    var isMatch = isActiveDesktop ? Native.IsDesktopWindow(wHwnd, out _) : wHwnd == rootHwnd;
                    if (!isMatch) continue;
                    if (!isActiveDesktop)
                    {
                        ExplorerShellWindowsHelper.TryGetTabHandle(window, out var itemTab);
                        if (!IsWindowForActiveTab(wHwnd, itemTab, rootHwnd, activeTab)) continue;
                    }

                    doc = w.Document;
                    return HasNoSelectedItems(doc);
                }
                catch { }
                finally
                {
                    ReleaseComObject(doc);
                    ReleaseComObject(window);
                }
            }
        }
        catch { }
        finally
        {
            ReleaseComObject(windows);
            ReleaseComObject(shell);
        }
        return false;
    }

    /// <summary>
    /// Whether one Shell.Application window item is the view the user is looking at.
    /// </summary>
    /// <remarks>
    /// Matching the top-level HWND alone answers for whichever tab of that window enumerates first. In a
    /// window whose other tab had nothing selected, that reported "empty space" while the user was
    /// double-clicking a folder in the active tab, and the QuickNav menu opened over the navigation it had
    /// just started -- reproducible by clicking the new-tab button and then double-clicking any folder.
    /// An item whose tab handle cannot be read is not the active view (unknown is not empty space), and a
    /// zero <paramref name="activeTabHwnd"/> means the window has no tab to compare against.
    /// </remarks>
    internal static bool IsWindowForActiveTab(IntPtr windowHwnd, IntPtr windowTabHwnd, IntPtr rootHwnd, IntPtr activeTabHwnd)
    {
        if (windowHwnd != rootHwnd) return false;
        return activeTabHwnd == IntPtr.Zero || windowTabHwnd == activeTabHwnd;
    }

    internal static bool HasNoSelectedItems(object? document)
    {
        if (document == null) return false;
        object? selectedItems = null;
        try
        {
            dynamic doc = document;
            selectedItems = doc.SelectedItems;
            if (selectedItems == null) return false;
            dynamic selected = selectedItems;
            int count = selected.Count;
            return count == 0;
        }
        catch { return false; }
        finally { ReleaseComObject(selectedItems); }
    }

    private static void ReleaseComObject(object? comObject)
    {
        try
        {
            if (comObject != null && System.Runtime.InteropServices.Marshal.IsComObject(comObject))
                System.Runtime.InteropServices.Marshal.ReleaseComObject(comObject);
        }
        catch
        {
            // Best-effort cleanup; the RCW will still be reclaimed by the GC finalizer.
        }
    }
}
