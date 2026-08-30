using Native = Lertaro.Core.Hook.ExplorerNativeHooks;

namespace Lertaro.App.Services.ShellMenu.QuickNav;

// Reads the active Explorer window's current selection count via dynamic Shell.Application COM
// automation -- the only way to distinguish "double-clicked empty space" from "double-clicked a
// selected item" for Explorer's own file list. Kept separate from QuickNavigationTriggerGate's gating
// policy: a Shell COM API quirk is a different concern from when the popup should show.
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
            var isActiveDesktop = Native.IsDesktopWindow(rootHwnd, out _);

            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return true;

            shell = Activator.CreateInstance(shellType);
            if (shell == null) return true;

            dynamic dShell = shell;
            windows = dShell.Windows();
            if (windows == null) return true;

            dynamic dWindows = windows;
            int count = dWindows.Count;
            for (var i = 0; i < count; i++)
            {
                object? window = null;
                object? doc = null;
                object? selectedItems = null;
                try
                {
                    window = dWindows.Item(i);
                    if (window == null) continue;

                    dynamic w = window;
                    var wHwnd = new IntPtr(w.HWND);

                    var isMatch = isActiveDesktop ? Native.IsDesktopWindow(wHwnd, out _) : wHwnd == rootHwnd;
                    if (!isMatch) continue;

                    doc = w.Document;
                    if (doc != null)
                    {
                        dynamic dDoc = doc;
                        selectedItems = dDoc.SelectedItems;
                        if (selectedItems != null)
                        {
                            dynamic dSelected = selectedItems;
                            int itemsCount = dSelected.Count;
                            if (itemsCount > 0) return false;
                        }
                    }
                    break;
                }
                catch { }
                finally
                {
                    ReleaseComObject(selectedItems);
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
        return true;
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
