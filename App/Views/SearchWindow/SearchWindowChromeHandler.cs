using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Lertaro.App.Helpers.Visuals;
using Lertaro.App.Views.InlineSearchWindow.Helpers;
using TextBox = System.Windows.Controls.TextBox;

namespace Lertaro.App.Views.SearchWindow;

public class SearchWindowChromeHandler
{
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    private readonly Lertaro.App.SearchWindow _window;

    public SearchWindowChromeHandler(Lertaro.App.SearchWindow window) => _window = window;

    public void HandleHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is TextBox)
        {
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
            }
            else
            {
                WindowMaximizedDragHelper.DragMoveOrRestore(_window, e);
            }
        }
    }

    public void HandleStateChanged()
    {
        _window.BtnMaximize?.Content = _window.WindowState == WindowState.Maximized ? "\uE923" : "\uE922";

        if (_window.MainBorder != null && _window.ClippingBorder != null)
        {
            if (_window.WindowState == WindowState.Maximized)
            {
                _window.MainBorder.CornerRadius = new CornerRadius(0);
                _window.MainBorder.Margin = new Thickness(0);
                _window.MainBorder.BorderThickness = new Thickness(0);
                _window.ClippingBorder.CornerRadius = new CornerRadius(0);
            }
            else
            {
                // No halo in the normal state either: the window is opaque and IS the card, so a margin
                // here would only inset the visible card inside its own window rectangle (which is what
                // the transparent window used it for).
                // The radius comes back from the same token the markup set, not from a number repeated
                // here: this used to say 10 while the XAML said whatever the token said, and the two only
                // happened to agree until the token moved.
                var resting = (CornerRadius)_window.FindResource("CornerRadiusWindow");
                _window.MainBorder.CornerRadius = resting;
                _window.MainBorder.Margin = new Thickness(0);
                _window.MainBorder.BorderThickness = new Thickness(1);
                _window.ClippingBorder.CornerRadius = resting;
            }
        }
    }

    public void Minimize() => _window.WindowState = WindowState.Minimized;

    public void ToggleMaximize() => _window.WindowState = _window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    /// <summary>
    /// Puts the window in the middle of the work area of the screen the mouse is on -- the screen the summon came
    /// from -- and is called around the first Show. The logo's right-click stays on
    /// <see cref="CenterInWorkArea"/>, which deliberately does not change screens.
    /// </summary>
    /// <remarks>
    /// This replaces the startup location, which is not the same thing: WPF's CenterScreen does follow the cursor's
    /// monitor (no-owner path: MonitorFromPoint of the cursor, then the center of that monitor's work area), but it
    /// sizes the window with the scale it holds at that moment -- the system's, for a window that has never been
    /// shown -- and the window is then resized to the monitor's own scale, so the center it aimed at is the center
    /// for a smaller window. Measured on a 100% primary plus a 250% secondary with 854x480 DIP: the rect came out
    /// (3093,768) at 2135x1200 while that work area's own center is (2453,408), off by +641,+360 = exactly
    /// (new size - old size) / 2, toward the bottom-right. Same-scale desktops hide this, because the two scales are
    /// equal; scratch/centerprobe is the instrument that reads it without showing anything.
    ///
    /// Placed twice, like QuickSearchWindowPositioner: once before Show and once after the first frame. The second
    /// pass is the one that counts -- Left/Top are DIP and WPF converts a never-shown window's with the system
    /// scale, so the first pass alone creates the window on the WRONG monitor (measured: (981,163) on the primary)
    /// and SetWindowPos in physical pixels is what moves it onto the target and keeps it there.
    /// </remarks>
    public void CenterOnCursorScreen()
    {
        var mouse = Control.MousePosition;
        var work = Screen.FromPoint(mouse).WorkingArea;
        var (scaleX, scaleY) = InlineCardSpace.DpiScaleFor(InlineCardSpace.MonitorForPoint(mouse));

        _window.WindowStartupLocation = WindowStartupLocation.Manual;
        var (left, top) = CalculateCenter(work.Left, work.Top, work.Width, work.Height,
            _window.Width, _window.Height, scaleX, scaleY);
        _window.Left = left;
        _window.Top = top;

        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd == IntPtr.Zero) return;

        SetWindowPos(hwnd, IntPtr.Zero,
            (int)Math.Round(left * scaleX), (int)Math.Round(top * scaleY), 0, 0,
            SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    /// <summary>
    /// Moves the whole window to the middle of the work area of the screen it is on: the area the taskbar is
    /// not part of, so a centered window never ends up under it. Wired to a right-click on the logo.
    /// </summary>
    public void CenterInWorkArea()
    {
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd == IntPtr.Zero) return;

        double width, height;
        if (_window.WindowState == WindowState.Maximized)
        {
            // A maximized window ignores Left/Top, so the gesture restores it first -- and what it restores to
            // is the size worth centering, since the live extent reads as the whole work area while maximized.
            width = _window.RestoreBounds.Width;
            height = _window.RestoreBounds.Height;
            _window.WindowState = WindowState.Normal;
        }
        else
        {
            width = _window.ActualWidth;
            height = _window.ActualHeight;
        }

        var dpi = VisualTreeHelper.GetDpi(_window);
        var work = Screen.FromHandle(hwnd).WorkingArea;
        var (left, top) = CalculateCenter(work.Left, work.Top, work.Width, work.Height,
            width, height, dpi.DpiScaleX, dpi.DpiScaleY);
        _window.Left = left;
        _window.Top = top;
    }

    /// <summary>
    /// Where a window of the given DIP size goes to sit in the middle of a work area given in physical pixels,
    /// answered in DIP.
    /// </summary>
    /// <remarks>
    /// The two units are why this is a function with its own tests rather than two lines at the call site: the
    /// screen is read in pixels while Left/Top are DIP, so the window's size is scaled up to be centered and
    /// the answer scaled back down, and on a mixed-DPI desktop the scale is whichever monitor the window is on.
    /// </remarks>
    internal static (double Left, double Top) CalculateCenter(
        int workLeft, int workTop, int workWidth, int workHeight,
        double windowDipWidth, double windowDipHeight,
        double dpiScaleX, double dpiScaleY)
    {
        var scaleX = dpiScaleX > 0 ? dpiScaleX : 1.0;
        var scaleY = dpiScaleY > 0 ? dpiScaleY : 1.0;

        // Maxed against the area's own edge: a window larger than the area cannot be centered in it, and
        // letting the arithmetic go negative would push the header -- the only thing that can drag the window
        // back -- off screen.
        var physLeft = Math.Max(workLeft, workLeft + (workWidth - windowDipWidth * scaleX) / 2.0);
        var physTop = Math.Max(workTop, workTop + (workHeight - windowDipHeight * scaleY) / 2.0);
        return (physLeft / scaleX, physTop / scaleY);
    }

    public void Close() => _window.Close();
}
