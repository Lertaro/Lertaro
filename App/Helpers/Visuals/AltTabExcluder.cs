using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using Lertaro.App.Views.InlineSearchWindow.Helpers;

namespace Lertaro.App.Helpers.Visuals;

/// <summary>
/// Hides a window from the Windows Alt+Tab task switcher by applying WS_EX_TOOLWINDOW.
/// </summary>
public static class AltTabExcluder
{
    // Windows that are attached again on every park rather than once in their constructor: a window that has
    // not been shown yet would stack one SourceInitialized handler per call, and those can only fire once.
    private static readonly ConditionalWeakTable<Window, object> PendingWindow = new();

    /// <summary>Keeps <paramref name="window"/> out of the Alt+Tab list for the rest of its life. Safe to call
    /// more than once: an already-shown window has the bit re-asserted, a pending one is only registered once.
    /// </summary>
    public static void Attach(Window window)
    {
        window.ShowInTaskbar = false;
        if (PresentationSource.FromVisual(window) is HwndSource hwndSource)
        {
            Apply(hwndSource.Handle);
            return;
        }

        if (!PendingWindow.TryAdd(window, window)) return;
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            Apply(handle);
        };
    }

    private static void Apply(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        var exStyle = InlineSearchWindowNativeMethods.GetWindowLongPtr(hwnd, InlineSearchWindowNativeMethods.GWL_EXSTYLE);
        var newExStyle = new IntPtr(exStyle.ToInt64() | InlineSearchWindowNativeMethods.WS_EX_TOOLWINDOW);
        InlineSearchWindowNativeMethods.SetWindowLongPtr(hwnd, InlineSearchWindowNativeMethods.GWL_EXSTYLE, newExStyle);
    }
}
