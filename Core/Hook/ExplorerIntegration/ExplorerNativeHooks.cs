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

    public const uint EM_SETSEL = 0x00B1;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;

    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
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
