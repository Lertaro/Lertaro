namespace Lertaro.App.Services.ShellMenu.QuickNav;

// Whether Quick Navigation should trigger for a middle-click while a recognized file dialog (Open/Save/
// Browse-for-folder) is the active window. Kept separate from the IQuickNavigationTriggerGate loop in
// App.xaml.cs since this isn't a plugin-owned gate at all: PluginSdk.Registries.FileDialogAdapterRegistry
// already knows "is this a dialog, and which kind" (the Hook process uses this exact same registry to
// auto-navigate a reactivated dialog) -- and once QuickNavigationMenu.Show() is triggered by ANY gate, it
// unconditionally builds content from every registered IQuickNavigationProvider anyway (e.g.
// FolderCascader's own Favorites/History/Folders categories), so this only has to answer "should the
// popup open at all," never supply content of its own.
internal static class FileDialogQuickNavGate
{
    private const uint GA_ROOT = 2;

    public static bool CanShow(IntPtr activeHwnd, string processName, string className, int x, int y)
    {
        var adapter = PluginSdk.Registries.FileDialogAdapterRegistry.GetMatchingAdapter(activeHwnd, className, processName);
        if (adapter == null) return false;

        var pt = new Views.InlineSearchWindow.Helpers.InlineSearchWindowNativeMethods.POINT { x = x, y = y };
        var hwndUnderCursor = Views.InlineSearchWindow.Helpers.InlineSearchWindowNativeMethods.WindowFromPoint(pt);
        if (hwndUnderCursor == IntPtr.Zero) return false;

        // activeHwnd comes from ExplorerTracker, which only updates on OS foreground-change events -- a
        // middle-click does not activate the window it lands on, so activeHwnd can still be a dialog from
        // some other (now background) app while the click actually landed elsewhere (e.g. the desktop).
        // Require the clicked window to actually be inside the matched dialog before trusting it, or a
        // stale match ends up routing the click into a dialog the user never touched.
        if (Core.Hook.ExplorerNativeHooks.GetAncestor(hwndUnderCursor, GA_ROOT) != activeHwnd) return false;

        var sb = new System.Text.StringBuilder(256);
        var classNameUnderCursor = Core.Hook.ExplorerNativeHooks.GetClassName(hwndUnderCursor, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
        return adapter.CanShowQuickNav(hwndUnderCursor, classNameUnderCursor);
    }
}
