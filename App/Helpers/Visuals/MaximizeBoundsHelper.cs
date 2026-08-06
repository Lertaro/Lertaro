using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Lertaro.App.Helpers.Visuals;

// Shared by the settings window and the full search window, both of which have custom chrome.
// A WindowStyle=None + AllowsTransparency window doesn't get the OS's normal maximize-to-work-area
// behavior -- Windows falls back to maximizing it over the full monitor bounds, taskbar included.
// The previous fix reacted to that after the fact (shrinking Width/Height and repositioning once
// WindowState was already Maximized), which left a gap on whichever edge the taskbar occupies since
// the maximize had already happened by the time the correction ran. Intercepting WM_GETMINMAXINFO
// instead tells Windows the correct bounds *before* it maximizes, so it never gets it wrong.
internal static class MaximizeBoundsHelper
{
    private const int WM_GETMINMAXINFO = 0x0024;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    public static void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (PresentationSource.FromVisual(window) is HwndSource hwndSource)
        {
            AttachHook(hwndSource, window);
        }
        else
        {
            RoutedEventHandler? onLoaded = null;
            onLoaded = (_, _) =>
            {
                window.Loaded -= onLoaded;
                if (PresentationSource.FromVisual(window) is HwndSource source)
                {
                    AttachHook(source, window);
                }
            };
            window.Loaded += onLoaded;
        }
    }

    private static void AttachHook(HwndSource hwndSource, Window window) => hwndSource.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                                                                                 {
                                                                                     if (msg != WM_GETMINMAXINFO)
                                                                                         return IntPtr.Zero;

                                                                                     var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                                                                                     if (monitor == IntPtr.Zero)
                                                                                         return IntPtr.Zero;

                                                                                     var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
                                                                                     if (!GetMonitorInfo(monitor, ref info))
                                                                                         return IntPtr.Zero;

                                                                                     var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                                                                                     mmi.ptMaxPosition.X = info.rcWork.Left - info.rcMonitor.Left;
                                                                                     mmi.ptMaxPosition.Y = info.rcWork.Top - info.rcMonitor.Top;
                                                                                     mmi.ptMaxSize.X = info.rcWork.Right - info.rcWork.Left;
                                                                                     mmi.ptMaxSize.Y = info.rcWork.Bottom - info.rcWork.Top;
                                                                                     mmi.ptMaxTrackSize = mmi.ptMaxSize;

                                                                                     // Claiming this message (handled = true below) preempts WPF's own WM_GETMINMAXINFO handling,
                                                                                     // which is what normally derives ptMinTrackSize from Window.MinWidth/MinHeight -- without this,
                                                                                     // the OS silently falls back to its tiny system-default minimum track size, letting the window
                                                                                     // shrink far past MinWidth/MinHeight and clip the title bar's buttons/rounded corner (#153).
                                                                                     var toDevice = hwndSource.CompositionTarget.TransformToDevice;
                                                                                     var minSize = toDevice.Transform(new System.Windows.Point(window.MinWidth, window.MinHeight));
                                                                                     mmi.ptMinTrackSize.X = (int)minSize.X;
                                                                                     mmi.ptMinTrackSize.Y = (int)minSize.Y;

                                                                                     Marshal.StructureToPtr(mmi, lParam, true);
                                                                                     handled = true;
                                                                                     return IntPtr.Zero;
                                                                                 });

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point ptReserved;
        public Point ptMaxSize;
        public Point ptMaxPosition;
        public Point ptMinTrackSize;
        public Point ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public int dwFlags;
    }
}
