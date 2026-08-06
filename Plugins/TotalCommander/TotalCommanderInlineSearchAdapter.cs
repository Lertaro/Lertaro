using System.IO;
using System.Runtime.InteropServices;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.TotalCommander.Win32;

using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
namespace Lertaro.Plugins.TotalCommander;

/// <summary>
/// Inline-search integration for Total Commander (TTOTAL_CMD). Reading the current path and navigating the
/// active pane both go through TC's documented WM_COPYDATA remote-control interface (see Win32Helper), so no
/// scraping of TC's custom-drawn controls is involved.
/// </summary>
public class TotalCommanderInlineSearchAdapter : IInlineSearchAdapter
{
    public string Name => "Total Commander";

    public bool IsFileExplorer => true;

    private const string MainClass = "TTOTAL_CMD";

    // Total Commander's file-list panes carry a trailing number that varies with tree/FTP state, and the class
    // name differs by build: 32-bit TC uses "TMyListBox1/2", 64-bit TC uses "LCLListBox1/2" (Lazarus). Match
    // either by prefix -- an exact compare never hits and nothing would ever trigger.
    private static bool IsFileList(string className) =>
        className.StartsWith("TMyListBox", StringComparison.OrdinalIgnoreCase) ||
        className.StartsWith("LCLListBox", StringComparison.OrdinalIgnoreCase);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    public bool CanHandle(IntPtr hwnd, string className, string processName)
    {
        if (!PluginSettingsService.GetSetting("Lertaro.Plugins.TotalCommander", "EnableInlineSearch", true))
            return false;

        if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(processName))
            return false;

