using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

public class InlineSearchWindowPositioner
{
    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private const int MDT_EFFECTIVE_DPI = 0;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    private readonly Lertaro.App.InlineSearchWindow _window;
    private int _positionUpdateQueued;

    private bool _hasCachedInputs;
    private IntPtr _cachedActiveHwnd;
    private bool _cachedIsDesktop;
    private bool _cachedIsActiveWindowDialog;
    private bool _cachedHasValidRect;
    private Core.Hook.ExplorerTracker.RECT _cachedRect;
    private System.Drawing.Point _cachedMousePosition;
    private double _cachedWindowWidth;
    private double _cachedWindowHeight;
    private double _cachedVisibleHeight;
    private bool _cachedIsResultsVisible;

    public InlineSearchWindowPositioner(Lertaro.App.InlineSearchWindow window) => _window = window ?? throw new ArgumentNullException(nameof(window));

    public void PositionWindow()
    {
        if (Interlocked.Exchange(ref _positionUpdateQueued, 1) == 1)
            return;

        _window.Dispatcher.BeginInvoke(new Action(() =>
        {
            Interlocked.Exchange(ref _positionUpdateQueued, 0);
            if (_window.IsVisible)
                PositionWindowCore();
        }), DispatcherPriority.Render);
    }

    public void PositionWindowImmediate() => PositionWindowCore();

