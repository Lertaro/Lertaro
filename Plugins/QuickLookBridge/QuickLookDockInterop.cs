using System.Runtime.InteropServices;

namespace Lertaro.Plugins.QuickLookBridge;

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left, Top, Right, Bottom;
}

// Just enough Win32 to find QuickLook's top-level window by process ID and move it with SetWindowPos --
// unlike the abandoned embedding attempt, this never touches window styles or the parent/child
// relationship, so it can't corrupt QuickLook's own window chrome or crash it on teardown. Moving another
// process's top-level window this way is the same, well-precedented technique window-snapping utilities
// (e.g. FancyZones) use; the only real risk is QuickLook's own internal layout code re-centering the
// window again on its own schedule and fighting this back, not anything structurally dangerous.
internal static class QuickLookDockInterop
{
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;

    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}
