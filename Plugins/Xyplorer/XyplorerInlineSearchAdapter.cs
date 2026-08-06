using System.IO;
using System.Runtime.InteropServices;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.Xyplorer.Win32;

using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
namespace Lertaro.Plugins.Xyplorer;

/// <summary>
/// Inline-search integration for XYplorer (ThunderRT6FormDC). Reading the current path and navigating the
/// active pane both go through XYplorer's documented WM_COPYDATA remote-control interface (see Win32Helper),
/// so no scraping of XYplorer's custom-drawn controls is involved.
/// </summary>
public class XyplorerInlineSearchAdapter : IInlineSearchAdapter
{
    public string Name => "XYplorer";

    public bool IsFileExplorer => true;

    // OnSelectionChanged's goto/SelectItems script commands can activate XYplorer as a side effect even
    // though this adapter never calls SetForegroundWindow itself -- tune independently from other
    // adapters here rather than a shared process-wide constant.
    public int SelectionSyncFocusReclaimDelayMs => 200;

    private const string MainClass = "ThunderRT6FormDC";

    // XYplorer's file-list panes are custom-drawn VB6 PictureBox controls; the trailing "DC67"/"DC57" varies
    // with which pane (left/right) currently has it and build. Match by prefix -- an exact compare never hits
    // and nothing would ever trigger.
    private static bool IsFileList(string className) =>
        className.StartsWith("ThunderRT6PictureBoxDC", StringComparison.OrdinalIgnoreCase);

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
        if (!PluginSettingsService.GetSetting("Lertaro.Plugins.Xyplorer", "EnableInlineSearch", true))
            return false;

        if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(processName))
            return false;

        return processName.StartsWith("xyplorer", StringComparison.OrdinalIgnoreCase) &&
               className.Equals(MainClass, StringComparison.OrdinalIgnoreCase);
    }

    public bool CanTrigger(IntPtr focusedHwnd, string className)
    {
        if (focusedHwnd == IntPtr.Zero || string.IsNullOrEmpty(className))
            return false;

        // Only trigger from a file list pane, so typing in XYplorer's address bar / filter box is left untouched.
        return IsFileList(className);
    }

    public bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor)
    {
        if (!PluginSettingsService.GetSetting("Lertaro.Plugins.Xyplorer", "EnableQuickNav", true))
            return false;

        return CanTrigger(hwndUnderCursor, classNameUnderCursor);
    }

    public string? GetSearchScope(IntPtr hwnd)
    {
        var path = Win32Helper.QueryCurrentPath(hwnd);
        if (string.IsNullOrEmpty(path)) return null;
        if (path.Length > 3 && path.EndsWith('\\'))
            path = path.TrimEnd('\\');
        return path;
    }

    public bool ExecuteItem(IntPtr hwnd, string path, string searchInput)
    {
        try
        {
            // See the identical comment on the Total Commander adapter's ExecuteItem: don't check
            // Directory.Exists/File.Exists here, the trailing separator the caller already encoded is the
            // only reliable signal when this runs elevated in the Hook process.
            var isDir = Path.EndsInDirectorySeparator(path);
            var cleanPath = isDir ? Path.TrimEndingDirectorySeparator(path) : path;
            return Win32Helper.Navigate(hwnd, cleanPath);
        }
        catch
        {
            return false;
        }
    }

    public void OnSelectionChanged(IntPtr hwnd, string path)
    {
        if (hwnd == IntPtr.Zero || string.IsNullOrEmpty(path)) return;

        // Trailing separator marks a directory (see InlineSearchWindowInputHandler.SyncExplorerSelection,
        // the sender of this path) -- can't re-derive this with Directory.Exists here instead: this runs
        // in the Hook process, which silently can't see a mapped network drive when running elevated.
        var isDir = Path.EndsInDirectorySeparator(path);
        var cleanPath = isDir ? Path.TrimEndingDirectorySeparator(path) : path;

        // goto is a real navigation command, not a pure "select" verb -- calling it unconditionally on
        // every arrow-key move while browsing global search results (which can span many unrelated
        // folders) would make XYplorer's pane jump around constantly. Scoped to the folder XYplorer's pane
        // already has open, same as Directory Opus's and Total Commander's own OnSelectionChanged.
        var scope = GetSearchScope(hwnd);
        var parent = Path.GetDirectoryName(cleanPath);
        var isInCurrentFolder = !string.IsNullOrEmpty(scope) && string.Equals(parent?.TrimEnd('\\'), scope.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        if (!isInCurrentFolder) return;

        try
        {
            if (isDir)
            {
                // goto on a folder path enters it -- wrong for a live preview highlight while browsing
                // search results. SelectItems operates on the folder already open (verified above) instead
                // of navigating into the target.
                Win32Helper.SelectItem(hwnd, cleanPath);
            }
            else
            {
                // stealFocus: false -- see Win32Helper.Navigate's own comment on why live-mirroring must
                // not steal foreground/keyboard focus away from the search box the user is still typing in.
                Win32Helper.Navigate(hwnd, cleanPath, stealFocus: false);
            }
        }
        catch
        {
        }
    }

    public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect)
    {
        rect = default;
        if (hwnd == IntPtr.Zero) return false;

        // Prefer docking over the focused file list pane (the active pane).
        var focused = Win32Helper.GetFocusedControl(hwnd);
        if (focused != IntPtr.Zero &&
            IsFileList(Win32Helper.GetClassName(focused)) &&
            GetWindowRect(focused, out var fr))
        {
            rect = new AdapterRect { Left = fr.Left, Top = fr.Top, Right = fr.Right, Bottom = fr.Bottom };
            return true;
        }

        // Fall back to the whole main window. Extended frame bounds excludes the drop shadow, matching the visible edge.
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