    private void PositionWindowCore()
    {
        _window.UpdateLayout();
        var tracker = _window.Manager.ExplorerTracker;

        var windowHeight = double.IsNaN(_window.Height) || _window.Height <= 0
            ? (_window.ActualHeight > 0 ? _window.ActualHeight : 550.0)
            : _window.Height;
        var windowWidth = _window.Width;

        var visibleHeight = _window.MainBorder.ActualHeight > 0
            ? _window.MainBorder.ActualHeight
            : windowHeight;

        var isResultsVisible = _window.ResultsPanelControl.Visibility == Visibility.Visible;

        var hasValidRect = false;
        var rect = new Core.Hook.ExplorerTracker.RECT();
        if (tracker.ActiveHwnd != IntPtr.Zero && !tracker.IsDesktop)
        {
            hasValidRect = tracker.TryGetActiveWindowRect(out rect) && (rect.Right - rect.Left > 100 && rect.Bottom - rect.Top > 100);
        }
        var mousePosition = System.Windows.Forms.Control.MousePosition;

        if (_hasCachedInputs
            && _cachedActiveHwnd == tracker.ActiveHwnd
            && _cachedIsDesktop == tracker.IsDesktop
            && _cachedIsActiveWindowDialog == tracker.IsActiveWindowDialog
            && _cachedHasValidRect == hasValidRect
            && _cachedRect.Left == rect.Left && _cachedRect.Top == rect.Top && _cachedRect.Right == rect.Right && _cachedRect.Bottom == rect.Bottom
            && _cachedWindowWidth == windowWidth
            && _cachedWindowHeight == windowHeight
            && _cachedVisibleHeight == visibleHeight
            && _cachedIsResultsVisible == isResultsVisible
            && (!tracker.IsDesktop || _cachedMousePosition == mousePosition))
        {
            return;
        }

        var targetMonitor = tracker.IsDesktop
            ? MonitorFromPoint(ToPoint(mousePosition), MONITOR_DEFAULTTONEAREST)
            : tracker.ActiveHwnd != IntPtr.Zero
                ? MonitorFromWindow(tracker.ActiveHwnd, MONITOR_DEFAULTTONEAREST)
                : IntPtr.Zero;
        var (targetDpiScaleX, targetDpiScaleY) = GetMonitorDpiScale(targetMonitor);

        const double xamlMargin = 12;
        const double visibleMargin = 0;

        var useDialogMode = false;
        if (hasValidRect && tracker.IsActiveWindowDialog)
        {
            var screen = Screen.FromHandle(tracker.ActiveHwnd);
            var spaceBelow = screen.WorkingArea.Bottom - rect.Bottom;
            if (spaceBelow >= (visibleHeight - xamlMargin) * targetDpiScaleY)
                useDialogMode = true;
        }

        ApplyLayoutMode(useDialogMode, isResultsVisible);

        var physWindowWidth = windowWidth * targetDpiScaleX;
        var physWindowHeight = windowHeight * targetDpiScaleY;
        var physXamlMargin = xamlMargin * targetDpiScaleX;
        var physVisibleMargin = visibleMargin * targetDpiScaleX;
        var physXamlMarginY = xamlMargin * targetDpiScaleY;
        var physVisibleMarginY = visibleMargin * targetDpiScaleY;

        double targetPhysLeft = 0;
        double targetPhysTop = 0;

        if (tracker.IsDesktop)
        {
            var screen = Screen.FromPoint(mousePosition);
            var workingArea = screen.WorkingArea;
            targetPhysLeft = workingArea.Right - physWindowWidth + physXamlMargin - physVisibleMargin;
            targetPhysTop = workingArea.Bottom - physWindowHeight + physXamlMarginY - physVisibleMarginY;
        }
        else if (tracker.ActiveHwnd != IntPtr.Zero)
        {
            var screen = Screen.FromHandle(tracker.ActiveHwnd);
            var workingArea = screen.WorkingArea;

            if (hasValidRect)
            {
                if (useDialogMode)
                {
                    var winWidth = rect.Right - rect.Left;
                    targetPhysLeft = rect.Left + (winWidth - physWindowWidth) / 2.0;
                    targetPhysTop = rect.Bottom - physXamlMarginY + physVisibleMarginY;
                }
                else if (tracker.IsActiveWindowDialog)
                {
                    var winWidth = rect.Right - rect.Left;
                    targetPhysLeft = rect.Left + (winWidth - physWindowWidth) / 2.0;
                    var searchBoxHeight = _window.SearchBoxBorder.ActualHeight > 0 ? _window.SearchBoxBorder.ActualHeight : 48.0;
                    targetPhysTop = rect.Bottom - physWindowHeight + physXamlMarginY + searchBoxHeight * targetDpiScaleY;
                }
                else
                {
                    targetPhysLeft = rect.Right - physWindowWidth + physXamlMargin - physVisibleMargin;
                    targetPhysTop = rect.Bottom - physWindowHeight + physXamlMarginY - physVisibleMarginY;
                }

                var minLeft = workingArea.Left + physVisibleMargin - physXamlMargin;
                var minTop = workingArea.Top + physVisibleMarginY - physXamlMarginY;
                var maxLeft = workingArea.Right - physWindowWidth + physXamlMargin - physVisibleMargin;
                var maxTop = workingArea.Bottom - physWindowHeight + physXamlMarginY - physVisibleMarginY;

                targetPhysLeft = Math.Clamp(targetPhysLeft, minLeft, maxLeft);

                if (useDialogMode)
                {
                    var maxDialogModeTop = workingArea.Bottom - visibleHeight * targetDpiScaleY;
                    targetPhysTop = Math.Clamp(targetPhysTop, minTop, Math.Max(minTop, maxDialogModeTop));
                }
                else if (tracker.IsActiveWindowDialog)
                {
                    var minDialogModeTop = workingArea.Top - physWindowHeight + visibleHeight * targetDpiScaleY;
                    targetPhysTop = Math.Clamp(targetPhysTop, minDialogModeTop, Math.Max(minDialogModeTop, maxTop));
                }
                else
                {
                    targetPhysTop = Math.Clamp(targetPhysTop, minTop, maxTop);
                }
            }
            else
            {
                targetPhysLeft = workingArea.Right - physWindowWidth + physXamlMargin - physVisibleMargin;
                targetPhysTop = workingArea.Bottom - physWindowHeight + physXamlMarginY - physVisibleMarginY;
            }
        }

        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd != IntPtr.Zero)
        {
            InlineSearchWindowNativeMethods.SetWindowPos(hwnd, IntPtr.Zero,
                (int)Math.Round(targetPhysLeft), (int)Math.Round(targetPhysTop), 0, 0,
                InlineSearchWindowNativeMethods.SWP_NOSIZE | InlineSearchWindowNativeMethods.SWP_NOZORDER | InlineSearchWindowNativeMethods.SWP_NOACTIVATE);
        }

