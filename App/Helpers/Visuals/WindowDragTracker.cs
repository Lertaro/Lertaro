using System.Windows;
using System.Windows.Input;
using Point = System.Windows.Point;

namespace Lertaro.App.Helpers.Visuals;

/// <summary>
/// Manual window-drag tracker shared by every "drag this to move the window" gesture that needs to
/// support constraining movement to vertical-only while Ctrl is held -- Window.DragMove()'s native move
/// loop is a blocking modal call with no way to query or constrain it mid-drag, so this replaces it with
/// per-frame delta tracking instead. Checks Keyboard.Modifiers fresh on every Update() call (not just
/// once at drag start), so pressing or releasing Ctrl mid-drag takes effect immediately: the frozen axis
/// simply stops accumulating rather than the delta backlogging and jumping once released.
/// </summary>
public sealed class WindowDragTracker
{
    private readonly Window _window;
    private Point? _lastScreenPoint;

    public WindowDragTracker(Window window) => _window = window;

    public bool IsDragging => _lastScreenPoint != null;

    public void Start(Point currentScreenPoint) => _lastScreenPoint = currentScreenPoint;

    public void Update(Point currentScreenPoint)
    {
        if (_lastScreenPoint == null)
            return;

        // currentScreenPoint is in physical pixels (Visual.PointToScreen); Window.Top/Left are in WPF's
        // device-independent units, so the raw pixel delta needs this same TransformFromDevice-based
        // conversion QuickNavigationMenu.Show already uses, or the window would move faster or slower
        // than the mouse on any monitor that isn't at 100% scaling.
        double dpiScaleX = 1.0, dpiScaleY = 1.0;
        var target = PresentationSource.FromVisual(_window)?.CompositionTarget;
        if (target != null)
        {
            dpiScaleX = target.TransformFromDevice.M11;
            dpiScaleY = target.TransformFromDevice.M22;
        }

        var deltaX = (currentScreenPoint.X - _lastScreenPoint.Value.X) * dpiScaleX;
        var deltaY = (currentScreenPoint.Y - _lastScreenPoint.Value.Y) * dpiScaleY;

        if (Keyboard.Modifiers != ModifierKeys.Control)
            _window.Left += deltaX;
        _window.Top += deltaY;

        _lastScreenPoint = currentScreenPoint;
    }

    public void End() => _lastScreenPoint = null;
}
