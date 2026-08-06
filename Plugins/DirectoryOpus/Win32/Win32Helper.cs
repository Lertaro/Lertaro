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
    private static extern int SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, StringBuilder lParam);

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    private const uint WM_GETTEXT = 0x000D;
    private const int GWL_STYLE = -16;
    private const int WS_VISIBLE = 0x10000000;

    public static string GetWindowText(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return string.Empty;
        var sb = new StringBuilder(512);
        SendMessage(hWnd, WM_GETTEXT, (IntPtr)sb.Capacity, sb);
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

    public static bool IsLocalWindowVisible(IntPtr hWnd) => (GetWindowLong(hWnd, GWL_STYLE) & WS_VISIBLE) != 0;

    public static List<IntPtr> GetVisibleContainers(IntPtr listerHwnd)
    {
        var containers = new List<IntPtr>();
        EnumChildWindows(listerHwnd, (hWnd, lParam) =>
        {
            if (GetClassName(hWnd).Equals("dopus.filedisplaycontainer", StringComparison.OrdinalIgnoreCase))
            {
                if (IsWindowVisible(hWnd))
                {
                    containers.Add(hWnd);
                }
            }
            return true;
        }, IntPtr.Zero);
        return containers;
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
