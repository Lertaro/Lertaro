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
