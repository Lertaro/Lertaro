using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

public class InlineSearchWindowPositioner
{
    private const double DefaultWindowWidth = 465;
    private const double DockedWidthRatio = 2.0 / 3.0;
    private const double DesktopWidthRatio = 0.2;

    private readonly Lertaro.App.InlineSearchWindow _window;
    private readonly InlineCardDragOffset _dragOffset;
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

    public InlineSearchWindowPositioner(Lertaro.App.InlineSearchWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _dragOffset = new InlineCardDragOffset(window);
    }

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

        var isResultsVisible = _window.ResultsPanelControl.Visibility == Visibility.Visible;

        var hasValidRect = false;
        var rect = new Core.Hook.ExplorerTracker.RECT();
        if (tracker.ActiveHwnd != IntPtr.Zero && !tracker.IsDesktop)
        {
            hasValidRect = tracker.TryGetActiveWindowRect(out rect) && (rect.Right - rect.Left > 100 && rect.Bottom - rect.Top > 100);
        }
        var mousePosition = System.Windows.Forms.Control.MousePosition;

        var hwnd = new WindowInteropHelper(_window).Handle;
        var targetMonitor = tracker.IsDesktop
            ? (_window.IsVisible && hwnd != IntPtr.Zero
                ? InlineCardSpace.MonitorForWindow(hwnd)
                : InlineCardSpace.MonitorForPoint(mousePosition))
            : tracker.ActiveHwnd != IntPtr.Zero
                ? InlineCardSpace.MonitorForWindow(tracker.ActiveHwnd)
                : IntPtr.Zero;
        var (targetDpiScaleX, targetDpiScaleY) = InlineCardSpace.DpiScaleFor(targetMonitor);

        var desktopWidth = tracker.IsDesktop
            ? (_window.IsVisible && hwnd != IntPtr.Zero ? Screen.FromHandle(hwnd) : Screen.FromPoint(mousePosition)).WorkingArea.Width / targetDpiScaleX
            : 0;
        var desiredWidth = hasValidRect && !tracker.IsDesktop
            ? CalculateDockedWidth((rect.Right - rect.Left) / targetDpiScaleX)
            : desktopWidth > 0
                ? CalculateDesktopWidth(desktopWidth)
                : DefaultWindowWidth;
        if (Math.Abs(_window.Width - desiredWidth) > 0.5)
        {
            _window.Width = desiredWidth;
            _window.UpdateLayout();
        }

        var windowHeight = double.IsNaN(_window.Height) || _window.Height <= 0
            ? (_window.ActualHeight > 0 ? _window.ActualHeight : 550.0)
            : _window.Height;
        var windowWidth = _window.Width;

        var visibleHeight = _window.MainBorder.ActualHeight > 0
            ? _window.MainBorder.ActualHeight
            : windowHeight;

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

        var workingArea = InlineCardSpace.WorkingAreaFor(_window, mousePosition);

        if (tracker.IsDesktop)
        {
            targetPhysLeft = workingArea.Right - physWindowWidth + physXamlMargin - physVisibleMargin;
            targetPhysTop = workingArea.Bottom - physWindowHeight + physXamlMarginY - physVisibleMarginY;
        }
        else if (tracker.ActiveHwnd != IntPtr.Zero)
        {
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
                    var searchBoxHeight = _window.CardSizing.SearchBoxHeight();
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

        var targetLeft = targetPhysLeft / targetDpiScaleX;
        var targetTop = targetPhysTop / targetDpiScaleY;

        // The dock position on its own, which is what a user's drag is measured against -- recorded before that
        // displacement is applied below.
        _dragOffset.RememberBase(targetLeft, targetTop);

        if (_dragOffset.IsSet)
        {
            (targetPhysLeft, targetPhysTop) = _dragOffset.ApplyPhysical(
                targetPhysLeft, targetPhysTop, targetDpiScaleX, targetDpiScaleY, workingArea, physWindowWidth, physWindowHeight);
            targetLeft = targetPhysLeft / targetDpiScaleX;
            targetTop = targetPhysTop / targetDpiScaleY;
        }

        if (Math.Abs(_window.Left - targetLeft) > 0.5) _window.Left = targetLeft;
        if (Math.Abs(_window.Top - targetTop) > 0.5) _window.Top = targetTop;

        if (hwnd != IntPtr.Zero)
        {
            // Size AND position in one native call, deliberately. Resizing the window is anchored at its
            // top-left, so a height change on its own would leave the bottom edge (and with it the search
            // box) sitting at the old spot until the reposition below caught up -- and when the two land in
            // different frames that shows as the card visibly snapping upward before settling. Handing
            // Windows both at once removes the frame where they disagree. SWP_NOZORDER/NOACTIVATE keep the
            // rest of the window's state untouched, and this only ever runs from the layout path that has
            // just set Height/Left/Top, so the values are the ones WPF is about to render anyway.
            InlineSearchWindowNativeMethods.SetWindowPos(hwnd, IntPtr.Zero,
                (int)Math.Round(targetPhysLeft), (int)Math.Round(targetPhysTop),
                (int)Math.Round(_window.Width * targetDpiScaleX), (int)Math.Round(_window.Height * targetDpiScaleY),
                InlineSearchWindowNativeMethods.SWP_NOZORDER | InlineSearchWindowNativeMethods.SWP_NOACTIVATE);
        }

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
            Grid.SetRow(_window.PathPreviewBorder, 3);
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
            Grid.SetRow(_window.PathPreviewBorder, 0);
            Grid.SetRow(_window.ResultsContainerWrapper, 1);
            Grid.SetRow(_window.ResultsSeparator, 2);
            Grid.SetRow(_window.SearchBoxBorder, 3);
            _window.PathPreviewBorder.BorderThickness = new Thickness(0, 0, 0, 1);
            _window.PathPreviewBorder.CornerRadius = new CornerRadius(7, 7, 0, 0);
            _window.MainBorder.CornerRadius = new CornerRadius(8);
            _window.ClippingBorder.CornerRadius = new CornerRadius(8);
            _window.SearchBoxBorder.CornerRadius = isResultsVisible ? new CornerRadius(0, 0, 7, 7) : new CornerRadius(7);
        }
    }

    /// <summary>Records a drag the user has just finished, so the card keeps the position they left it at.</summary>
    /// <remarks>
    /// Wired to the search box logo's own drag (see InlineSearchWindow's constructor). Measured against the
    /// dock position the last positioning pass applied, which is exactly what a displacement is relative to.
    /// </remarks>
    public void RememberUserDrag() => _dragOffset.RememberDrag();

    internal static double CalculateDockedWidth(double targetWindowWidth) => targetWindowWidth * DockedWidthRatio;

    internal static double CalculateDesktopWidth(double desktopWidth) => desktopWidth * DesktopWidthRatio;
}
