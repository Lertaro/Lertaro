using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Lertaro.App.Helpers.Visuals;
using TextBox = System.Windows.Controls.TextBox;

namespace Lertaro.App.Views.SearchWindow;

public class SearchWindowChromeHandler
{
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
        var (left, top) = WindowCentering.CenterInWork(work.Left, work.Top, work.Width, work.Height,
            width, height, dpi.DpiScaleX, dpi.DpiScaleY);
        _window.Left = left;
        _window.Top = top;
    }

    public void Close() => _window.Close();
}