        // TOTALCMD.EXE (32-bit) and TOTALCMD64.EXE both report a process name starting with "totalcmd".
        return processName.StartsWith("totalcmd", StringComparison.OrdinalIgnoreCase) &&
               className.Equals(MainClass, StringComparison.OrdinalIgnoreCase);
    }

    public bool CanTrigger(IntPtr focusedHwnd, string className)
    {
        if (focusedHwnd == IntPtr.Zero || string.IsNullOrEmpty(className))
            return false;

        // Only trigger from a file list, so typing in the command line / quick-rename box is left untouched.
        return IsFileList(className);
    }

    public bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor)
    {
        if (!PluginSettingsService.GetSetting("Lertaro.Plugins.TotalCommander", "EnableQuickNav", true))
            return false;

        return CanTrigger(hwndUnderCursor, classNameUnderCursor);
    }

    private string? _lastScopePath;

    public string? GetSearchScope(IntPtr hwnd) =>
        GetSearchScopeCore(hwnd, IsQuickRenameOpen, Win32Helper.QuerySourcePanelPath);

    // The lookups are passed in so the skip can be exercised without a live Total Commander.
    internal string? GetSearchScopeCore(IntPtr hwnd, Func<IntPtr, bool> isQuickRenameOpen, Func<IntPtr, string?> querySourcePanelPath)
    {
        // Asking Total Commander for its source panel path is a synchronous WM_COPYDATA into its UI thread,
        // and TC abandons an in-progress quick rename as soon as it services one (reproduced on its own,
        // with Lertaro not running, by scratch/tcquerykill.ps1). Renaming was impossible for as long as
        // Lertaro ran -- by F2 or by the context menu alike, since both open this same editor -- because
        // the editor was cancelled again before a character could be typed (issue #189). The panel cannot
        // change directory while its own rename is up, so the last answer is still the right one.
        if (isQuickRenameOpen(hwnd))
            return _lastScopePath;

        var path = querySourcePanelPath(hwnd);
        if (path is { Length: > 3 } && path.EndsWith('\\'))
            path = path.TrimEnd('\\');

        _lastScopePath = string.IsNullOrEmpty(path) ? null : path;
        return _lastScopePath;
    }

    private static bool IsQuickRenameOpen(IntPtr mainHwnd)
    {
        var focused = Win32Helper.GetFocusedControl(mainHwnd);
        if (focused == IntPtr.Zero)
            return false;

        return IsQuickRenameOpenCore(Win32Helper.GetClassName(focused), () => HasVisibleEditor(focused, mainHwnd));
    }

    internal static bool IsQuickRenameOpenCore(string focusedClassName, Func<bool> paneHasVisibleEditor)
    {
        // Total Commander moves keyboard focus into the editor, so this catches it outright -- confirmed
        // from the hook log, which reported the focused control as Edit for as long as the box was up. It
        // also covers the command line, which is no better a moment to interrupt.
        if (IsEditorClass(focusedClassName))
            return true;

        // Focus takes a moment to land there though (measured at ~36ms after F2), and an event arriving in
        // that gap still reports the pane as focused. Look underneath it for the editor as well, or a poll
        // landing in that window would sail past the check and cancel the rename after all.
        return IsFileList(focusedClassName) && paneHasVisibleEditor();
    }

    // "Edit" on the Lazarus build; matched loosely so a Delphi build naming it TEdit/TMyEdit, or a future
    // rename of the control, still counts.
    internal static bool IsEditorClass(string className) =>
        !string.IsNullOrEmpty(className) && className.Contains("Edit", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether <paramref name="pane"/>'s quick-rename editor is up, for the moment before focus reaches it.
    /// </summary>
    private static bool HasVisibleEditor(IntPtr pane, IntPtr mainHwnd)
    {
        var found = false;

        bool Inspect(IntPtr window)
        {
            if (!IsWindowVisible(window) || !IsEditorClass(Win32Helper.GetClassName(window)))
                return true;
            found = true;
            return false; // stop enumerating
        }

        EnumChildWindows(pane, (child, _) => Inspect(child), IntPtr.Zero);
        if (found)
            return true;

        // The editor need not be a child: it can equally be a top-level window merely OWNED by the pane,
        // which EnumChildWindows never visits, and the two are indistinguishable from outside (GetParent
        // reports the owner of an unparented window exactly as it reports the parent of a child). Walk both
        // so whichever shape a given build uses is covered. EnumThreadWindows yields only non-child windows,
        // which is why the command line -- a child Edit of the main window -- still cannot match here.
        var threadId = GetWindowThreadProcessId(pane, IntPtr.Zero);
        if (threadId == 0)
            return false;

        EnumThreadWindows(threadId, (window, _) =>
        {
            var owner = GetParent(window);
            return (owner == pane || owner == mainHwnd) ? Inspect(window) : true;
        }, IntPtr.Zero);
        return found;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumThreadWindows(uint dwThreadId, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    public bool ExecuteItem(IntPtr hwnd, string path, string searchInput)
    {
        try
        {
            // The Hook (which runs this) doesn't check Directory.Exists/File.Exists itself -- when it runs
            // elevated (admin auto-elevate), UAC's split token puts it in a different logon session than
            // the one that mapped any network drive letters, so a perfectly valid mapped-drive path would
            // otherwise silently resolve to "doesn't exist". The caller already knows and encodes it as a
            // trailing separator (see InlineAdapterIpcCoordinator.ExecuteItem); stripped back off here so
            // the path actually sent to TC is unchanged from before.
            var isDir = Path.EndsInDirectorySeparator(path);
            var cleanPath = isDir ? Path.TrimEndingDirectorySeparator(path) : path;
            // Enter the folder directly, or pass the file itself -- the 'A' flag opens its parent folder
            // and puts the cursor on it.
            return Win32Helper.ChangeSourcePanelDirectory(hwnd, cleanPath, placeCursorOnItem: !isDir);
        }
        catch
        {
            return false;
        }
    }

    // No OnSelectionChanged override: ChangeSourcePanelDirectory requires TC to be the foreground window
    // to act on its CD command at all, so live-mirroring here would steal real OS keyboard focus on every
    // selection change (every keystroke that changes the filtered results, not just arrow-key moves).
    // Tried it with a focus-reclaim timer afterward (see git history), but that only restores focus AFTER
    // the steal -- any characters typed during the steal itself still go to TC and are lost, with no
    // fixed timing to reclaim around. Confirmed in practice as random dropped keystrokes while typing.
    // Directory Opus and XYplorer don't have this problem (their own mechanisms don't require foreground),
    // so only TC is limited to ExecuteItem's one-shot "select on jump" behavior above.

    public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect)
    {
        rect = default;
        if (hwnd == IntPtr.Zero) return false;

        // Prefer docking over the focused file list (the active pane).
        var focused = Win32Helper.GetFocusedControl(hwnd);
        if (focused != IntPtr.Zero &&
            IsFileList(Win32Helper.GetClassName(focused)) &&
            GetWindowRect(focused, out var fr))
        {
            rect = new AdapterRect { Left = fr.Left, Top = fr.Top, Right = fr.Right, Bottom = fr.Bottom };
            return true;
        }

        // Fall back to the whole lister. Extended frame bounds excludes the drop shadow, matching the visible edge.
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var dr, Marshal.SizeOf<RECT>()) == 0)
        {
            rect = new AdapterRect { Left = dr.Left, Top = dr.Top, Right = dr.Right, Bottom = dr.Bottom };
            return true;
        }

        if (GetWindowRect(hwnd, out var wr))
        {
            rect = new AdapterRect { Left = wr.Left, Top = wr.Top, Right = wr.Right, Bottom = wr.Bottom };
            return true;
        }

        return false;
    }

    public bool CanEnterActionsMode(IntPtr hwnd) => true;
}
