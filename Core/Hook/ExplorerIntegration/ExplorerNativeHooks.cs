using System.Runtime.InteropServices;
using System.Text;

namespace Lertaro.Core.Hook;

public static class ExplorerNativeHooks
{
    public delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    public static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    /// <summary>
    /// Top-level windows in Z order, topmost first. Windows keeps the window the user most recently used
    /// highest among those that are still open, which is the closest thing to "the file manager I was just
    /// in" that exists without having watched the user switch windows.
    /// </summary>
    /// <remarks>Enumerates only; callers filter, and the delegate stays alive for the synchronous call.</remarks>
    public static IReadOnlyList<IntPtr> TopLevelWindowsInZOrder()
    {
        var windows = new List<IntPtr>();
        EnumWindows((hwnd, _) => { windows.Add(hwnd); return true; }, IntPtr.Zero);
        return windows;
    }

    public const uint EM_SETSEL = 0x00B1;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;

    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X, Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    /// <summary>
    /// Where a window's client area starts on screen, in physical pixels -- the bottom edge of whatever the
    /// system drew above it (its title bar, with the caption buttons at the right).
    /// </summary>
    /// <remarks>
    /// Safe from the thread that places the inline card: both calls read window information win32k already
    /// holds and neither sends a message into the owning process, which is the difference between these and
    /// the WM_GETTEXT reads that a host with a busy UI thread can park a caller behind for seconds.
    /// False for no window, a client area with no height (an iconic one), or a failed read, so a caller that
    /// has no answer for the window simply does not apply the value.
    /// </remarks>
    public static bool TryGetClientTopOnScreen(IntPtr hwnd, out int top)
    {
        top = 0;
        if (hwnd == IntPtr.Zero) return false;
        if (!GetClientRect(hwnd, out var client) || client.Bottom <= 0) return false;

        // The client area's own top-left, translated: (0,0) in client coordinates is that corner.
        var origin = new POINT { X = 0, Y = 0 };
        if (!ClientToScreen(hwnd, ref origin)) return false;

        top = origin.Y;
        return true;
    }

    public const uint EVENT_OBJECT_FOCUS = 0x8005;
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
    public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    public const uint WINEVENT_OUTOFCONTEXT = 0;
    public const uint GA_ROOTOWNER = 3;

    public static bool IsDesktopWindow(IntPtr hwnd, out string className)
    {
        className = "Unknown";
        if (hwnd == IntPtr.Zero) return false;

        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        className = sb.ToString();

        if (hwnd == GetShellWindow()) return true;

        if (className.Equals("Progman", StringComparison.OrdinalIgnoreCase))
            return true;

        if (className.Equals("WorkerW", StringComparison.OrdinalIgnoreCase))
        {
            var defView = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero)
                return true;
        }

        return false;
    }

    public static IntPtr FindSubEditBox(IntPtr parent)
    {
        var edit = FindWindowEx(parent, IntPtr.Zero, "Edit", null);
        if (edit != IntPtr.Zero) return edit;

        var child = FindWindowEx(parent, IntPtr.Zero, null, null);
        while (child != IntPtr.Zero)
        {
            var subEdit = FindSubEditBox(child);
            if (subEdit != IntPtr.Zero) return subEdit;
            child = FindWindowEx(parent, child, null, null);
        }
        return IntPtr.Zero;
    }

    public static IntPtr FindMainFileDialog(IntPtr hwnd)
    {
        var current = hwnd;
        while (current != IntPtr.Zero)
        {
            var sb = new StringBuilder(256);
            GetClassName(current, sb, sb.Capacity);
            if (sb.ToString().Equals("#32770", StringComparison.OrdinalIgnoreCase))
            {
                if (HasBreadcrumbParent(current))
                {
                    return current;
                }
            }
            current = GetParent(current);
        }
        return IntPtr.Zero;
    }

    public static bool IsCommonDialogClass(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString().Equals("#32770", StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasBreadcrumbParent(IntPtr parent)
    {
        var child = FindWindowEx(parent, IntPtr.Zero, null, null);
        while (child != IntPtr.Zero)
        {
            var classNameSb = new StringBuilder(256);
            GetClassName(child, classNameSb, classNameSb.Capacity);
            var className = classNameSb.ToString();

            if (className.Equals("Breadcrumb Parent", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (HasBreadcrumbParent(child))
            {
                return true;
            }

            child = FindWindowEx(parent, child, null, null);
        }
        return false;
    }
}
