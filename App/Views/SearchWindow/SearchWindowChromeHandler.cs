using System.Windows;
using System.Windows.Input;
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

    public void Close() => _window.Close();
}
