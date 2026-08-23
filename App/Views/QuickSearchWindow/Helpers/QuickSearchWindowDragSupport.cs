using System.Windows;
using System.Windows.Input;
using Lertaro.Core;
using Lertaro.App.Helpers.Visuals;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace Lertaro.App.Views.QuickSearchWindow.Helpers;

// Extracted from QuickSearchWindow.xaml.cs to keep that file under the repo's per-file line limit.
public class QuickSearchWindowDragSupport
{
    private readonly Lertaro.App.QuickSearchWindow _window;
    private readonly WindowDragTracker _borderDragTracker;

    public QuickSearchWindowDragSupport(Lertaro.App.QuickSearchWindow window)
    {
        _window = window;
        _borderDragTracker = new WindowDragTracker(window);
    }

    public static bool ShouldStartDrag(MouseButton changedButton, bool lockPosition)
        => changedButton == MouseButton.Left && !lockPosition;

    public static bool ShouldAllowIconDrag(bool lockPosition) => !lockPosition;

    public void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!ShouldStartDrag(e.ChangedButton, UserSettings.Load().SearchWindow.LockPosition)) return;

        if (sender is IInputElement el) el.CaptureMouse();
        _borderDragTracker.Start(_window.PointToScreen(e.GetPosition(_window)));
    }

    public void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_borderDragTracker.IsDragging || e.LeftButton != MouseButtonState.Pressed)
            return;

        _borderDragTracker.Update(_window.PointToScreen(e.GetPosition(_window)));
    }

    public void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_borderDragTracker.IsDragging) return;

        _borderDragTracker.End();
        if (sender is IInputElement el) el.ReleaseMouseCapture();
        _window.PositionWindow();
    }
}
