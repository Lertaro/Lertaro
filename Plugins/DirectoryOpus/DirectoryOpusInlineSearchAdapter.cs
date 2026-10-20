using System.IO;
using System.Runtime.InteropServices;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.DirectoryOpus.Win32;

using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
namespace Lertaro.Plugins.DirectoryOpus;

public class DirectoryOpusInlineSearchAdapter : IInlineSearchAdapter
{
    public string Name => "Directory Opus";

    public bool IsFileExplorer => true;

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    // Returns Win32 BOOL, so it is read as int: on x64 the upper half of RAX is unspecified after a 32-bit
    // return, which an IntPtr would let through as garbage.
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, ref COPYDATASTRUCT lParam, uint flags, uint timeoutMs, out IntPtr result);

    private const uint WM_COPYDATA = 0x004A;
    private const uint SmtoBlock = 0x0001;
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint CommandTimeoutMs = 1000;

    public bool CanHandle(IntPtr hwnd, string className, string processName)
    {
        if (!PluginSettingsService.GetSetting("Lertaro.Plugins.DirectoryOpus", "EnableInlineSearch", true))
            return false;

        return CanRecognizeHost(hwnd, className, processName);
    }

    public bool CanRecognizeHost(IntPtr hwnd, string className, string processName)
    {
        if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(processName))
            return false;

        return processName.Equals("dopus", StringComparison.OrdinalIgnoreCase) &&
               className.Equals("dopus.lister", StringComparison.OrdinalIgnoreCase);
    }

    public bool CanTrigger(IntPtr focusedHwnd, string className)
    {
        if (focusedHwnd == IntPtr.Zero || string.IsNullOrEmpty(className))
            return false;

        // Thumbnails/Tiles/Large Icons render the file list in a separate child window class
        // ("dopus.iconfiledisplay") from Details/List's "dopus.filedisplay" -- confirmed via the
        // targetFocus/className debug log (see HandleInlineSearchKeys) while focused in that mode.
        return className.Equals("dopus.filedisplay", StringComparison.OrdinalIgnoreCase) ||
               className.Equals("dopus.filedisplaycontainer", StringComparison.OrdinalIgnoreCase) ||
               className.Equals("dopus.iconfiledisplay", StringComparison.OrdinalIgnoreCase);
    }

    public bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor)
    {
        if (!PluginSettingsService.GetSetting("Lertaro.Plugins.DirectoryOpus", "EnableQuickNav", true))
            return false;

        return CanTrigger(hwndUnderCursor, classNameUnderCursor);
    }

    public string? GetSearchScope(IntPtr hwnd)
    {
        var collector = new DirectoryOpusPathCollector();
        var className = Win32Helper.GetClassName(hwnd);
        return collector.TryGetPath(hwnd, className, hwnd, className, "dopus");
    }

    internal static byte[] BuildCommandPayload(string command) => System.Text.Encoding.Unicode.GetBytes(command + "\0");

    // Delivery is a synchronous cross-process message, so it is bounded and its answer is read. SendMessage
    // (used here before) returns 0 for a receiver that is not answering while still letting that receiver run
    // the command later, and its value was discarded: an Opus parked inside its own script engine made "open
    // containing folder" report success without opening anything, which is how a user ends up pressing it
    // again and again. The buffer is AllocHGlobal rather than a pinned array because a call that did not
    // complete must never leave Opus holding a pointer the GC has since moved; that one buffer is then
    // deliberately left allocated.
    // ponytail: ceiling is one leaked small buffer per aborted command, which only a wedged host plus a
    // persistent user can accumulate. The upgrade path is a bounded pool, not an earlier free.
    private static bool RunDopusCommandViaCopyData(string command)
    {
        var dopusParent = FindWindow("DOpus.ParentWindow", "Directory Opus");
        if (dopusParent == IntPtr.Zero) return false;

        var bytes = BuildCommandPayload(command);
        var buffer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, buffer, bytes.Length);
        var cds = new COPYDATASTRUCT
        {
            dwData = (IntPtr)0x14,
            cbData = bytes.Length,
            lpData = buffer
        };

        if (SendMessageTimeout(dopusParent, WM_COPYDATA, IntPtr.Zero, ref cds,
                SmtoAbortIfHung | SmtoBlock, CommandTimeoutMs, out _) == 0)
            return false;

        Marshal.FreeHGlobal(buffer);
        return true;
    }

    public bool LocateItem(IntPtr hwnd, string path)
    {
        var item = Path.TrimEndingDirectorySeparator(path);
        // The file branch already navigates to the parent then selects by name, for either item kind.
        return !string.IsNullOrEmpty(Path.GetDirectoryName(item)) && ExecuteItem(hwnd, item, string.Empty);
    }

    public bool ExecuteItem(IntPtr hwnd, string path, string searchInput)
    {
        try
        {
            // Why the caller marks a directory with a trailing separator and this strips it back off --
            // including the elevation/logon-session reason the Hook cannot test that itself -- is written
            // out once in CoreExtensions' ExplorerInlineSearchAdapter.ExecuteItem. Same rule here, so the
            // path embedded in the DO command is unchanged from before.
            var isDir = Path.EndsInDirectorySeparator(path);
            var cleanPath = isDir ? Path.TrimEndingDirectorySeparator(path) : path;

            if (isDir)
            {
                if (RunDopusCommandViaCopyData($"Go \"{cleanPath}\""))
                {
                    return true;
                }
            }
            else
            {
                // Read only in this branch: deciding whether a FILE is already in the open folder needs the
                // lister's current folder, while a folder result goes straight to "Go" and would pay for a
                // window-tree walk it never reads.
                var isInCurrentFolder = IsInFolder(GetSearchScope(hwnd), cleanPath);
                var parent = Path.GetDirectoryName(cleanPath);

                if (isInCurrentFolder)
                {
                    SelectByFileName(cleanPath, focus: true);
                    return true;
                }

                // Not in the open folder: navigate to the parent first, then select once the listing has
                // caught up with the navigation.
                if (parent != null && RunDopusCommandViaCopyData($"Go \"{parent}\""))
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(200);
                        SelectByFileName(cleanPath, focus: true);
                    });
                    return true;
                }
            }
        }
        catch
        {
            // ponytail: ignore execution errors, fallback to default behavior
        }
        return false;
    }

    // Selects one item in the lister's currently open folder without navigating. MAKEVISIBLE is what
    // scrolls the item into view: without it the file is selected but can sit outside the visible rows, so
    // a live sync looks like it did nothing. DESELECTNOMATCH makes this a replace-selection rather than an
    // add.
    //
    // SETFOCUS is deliberately NOT here. It moves the file display's keyboard focus onto the item, and the
    // live mirror fires WHILE the user is still typing in the search box (see
    // InlineExplorerSelectionSync) -- so a keystroke could land in Directory Opus's file display instead,
    // where its own type-ahead "quick find" picks it up and jumps the listing. Explorer's adapter has no
    // equivalent because it only ever sets the selection, never the focus.
    internal const string SelectArguments = "DESELECTNOMATCH MAKEVISIBLE";

    // The commit path (ExecuteItem) DOES want focus moved: the inline window is already hidden by then and
    // the user asked to land on this item in Directory Opus, so handing it the focus is the point.
    internal const string SelectAndFocusArguments = SelectArguments + " SETFOCUS";

    private static void SelectByFileName(string path, bool focus = false)
    {
        var filename = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.IsNullOrEmpty(filename))
            RunDopusCommandViaCopyData($"Select \"{filename}\" {(focus ? SelectAndFocusArguments : SelectArguments)}");
    }

    // Whether `path` is an item sitting directly in `scope` (the folder the lister has open), rather than
    // something in a subfolder or another drive entirely.
    //
    // The trailing separator matters here and must be trimmed BEFORE GetDirectoryName: the sender appends
    // one to mark a directory (see InlineSearchWindowInputHandler.SyncExplorerSelection), and
    // GetDirectoryName does not read that as "the parent of this folder" -- it returns
    // "C:\Root\Sub" for both "C:\Root\Sub" AND "C:\Root\Sub\", i.e. the folder itself for the trailing
    // form. Comparing that against the open folder never matched, so FOLDER results never mirrored while
    // files (which carry no trailing separator) did.
    internal static bool IsInFolder(string? scope, string path)
    {
        if (string.IsNullOrEmpty(scope))
            return false;
        var cleanPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(cleanPath);
        return string.Equals(parent?.TrimEnd('\\'), scope.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    }

    public void OnSelectionChanged(IntPtr hwnd, string path)
    {
        if (hwnd == IntPtr.Zero || string.IsNullOrEmpty(path)) return;

        if (IsInFolder(GetSearchScope(hwnd), path))
        {
            SelectByFileName(path);
        }
    }

    // Win32Helper.RECT rather than a private copy: it is the same four ints in the same order, and the
    // plugin's own Win32Helper already owns the GetWindowRect fallback below.
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out Win32Helper.RECT pvAttribute, int cbAttribute);

    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    private static IntPtr GetListerWindow(IntPtr hwnd)
    {
        var current = hwnd;
        while (current != IntPtr.Zero)
        {
            var className = Win32Helper.GetClassName(current);
            if (className.Equals("dopus.lister", StringComparison.OrdinalIgnoreCase))
            {
                return current;
            }
            current = Win32Helper.GetParent(current);
        }
        return hwnd;
    }

    public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect)
    {
        rect = default;
        if (hwnd == IntPtr.Zero) return false;

        var listerHwnd = GetListerWindow(hwnd);

        // Dock to the file displays the lister is showing: the card covers the file area and scrolls it, so
        // that is the corner it hangs from. His reference for this is 1451,331 on a live lister, which is the
        // file display's own top-right corner (1090,331,1452,1027) and not the window's.
        if (Win32Helper.TryGetFileDisplayBounds(listerHwnd, out var files))
        {
            rect = new AdapterRect { Left = files.Left, Top = files.Top, Right = files.Right, Bottom = files.Bottom };
            return true;
        }

        // With no file display to name, the whole lister window -- and the App keeps the card below its title
        // bar (InlineSearchWindowPositioner.CalculatePhysTop), because the top of this rect is the caption the
        // system drew with the lister's close button at its right end (measured: frame 196, client area 227).
        // Extended frame bounds excludes the drop shadow, matching the visible edge; GetWindowRect is the
        // fallback for a window DWM will not answer for.
        if (DwmGetWindowAttribute(listerHwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var nativeRect, Marshal.SizeOf<Win32Helper.RECT>()) != 0 &&
            !Win32Helper.TryGetWindowRect(listerHwnd, out nativeRect))
        {
            return false;
        }

        rect = new AdapterRect { Left = nativeRect.Left, Top = nativeRect.Top, Right = nativeRect.Right, Bottom = nativeRect.Bottom };
        return true;
    }

    public bool CanEnterActionsMode(IntPtr hwnd) => true;
}
