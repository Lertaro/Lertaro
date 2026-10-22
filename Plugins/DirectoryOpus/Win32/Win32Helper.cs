using System.Runtime.InteropServices;
using System.Text;

namespace Lertaro.Plugins.DirectoryOpus.Win32;

public static class Win32Helper
{
    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hwndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, StringBuilder lParam, uint flags, uint timeoutMs, out IntPtr result);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    public static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    private const uint WM_GETTEXT = 0x000D;
    private const uint SmtoAbortIfHung = 0x0002;

    // Measured on a window that stopped pumping: SendMessageTimeout(WM_GETTEXT, 50) comes back with
    // ERROR_TIMEOUT in ~79 ms (dev-docs/pitfalls.md). 100 ms is above that without giving a wedged host an
    // open-ended hold on the reader; a timeout reads as "no text", which every caller already treats as
    // absence. This was SendMessage(WM_GETTEXT) -- the untimed variant, not Win32's own GetWindowText,
    // which sends the same message with an internal timeout.
    private const uint GetTextTimeoutMs = 100;

    public static string GetWindowText(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return string.Empty;
        var sb = new StringBuilder(512);
        SendMessageTimeout(hWnd, WM_GETTEXT, (IntPtr)sb.Capacity, sb, SmtoAbortIfHung, GetTextTimeoutMs, out _);
        return sb.ToString().Trim();
    }

    public static string GetClassName(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return string.Empty;
        var sb = new StringBuilder(256);
        GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static bool TryGetWindowRect(IntPtr hWnd, out RECT rect) => GetWindowRect(hWnd, out rect);

    public static bool IsDescendant(IntPtr parent, IntPtr child)
    {
        var cur = child;
        while (cur != IntPtr.Zero)
        {
            if (cur == parent) return true;
            cur = GetParent(cur);
        }
        return false;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    /// <summary>
    /// The file-display containers of a lister: one per pane per TAB, so a lister showing one tab on
    /// each side has two and one with a tab stack has more. An inactive tab keeps its container, merely
    /// hidden, and that is what <paramref name="visibleOnly"/> selects against: the active-path logic
    /// wants only the pane the user is looking at (a hidden tab's container must never answer for it),
    /// while the opened-folder snapshot wants every tab the user has open.
    /// </summary>
    public static List<IntPtr> GetContainers(IntPtr listerHwnd, bool visibleOnly)
    {
        var containers = new List<IntPtr>();
        EnumChildWindows(listerHwnd, (hWnd, lParam) =>
        {
            if (GetClassName(hWnd).Equals("dopus.filedisplaycontainer", StringComparison.OrdinalIgnoreCase))
            {
                if (!visibleOnly || IsWindowVisible(hWnd))
                {
                    containers.Add(hWnd);
                }
            }
            return true;
        }, IntPtr.Zero);
        return containers;
    }

    /// <summary>
    /// The union of a lister's VISIBLE file displays. Details/List draws into "dopus.filedisplay" and
    /// Thumbnails/Tiles/Large Icons into "dopus.iconfiledisplay", and the view mode a pane is not in keeps its
    /// window hidden, so only the visible ones count -- a dual-display lister answers with the area spanning
    /// both panes, which is where the card belongs.
    /// </summary>
    /// <remarks>
    /// Measured on a live lister, top to bottom: window frame 196, client area 227 (the system title bar band
    /// in between), two toolbar rows 227..266 and 266..298, each pane's own path row 299..330, and the file
    /// display 331..1027. Every one of those bands is a different window, which is why the card asks for this
    /// one by class rather than taking the lister's frame and subtracting something.
    /// </remarks>
    public static bool TryGetFileDisplayBounds(IntPtr listerHwnd, out RECT bounds)
    {
        var union = default(RECT);
        var found = false;
        EnumChildWindows(listerHwnd, (hWnd, lParam) =>
        {
            var className = GetClassName(hWnd);
            if (!className.Equals("dopus.filedisplay", StringComparison.OrdinalIgnoreCase)
                && !className.Equals("dopus.iconfiledisplay", StringComparison.OrdinalIgnoreCase))
                return true;
            if (!IsWindowVisible(hWnd) || !TryGetWindowRect(hWnd, out var rect)) return true;

            if (!found)
            {
                union = rect;
                found = true;
            }
            else
            {
                union.Left = Math.Min(union.Left, rect.Left);
                union.Top = Math.Min(union.Top, rect.Top);
                union.Right = Math.Max(union.Right, rect.Right);
                union.Bottom = Math.Max(union.Bottom, rect.Bottom);
            }
            return true;
        }, IntPtr.Zero);

        bounds = union;
        return found;
    }

    public static IntPtr FindWindowExRecursively(IntPtr parent, IntPtr childAfter, string className, string? windowName)
    {
        var child = FindWindowEx(parent, childAfter, className, windowName);
        if (child != IntPtr.Zero) return child;

        child = FindWindowEx(parent, IntPtr.Zero, null, null);
        while (child != IntPtr.Zero)
        {
            var result = FindWindowExRecursively(child, IntPtr.Zero, className, windowName);
            if (result != IntPtr.Zero) return result;
            child = FindWindowEx(parent, child, null, null);
        }

        return IntPtr.Zero;
    }
}
