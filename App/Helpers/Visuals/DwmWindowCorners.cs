using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Lertaro.App.Helpers.Visuals;

/// <summary>
/// Asks the desktop window manager to round a borderless window's own corners, so the rounding is the
/// window's shape rather than a painted trick inside it.
/// </summary>
/// <remarks>
/// A window rounds itself only on Windows 11 and newer; the call is skipped where it does not exist and the
/// corners stay square there. The benefit over a transparent window with a rounded Border is what this
/// project needed for notifications: content is clipped by the real window shape, the compositor owns the
/// corners, and text gets ClearType back because the window is no longer a layered one.
/// </remarks>
public static class DwmWindowCorners
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void ApplyRound(Window window)
    {
        // No gate on the version: the attribute simply fails on an OS that has never heard of it, and one
        // fewer platform check is one fewer thing to keep in step with the next Windows release.
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var preference = DwmwcpRound;
        _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
    }
}
