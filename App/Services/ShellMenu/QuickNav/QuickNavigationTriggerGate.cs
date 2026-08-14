using System.Text;
using Native = Lertaro.Core.Hook.ExplorerNativeHooks;
using PointNative = Lertaro.App.Views.InlineSearchWindow.Helpers.InlineSearchWindowNativeMethods;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
namespace Lertaro.App.Services.ShellMenu.QuickNav;

// Decides whether the Quick Navigation popup should open for a double-click/middle-click in Explorer,
// the desktop, or a recognized third-party file manager. This used to live inside the FolderCascader
// plugin (as its CanShow), but none of it is actually FolderCascader-specific content-provider logic --
// it's host recognition, the same kind of thing IInlineSearchAdapter/IFileDialogAdapter already do for
// their hosts, so it belongs here alongside FileDialogQuickNavGate rather than behind a plugin interface.
internal static class QuickNavigationTriggerGate
{
    public static bool CanShow(IntPtr activeHwnd, string processName, string className, bool isDesktop, int x, int y, MouseTriggerType triggerType)
    {
        if (string.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase) || isDesktop)
        {
            return CanShowInExplorer(activeHwnd, x, y);
        }

        return CanShowInOtherFileManager(activeHwnd, processName, className, x, y, triggerType);
    }

    private static bool CanShowInExplorer(IntPtr activeHwnd, int x, int y)
    {
        var hwndUnderCursor = PointNative.WindowFromPoint(new PointNative.POINT { x = x, y = y });
        if (hwndUnderCursor == IntPtr.Zero) return false;

        var sbClass = new StringBuilder(256);
        Native.GetClassName(hwndUnderCursor, sbClass, sbClass.Capacity);
        var clsName = sbClass.ToString();

        // Turning off "Show desktop icons" does not remove them: the shell hides the SysListView32 that
        // holds them. With it hidden, WindowFromPoint returns whatever was behind it, the wallpaper
        // host, and both checks below fail on it: the class is not one of the two they accept, and
        // depending on which window answers it may not sit under SHELLDLL_DefView either. The menu
        // simply stopped opening on the desktop.
        //
        // Nothing needs hit-testing in that state. There are no icons on screen to have clicked, so any
        // click on the desktop is a click on empty space, which is the very thing the checks below exist
        // to establish.
        if (IsDesktopBackgroundClass(clsName)) return true;

        if (!IsDescendantOfShellDllDefView(hwndUnderCursor)) return false;

        if (!clsName.Equals("DirectUIHWND", StringComparison.OrdinalIgnoreCase) &&
            !clsName.Equals("SysListView32", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (clsName.Equals("SysListView32", StringComparison.OrdinalIgnoreCase))
        {
            // Cross-process LVM_HITTEST distinguishes a desktop icon from empty space; if that fails
            // (process open/memory allocation failure), fall through to the Shell selection-count check.
            if (DesktopIconHitTester.IsPointOnDesktopIcon(hwndUnderCursor, x, y))
            {
                return false;
            }
        }

        return ExplorerSelectionQuery.IsActiveWindowFolderEmptySpace(activeHwnd);
    }

    // Third-party file managers (Directory Opus, Total Commander, ...) integrate through their
    // IInlineSearchAdapter instead of host-specific hit-testing here -- CanShowQuickNav reuses whatever
    // "is this the host's file list" check the adapter already has for inline search's keyboard trigger.
    //
    // Restricted to middle-click: unlike Explorer (where empty space is detected precisely via the shell's
    // selection count), these hosts give no reliable way to tell "clicked an item" from "clicked empty
    // space", and double-clicking an item there already navigates into it -- popping this menu on top of
    // that would be confusing. Middle-click carries no such default action in these hosts.
    private static bool CanShowInOtherFileManager(IntPtr activeHwnd, string processName, string className, int x, int y, MouseTriggerType triggerType)
    {
        if (triggerType != MouseTriggerType.MiddleClick) return false;

        var adapter = FindFileManagerAdapter(PluginSdk.Registries.InlineSearchAdapterRegistry.GetAdapters(), activeHwnd, className, processName);
        if (adapter == null) return false;

        var hwndUnderCursor = PointNative.WindowFromPoint(new PointNative.POINT { x = x, y = y });
        if (hwndUnderCursor == IntPtr.Zero) return false;

        // Same staleness guard as FileDialogQuickNavGate: activeHwnd tracks the OS foreground window, which
        // a middle-click doesn't change, so a stale match could otherwise pass a completely unrelated
        // window's class name (e.g. the desktop's) to an adapter whose own CanShowQuickNav happens not to
        // reject it. Require the clicked window to actually be inside the matched host window first.
        if (Native.GetAncestor(hwndUnderCursor, GA_ROOT) != activeHwnd) return false;

        var sbClass = new StringBuilder(256);
        Native.GetClassName(hwndUnderCursor, sbClass, sbClass.Capacity);
        return adapter.CanShowQuickNav(hwndUnderCursor, sbClass.ToString());
    }

    // An adapter component may still be disabled as a whole, but its EnableInlineSearch setting must not
    // suppress the independent EnableQuickNav setting. Host recognition intentionally bypasses CanHandle,
    // whose job is to honor the former setting for inline-search activation.
    internal static IInlineSearchAdapter? FindFileManagerAdapter(IEnumerable<IInlineSearchAdapter> adapters, IntPtr hwnd, string className, string processName)
    {
        foreach (var adapter in adapters)
        {
            if (adapter.IsFileExplorer && adapter.CanRecognizeHost(hwnd, className, processName))
                return adapter;
        }
        return null;
    }

    private const uint GA_ROOT = 2;

    /// <summary>The windows that are the desktop itself rather than anything sitting on it.</summary>
    /// <remarks>
    /// Progman is the desktop window; WorkerW is the sibling the shell interposes for the wallpaper, and
    /// which one answers WindowFromPoint varies with the Windows version and whether a slideshow is
    /// running. SHELLDLL_DefView is included because it is what is left once its SysListView32 child is
    /// hidden, and reaching it means the cursor got past the icons without landing on one.
    ///
    /// None of the three can hold a desktop icon, which is what makes this safe to treat as empty space
    /// without hit-testing: an icon is always a SysListView32 item, and that window answers for itself
    /// while it is visible.
    /// </remarks>
    internal static bool IsDesktopBackgroundClass(string className) =>
        className.Equals("Progman", StringComparison.OrdinalIgnoreCase)
        || className.Equals("WorkerW", StringComparison.OrdinalIgnoreCase)
        || className.Equals("SHELLDLL_DefView", StringComparison.OrdinalIgnoreCase);

    private static bool IsDescendantOfShellDllDefView(IntPtr hwnd)
    {
        var current = hwnd;
        while (current != IntPtr.Zero)
        {
            var sbClass = new StringBuilder(256);
            Native.GetClassName(current, sbClass, sbClass.Capacity);
            if (sbClass.ToString().Equals("SHELLDLL_DefView", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            current = Native.GetParent(current);
        }
        return false;
    }

}
