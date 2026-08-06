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

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, ref COPYDATASTRUCT lParam);

    private const uint WM_COPYDATA = 0x004A;

    public bool CanHandle(IntPtr hwnd, string className, string processName)
    {
        if (!PluginSettingsService.GetSetting("Lertaro.Plugins.DirectoryOpus", "EnableInlineSearch", true))
            return false;

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

    private static bool RunDopusCommandViaCopyData(string command)
    {
        var dopusParent = FindWindow("DOpus.ParentWindow", "Directory Opus");
        if (dopusParent == IntPtr.Zero) return false;

        try
        {
            var cmdString = command + "\0";
            var bytes = System.Text.Encoding.Unicode.GetBytes(cmdString);
            var pinnedArray = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                var cds = new COPYDATASTRUCT
                {
                    dwData = (IntPtr)0x14,
                    cbData = bytes.Length,
                    lpData = pinnedArray.AddrOfPinnedObject()
                };
                SendMessage(dopusParent, WM_COPYDATA, IntPtr.Zero, ref cds);
                return true;
            }
            finally
            {
                pinnedArray.Free();
            }
        }
        catch
        {
            return false;
        }
    }

    public bool ExecuteItem(IntPtr hwnd, string path, string searchInput)
    {
        try
        {
            // The Hook (which runs this) doesn't check Directory.Exists/File.Exists itself -- when it runs
            // elevated (admin auto-elevate), UAC's split token puts it in a different logon session than
            // the one that mapped any network drive letters, so a perfectly valid mapped-drive path would
            // otherwise silently resolve to "doesn't exist". The caller already knows and encodes it as a
            // trailing separator (see InlineAdapterIpcCoordinator.ExecuteItem); stripped back off here so
            // the path embedded in the DO command is unchanged from before.
            var isDir = Path.EndsInDirectorySeparator(path);
            var cleanPath = isDir ? Path.TrimEndingDirectorySeparator(path) : path;

            var scope = GetSearchScope(hwnd);
            var parent = Path.GetDirectoryName(cleanPath);
            var isInCurrentFolder = !string.IsNullOrEmpty(scope) && string.Equals(parent?.TrimEnd('\\'), scope.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

            if (isDir)
            {
                if (RunDopusCommandViaCopyData($"Go \"{cleanPath}\""))
                {
                    return true;
                }
            }
            else
            {
                var filename = Path.GetFileName(cleanPath);
                if (isInCurrentFolder)
                {
                    RunDopusCommandViaCopyData($"Select \"{filename}\" DESELECTNOMATCH SETFOCUS");
                    return true;
                }
                else if (parent != null)
                {
                    if (RunDopusCommandViaCopyData($"Go \"{parent}\""))
                    {
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(200);
                            RunDopusCommandViaCopyData($"Select \"{filename}\" DESELECTNOMATCH SETFOCUS");
                        });
                        return true;
                    }
                }
            }
        }
        catch
        {
            // ponytail: ignore execution errors, fallback to default behavior
        }
        return false;
    }

    public void OnSelectionChanged(IntPtr hwnd, string path)
    {
        if (hwnd == IntPtr.Zero || string.IsNullOrEmpty(path)) return;
        var scope = GetSearchScope(hwnd);
        var parent = Path.GetDirectoryName(path);
        var isInCurrentFolder = !string.IsNullOrEmpty(scope) && string.Equals(parent?.TrimEnd('\\'), scope.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

        if (isInCurrentFolder)
        {
            var filename = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(filename))
            {
                RunDopusCommandViaCopyData($"Select \"{filename}\" DESELECTNOMATCH SETFOCUS");
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

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

        // Dock over the whole lister window's bottom-right corner (same as the Total Commander plugin).
        // Extended frame bounds excludes the drop shadow, matching the visible edge.
        var listerHwnd = GetListerWindow(hwnd);
        if (DwmGetWindowAttribute(listerHwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var nativeRect, Marshal.SizeOf<RECT>()) == 0)
        {
            rect = new AdapterRect { Left = nativeRect.Left, Top = nativeRect.Top, Right = nativeRect.Right, Bottom = nativeRect.Bottom };
            return true;
        }
        if (GetWindowRect(listerHwnd, out nativeRect))
        {
            rect = new AdapterRect { Left = nativeRect.Left, Top = nativeRect.Top, Right = nativeRect.Right, Bottom = nativeRect.Bottom };
            return true;
        }
        return false;
    }

    public bool CanEnterActionsMode(IntPtr hwnd) => true;
}