        var targetLeft = targetPhysLeft / targetDpiScaleX;
        var targetTop = targetPhysTop / targetDpiScaleY;

        if (Math.Abs(_window.Left - targetLeft) > 0.5) _window.Left = targetLeft;
        if (Math.Abs(_window.Top - targetTop) > 0.5) _window.Top = targetTop;

        _hasCachedInputs = true;
        _cachedActiveHwnd = tracker.ActiveHwnd;
        _cachedIsDesktop = tracker.IsDesktop;
        _cachedIsActiveWindowDialog = tracker.IsActiveWindowDialog;
        _cachedHasValidRect = hasValidRect;
        _cachedRect = rect;
        _cachedMousePosition = mousePosition;
        _cachedWindowWidth = windowWidth;
        _cachedWindowHeight = windowHeight;
        _cachedVisibleHeight = visibleHeight;
        _cachedIsResultsVisible = isResultsVisible;
    }

    private void ApplyLayoutMode(bool useDialogMode, bool isResultsVisible)
    {
        if (useDialogMode)
        {
            _window.RootGrid.VerticalAlignment = VerticalAlignment.Top;
            _window.MainBorder.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetRow(_window.SearchBoxBorder, 0);
            Grid.SetRow(_window.ResultsSeparator, 1);
            Grid.SetRow(_window.ResultsContainerWrapper, 2);
            Grid.SetRow(_window.PathPreviewBorder, 1);
            Grid.SetRow(_window.ResultsPanelControl, 0);
            _window.PathPreviewBorder.BorderThickness = new Thickness(0, 1, 0, 0);
            _window.PathPreviewBorder.CornerRadius = new CornerRadius(0, 0, 7, 7);
            _window.MainBorder.CornerRadius = new CornerRadius(0, 0, 8, 8);
            _window.ClippingBorder.CornerRadius = new CornerRadius(0, 0, 8, 8);
            _window.SearchBoxBorder.CornerRadius = isResultsVisible ? new CornerRadius(0) : new CornerRadius(0, 0, 7, 7);
        }
        else
        {
            _window.RootGrid.VerticalAlignment = VerticalAlignment.Bottom;
            _window.MainBorder.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetRow(_window.ResultsContainerWrapper, 0);
            Grid.SetRow(_window.ResultsSeparator, 1);
            Grid.SetRow(_window.SearchBoxBorder, 2);
            Grid.SetRow(_window.PathPreviewBorder, 0);
            Grid.SetRow(_window.ResultsPanelControl, 1);
            _window.PathPreviewBorder.BorderThickness = new Thickness(0, 0, 0, 1);
            _window.PathPreviewBorder.CornerRadius = new CornerRadius(7, 7, 0, 0);
            _window.MainBorder.CornerRadius = new CornerRadius(8);
            _window.ClippingBorder.CornerRadius = new CornerRadius(8);
            _window.SearchBoxBorder.CornerRadius = isResultsVisible ? new CornerRadius(0, 0, 7, 7) : new CornerRadius(7);
        }
    }

    private static POINT ToPoint(System.Drawing.Point p) => new() { X = p.X, Y = p.Y };

    private static (double x, double y) GetMonitorDpiScale(IntPtr hMonitor)
    {
        if (hMonitor != IntPtr.Zero && GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out var dpiX, out var dpiY) == 0 && dpiX > 0 && dpiY > 0)
            return (dpiX / 96.0, dpiY / 96.0);
        return (1.0, 1.0);
    }
}
