using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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

    // Everything PositionWindowCore computes (Grid.Row layout mode, DPI-scaled Left/Top) is a pure
    // function of these inputs -- when every one of them is unchanged since the last full run, the
    // result would be identical too, so the DPI/monitor P/Invoke calls and Screen.FromHandle/FromPoint
    // lookups (the actually expensive part -- UpdateLayout() alone is cheap when the tree isn't dirty)
    // can be skipped outright instead of recomputing the same answer on every keystroke that doesn't
    // change the result count/height or move the target window.
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

    // Runs the same placement logic synchronously, before the window is ever shown -- called once from
    // InlineSearchManager.EnsureWindowCreated, right before Show(). PositionWindow() above always defers
    // to a later Render-priority dispatcher pass, which is fine for repositioning an already-visible
    // window (resize, active-window moved, ...) but means the very first paint happens at whatever
    // Left/Top this brand-new Window instance defaulted to -- Show() renders it there first, and only
    // the queued pass afterward snaps it to the real target. That's an unconditional flash-then-jump on
    // every single open, not just while ExplorerTracker's state happens to be wrong (see
    // ExplorerTracker.PublishCurrentState's own comment for that separate, now-fixed bug) -- computing
    // the real position up front and calling this before Show() eliminates it outright.
    public void PositionWindowImmediate() => PositionWindowCore();

    private void PositionWindowCore()
    {
        _window.UpdateLayout();
        var tracker = _window.Manager.ExplorerTracker;

        // Window height is fixed at 550 logical pixels to allow content to grow upwards/downwards internally
        var windowHeight = double.IsNaN(_window.Height) || _window.Height <= 0
            ? (_window.ActualHeight > 0 ? _window.ActualHeight : 550.0)
            : _window.Height;
        var windowWidth = _window.Width;

        // Get actual visible content height (MainBorder)
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

        // Everything below (Grid.Row layout mode, DPI-scaled Left/Top) is a pure function of the inputs
        // gathered above -- if none of them moved since the last full run, the DPI/monitor P/Invoke calls
        // and Screen.FromHandle/FromPoint lookups below would just recompute the exact same answer, so
        // skip straight past them. This is the actual expense (not the UpdateLayout() above, which is a
        // no-op when the visual tree isn't dirty) -- it fires on every keystroke that changes the result
        // count/height even when the target window hasn't moved and this window's own size hasn't changed.
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

        // DPI must come from the monitor this window is about to be placed ON (the Desktop's cursor
        // monitor, or the active window's monitor) -- NOT from wherever this window itself currently
        // happens to sit (PresentationSource.FromVisual(_window)'s own CompositionTarget, the old
        // source here). Those only agree when every monitor shares the same scale, or once the window
        // has already been correctly placed once. PositionWindowImmediate calls this before Show(),
        // when the window is sitting at whatever default position Windows picked for a brand-new HWND
        // -- on a mixed-DPI multi-monitor setup that default position's monitor has no relationship to
        // the actual target, so sourcing the scale from it computes a position wrong by exactly the
        // ratio between the two monitors' scales (e.g. summoning on a 150% secondary screen while this
        // window's own HWND landed on a 100% primary one).
        var targetMonitor = tracker.IsDesktop
            ? MonitorFromPoint(ToPoint(mousePosition), MONITOR_DEFAULTTONEAREST)
            : tracker.ActiveHwnd != IntPtr.Zero
                ? MonitorFromWindow(tracker.ActiveHwnd, MONITOR_DEFAULTTONEAREST)
                : IntPtr.Zero;
        var (dpiScaleX, dpiScaleY) = GetMonitorDpiScale(targetMonitor);

        // MainBorder in XAML has Margin="12" to make room for drop shadow.
        // We want the visible border to be exactly aligned to the screen/window corner.
        const double xamlMargin = 12;
        const double visibleMargin = 0;

        var useDialogMode = false;

        if (hasValidRect)
        {
            if (tracker.IsActiveWindowDialog)
            {
                var screen = Screen.FromHandle(tracker.ActiveHwnd);
                var workingArea = screen.WorkingArea;
                var winBottom = rect.Bottom * dpiScaleY;
                var spaceBelow = (workingArea.Bottom * dpiScaleY) - winBottom;
                if (spaceBelow >= (visibleHeight - xamlMargin))
                {
                    useDialogMode = true;
                }
            }
        }

        if (useDialogMode)
        {
            // Dialog mode: search box on top (Row 0), results on bottom (Row 2), content aligns to top of transparent window
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
            // ClippingBorder must track MainBorder's own CornerRadius exactly: it's what actually clips
            // content to the visible shape (see InlineSearchWindow.xaml's own comment on why ClipToBounds
            // moved off MainBorder), so if the two ever disagree, content clips to a different shape than
            // the chrome is drawn with -- square corners peeking past a rounded edge, or vice versa.
            _window.ClippingBorder.CornerRadius = new CornerRadius(0, 0, 8, 8);
            _window.SearchBoxBorder.CornerRadius = isResultsVisible ? new CornerRadius(0) : new CornerRadius(0, 0, 7, 7);
        }
        else
        {
            // Standard mode: results on top, search box on bottom, content aligns to bottom of transparent window
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

        if (tracker.IsDesktop)
        {
            // The desktop spans every monitor, so anchor to the one the cursor is on rather than always
            // the primary -- otherwise triggering inline search on a secondary screen jumps the window
            // to the primary's bottom-right corner.
            var screen = Screen.FromPoint(System.Windows.Forms.Control.MousePosition);
            var workingArea = screen.WorkingArea;
            var targetLeft = workingArea.Right * dpiScaleX - windowWidth + xamlMargin - visibleMargin;
            var targetTop = workingArea.Bottom * dpiScaleY - windowHeight + xamlMargin - visibleMargin;

            if (Math.Abs(_window.Left - targetLeft) > 0.5) _window.Left = targetLeft;
            if (Math.Abs(_window.Top - targetTop) > 0.5) _window.Top = targetTop;
        }
        else if (tracker.ActiveHwnd != IntPtr.Zero)
        {
            if (hasValidRect)
            {
                var winLeft = rect.Left * dpiScaleX;
                var winTop = rect.Top * dpiScaleY;
                var winRight = rect.Right * dpiScaleX;
                var winBottom = rect.Bottom * dpiScaleY;

                double targetLeft = 0;
                double targetTop = 0;

                if (useDialogMode)
                {
                    var winWidth = (rect.Right - rect.Left) * dpiScaleX;
                    targetLeft = winLeft + (winWidth - windowWidth) / 2;
                    // Align top of search window to bottom of dialog
                    targetTop = winBottom - xamlMargin + visibleMargin;
                }
                else if (tracker.IsActiveWindowDialog)
                {
                    // Standard (upward) mode: searchbox (Row 2 = bottom of card) should align to dialog bottom.
                    // card.Bottom = window.Top + windowHeight, and we want card.Bottom ≈ winBottom,
                    // but the card's bottom Row is the searchbox. Offset by one searchbox height to keep it visible.
                    var winWidth = (rect.Right - rect.Left) * dpiScaleX;
                    targetLeft = winLeft + (winWidth - windowWidth) / 2;
                    var searchBoxHeight = _window.SearchBoxBorder.ActualHeight > 0 ? _window.SearchBoxBorder.ActualHeight : 48.0;
                    targetTop = winBottom - windowHeight + xamlMargin + searchBoxHeight;
                }
                else
                {
                    targetLeft = winRight - windowWidth + xamlMargin - visibleMargin;
                    targetTop = winBottom - windowHeight + xamlMargin - visibleMargin;
                }

                // Constrain within the monitor work area where the active window is located
                var screen = Screen.FromHandle(tracker.ActiveHwnd);
                var workingArea = screen.WorkingArea;
                var minLeft = workingArea.Left * dpiScaleX + visibleMargin - xamlMargin;
                var minTop = workingArea.Top * dpiScaleY + visibleMargin - xamlMargin;
                var maxLeft = workingArea.Right * dpiScaleX - windowWidth + xamlMargin - visibleMargin;
                var maxTop = workingArea.Bottom * dpiScaleY - windowHeight + xamlMargin - visibleMargin;

                if (targetLeft < minLeft) targetLeft = minLeft;
                if (targetLeft > maxLeft) targetLeft = maxLeft;

                if (useDialogMode)
                {
                    // In dialog mode, keep the visible card bottom within the screen
                    var maxDialogModeTop = workingArea.Bottom * dpiScaleY - visibleHeight;
                    if (targetTop > maxDialogModeTop) targetTop = maxDialogModeTop;
                    if (targetTop < minTop) targetTop = minTop;
                }
                else if (tracker.IsActiveWindowDialog)
                {
                    // In standard mode on dialog, keep the visible card top within the screen
                    var minDialogModeTop = workingArea.Top * dpiScaleY - windowHeight + visibleHeight;
                    if (targetTop < minDialogModeTop) targetTop = minDialogModeTop;
                    if (targetTop > maxTop) targetTop = maxTop;
                }
                else
                {
                    if (targetTop < minTop) targetTop = minTop;
                    if (targetTop > maxTop) targetTop = maxTop;
                }

                if (Math.Abs(_window.Left - targetLeft) > 0.5) _window.Left = targetLeft;
                if (Math.Abs(_window.Top - targetTop) > 0.5) _window.Top = targetTop;
            }
            else
            {
                // Fallback: place the window safely in the bottom-right corner of the active window's monitor work area so it is fully visible
                var screen = tracker.ActiveHwnd != IntPtr.Zero
                    ? Screen.FromHandle(tracker.ActiveHwnd)
                    : Screen.PrimaryScreen ?? Screen.AllScreens[0];
                var workingArea = screen.WorkingArea;
                var targetLeft = workingArea.Right * dpiScaleX - windowWidth + xamlMargin - visibleMargin;
                var targetTop = workingArea.Bottom * dpiScaleY - windowHeight + xamlMargin - visibleMargin;

                if (Math.Abs(_window.Left - targetLeft) > 0.5) _window.Left = targetLeft;
                if (Math.Abs(_window.Top - targetTop) > 0.5) _window.Top = targetTop;
            }
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

    private static POINT ToPoint(System.Drawing.Point p) => new() { X = p.X, Y = p.Y };

    // Falls back to 1.0 (96 DPI, unscaled) if the monitor handle is invalid or the query fails --
    // GetDpiForMonitor has been available since Windows 8.1, so this should only trip on some
    // unexpected edge case, not any supported OS version.
    private static (double x, double y) GetMonitorDpiScale(IntPtr hMonitor)
    {
        if (hMonitor != IntPtr.Zero && GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out var dpiX, out var dpiY) == 0 && dpiX > 0 && dpiY > 0)
            return (96.0 / dpiX, 96.0 / dpiY);
        return (1.0, 1.0);
    }
}
