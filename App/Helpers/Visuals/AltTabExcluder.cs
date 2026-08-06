using System.Windows;
using System.Windows.Interop;
using Lertaro.App.Views.InlineSearchWindow.Helpers;

namespace Lertaro.App.Helpers.Visuals;

/// <summary>
/// Hides a window from the Windows Alt+Tab task switcher by applying WS_EX_TOOLWINDOW.
/// </summary>
public static class AltTabExcluder
{
    public static void Attach(Window window)
    {
        window.ShowInTaskbar = false;
        if (PresentationSource.FromVisual(window) is HwndSource hwndSource)
        {
            Apply(hwndSource.Handle);
        }
        else
        {
            window.SourceInitialized += (_, _) =>
            {
                var handle = new WindowInteropHelper(window).Handle;
                Apply(handle);
            };
        }
    }

    private static void Apply(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        var exStyle = InlineSearchWindowNativeMethods.GetWindowLongPtr(hwnd, InlineSearchWindowNativeMethods.GWL_EXSTYLE);
        var newExStyle = new IntPtr(exStyle.ToInt64() | InlineSearchWindowNativeMethods.WS_EX_TOOLWINDOW);
        InlineSearchWindowNativeMethods.SetWindowLongPtr(hwnd, InlineSearchWindowNativeMethods.GWL_EXSTYLE, newExStyle);
    }
}
