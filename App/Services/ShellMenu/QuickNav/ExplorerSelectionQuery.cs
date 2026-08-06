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
        try
        {
            var rootHwnd = Native.GetAncestor(hwnd, GA_ROOT);
            var isActiveDesktop = Native.IsDesktopWindow(rootHwnd, out _);

            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return true;

            var shell = Activator.CreateInstance(shellType);
            if (shell == null) return true;

            dynamic dShell = shell;
            dynamic windows = dShell.Windows();
            if (windows == null) return true;

            int count = windows.Count;
            for (var i = 0; i < count; i++)
            {
                try
                {
                    dynamic window = windows.Item(i);
                    if (window == null) continue;

                    dynamic w = window;
                    var wHwnd = new IntPtr(w.HWND);

                    var isMatch = isActiveDesktop ? Native.IsDesktopWindow(wHwnd, out _) : wHwnd == rootHwnd;
                    if (!isMatch) continue;

                    dynamic doc = w.Document;
                    if (doc != null)
                    {
                        dynamic selectedItems = doc.SelectedItems;
                        if (selectedItems != null)
                        {
                            int itemsCount = selectedItems.Count;
                            if (itemsCount > 0) return false;
                        }
                    }
                    break;
                }
                catch { }
            }
        }
        catch { }
        return true;
    }
}
