using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Lertaro.Core;

namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

public class InlineSearchWindowPositioner
{
    private const double DefaultWindowWidth = 465;
    private const double DockedWidthRatio = 2.0 / 3.0;
    private const double DesktopWidthRatio = 0.2;

    private readonly Lertaro.App.InlineSearchWindow _window;
    private readonly InlineCardDragOffset _dragOffset;
    private readonly InlineDialogGeometryProbe _geometry;
    private int _positionUpdateQueued;

    private bool _hasCachedInputs;
    private IntPtr _cachedActiveHwnd;
    private bool _cachedIsDesktop;
    private bool _cachedIsActiveWindowDialog;
    private bool _cachedHasValidRect;
    private Core.Hook.ExplorerTracker.RECT _cachedRect;
    private Core.Hook.ExplorerTracker.RECT? _cachedAnchor;
    private Core.Hook.ExplorerTracker.RECT? _cachedFileList;
    private System.Drawing.Point _cachedMousePosition;
    private double _cachedWindowWidth;
    private double _cachedWindowHeight;
    private bool _cachedIsResultsVisible;

    public InlineSearchWindowPositioner(Lertaro.App.InlineSearchWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _dragOffset = new InlineCardDragOffset(window);
        _geometry = new InlineDialogGeometryProbe(MeasureDialogGeometry, PositionWindow);
    }

    // The dialog's own rectangles, read off the thread that asks for them -- which is why they are read here
    // rather than inline in PositionWindowCore. An adapter that has to reach into a foreign process to answer
    // must not be able to stop the card from being placed.
    private InlineDialogGeometryProbe.Answer MeasureDialogGeometry(IntPtr hwnd)
    {
        var tracker = _window.Manager.ExplorerTracker;
        // A dialog that closed or was superseded mid-measurement has nothing left to answer for.
        if (tracker.ActiveHwnd != hwnd) return default;

        return new InlineDialogGeometryProbe.Answer(
            tracker.TryGetTargetFieldRect(out var anchor) ? anchor : null,
            tracker.TryGetFileListRect(out var list) ? list : null);
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

        // Nothing tracked and not the desktop: there is no edge to dock to and WorkingAreaFor answers
        // Empty, so every placement below would leave the target at its (0,0) initializer and fling the
        // still-visible card into the screen corner -- reachable in the 200ms between a deactivation and
        // the manager's deferred close, and at startup before the IPC mirror's first state arrives.
        // Keeping the last position for those moments is the honest answer.
        if (!tracker.IsDesktop && tracker.ActiveHwnd == IntPtr.Zero)
            return;

        var isResultsVisible = _window.ResultsPanelControl.Visibility == Visibility.Visible;

        var hasValidRect = false;
        var rect = new Core.Hook.ExplorerTracker.RECT();
        if (tracker.ActiveHwnd != IntPtr.Zero && !tracker.IsDesktop)
        {
            hasValidRect = tracker.TryGetActiveWindowRect(out rect) && (rect.Right - rect.Left > 100 && rect.Bottom - rect.Top > 100);
        }

        // Which part of that window the card's own text lands in, and that dialog's own file list. Only a
        // dialog's adapter can answer either, and only the ones that opt in do; no answer leaves the placement
        // exactly as it was before either was asked. Not answered here, on this thread: for a dialog whose
        // widgets have no window handles of their own it would be a call into that other process, which is
        // how a closing WPS dialog once took the whole application down with it -- InlineDialogGeometryProbe.
        var geometry = hasValidRect
            ? _geometry.Request(tracker.ActiveHwnd, rect.Right - rect.Left, rect.Bottom - rect.Top)
            : default;
        Core.Hook.ExplorerTracker.RECT? anchor = geometry.Anchor;
        Core.Hook.ExplorerTracker.RECT? fileList = geometry.FileList;
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

        if (_hasCachedInputs
            && _cachedActiveHwnd == tracker.ActiveHwnd
            && _cachedIsDesktop == tracker.IsDesktop
            && _cachedIsActiveWindowDialog == tracker.IsActiveWindowDialog
            && _cachedHasValidRect == hasValidRect
            && _cachedRect.Left == rect.Left && _cachedRect.Top == rect.Top && _cachedRect.Right == rect.Right && _cachedRect.Bottom == rect.Bottom
            && SameAnchor(_cachedAnchor, anchor)
            && SameAnchor(_cachedFileList, fileList)
            && _cachedWindowWidth == windowWidth
            && _cachedWindowHeight == windowHeight
            && _cachedIsResultsVisible == isResultsVisible
            && (!tracker.IsDesktop || _cachedMousePosition == mousePosition))
        {
            return;
        }

        const double xamlMargin = 12;
        const double visibleMargin = 0;

        // Whether the space under the anchored window can hold the WHOLE card. Asked of the tallest the card
        // can ever be (InlineCardSizingSupport.FullCardHeight), never of the height it happens to have: the
        // row count follows what the search returned, so a card measured as it is now picks a different
        // corner to hang from between two result counts -- which is the card jumping while the user types.
        //
        // Measured to the MONITOR's bottom edge, taskbar included. A card hanging below is allowed to cover
        // the taskbar, and on a screen where the dialog nearly fills the monitor that strip is the whole
        // difference between hanging below and lying over the dialog -- so the vertical clamp below has to
        // be measured the same way, or it would simply pull the card back up again.
        var screen = Screen.FromHandle(tracker.ActiveHwnd);
        var hangsBelow = false;
        if (hasValidRect)
        {
            var spaceBelow = screen.Bounds.Bottom - rect.Bottom;
            hangsBelow = InlineCardMetrics.HasRoomToHangBelow(
                spaceBelow / targetDpiScaleY, _window.CardSizing.FullCardHeight());
        }

        // Both placements draw the card as a drop-down from an edge the row count cannot move -- the
        // anchored window's bottom when there is room outside it, its top when there is not -- so they share
        // the internal layout as well as the horizontal anchor below.
        var dropDown = hasValidRect;

        ApplyLayoutMode(dropDown, isResultsVisible);

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
                var isDialog = tracker.IsActiveWindowDialog;

                // One call decides the horizontal anchor for both placements, so its rungs (the card centered
                // under the window it hangs below, a dialog's file list when it has to lie over the dialog,
                // the field a dialog feeds, the window's own right edge) cannot drift into disagreeing about
                // which corner the card hangs off.
                targetPhysLeft = CalculatePhysLeft(isDialog, hangsBelow, rect, fileList, anchor,
                    physWindowWidth, physXamlMargin, physVisibleMargin);

                // Outside below, or from the top edge of whatever the card has to cover. The top, not the
                // bottom, is what keeps a dialog's Open/Cancel row clear by arithmetic when it has to lie
                // inside: AvailableCardHeight caps the card at AnchoredWindowHeightShare of the window it
                // covers, so starting at the top is what leaves its bottom edge free -- and for a plain
                // window it is the same edge a row count cannot move.
                targetPhysTop = CalculatePhysTop(hangsBelow, rect, fileList, physXamlMarginY, physVisibleMarginY);

                var minLeft = workingArea.Left + physVisibleMargin - physXamlMargin;
                var minTop = workingArea.Top + physVisibleMarginY - physXamlMarginY;
                var maxLeft = workingArea.Right - physWindowWidth + physXamlMargin - physVisibleMargin;
                // Below, the monitor's own bottom edge, matching the room-below measurement above; over the
                // window, the working area, so a card that covers a dialog never runs off the screen.
                var maxTop = (hangsBelow ? screen.Bounds.Bottom : workingArea.Bottom)
                    - physWindowHeight + physXamlMarginY - physVisibleMarginY;

                // Guarded like the top clamp below: a target window spanning two monitors makes the card
                // (2/3 of it) wider than one monitor's working area, and Math.Clamp THROWS when min > max.
                targetPhysLeft = Math.Clamp(targetPhysLeft, minLeft, Math.Max(minLeft, maxLeft));

                // One clamp for all three placements. Measured with the window's height, which is the same
                // height the room-below question was answered with -- measuring the content instead looked
                // like the fix for a card shoved over a dialog's input row, but that was the two disagreeing,
                // and loosening this bound only let the card run past the bottom of the screen.
                targetPhysTop = Math.Clamp(targetPhysTop, minTop, Math.Max(minTop, maxTop));

                // The placement math in one line, at Debug: both reports about where the card lands were
                // diagnosed from outside the process, and this would have settled the second one at once. It
                // sits behind the input guard above, so it only speaks when something changed.
                Logger.Log(
                    "[InlineCard] "
                    + $"hwnd={tracker.ActiveHwnd:x8} dialog={isDialog} valid={hasValidRect} "
                    + $"rect={rect.Left},{rect.Top},{rect.Right},{rect.Bottom} "
                    + $"anchor={(anchor is { } a ? $"{a.Left},{a.Top},{a.Right},{a.Bottom}" : "none")} "
                    + $"list={(fileList is { } listRect ? $"{listRect.Right},{listRect.Top}" : "none")} "
                    + $"dpi={targetDpiScaleX:F2} window={windowWidth:F0}x{windowHeight:F0} "
                    + $"below={hangsBelow} boundsBottom={screen.Bounds.Bottom} work={workingArea.Left},{workingArea.Top},{workingArea.Right},{workingArea.Bottom} "
                    + $"bound={minTop:F0}..{maxTop:F0} at={targetPhysLeft:F0},{targetPhysTop:F0} drag={_dragOffset.IsSet}",
                    LogLevel.Debug);
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
        _cachedAnchor = anchor;
        _cachedFileList = fileList;
        _cachedMousePosition = mousePosition;
        _cachedWindowWidth = windowWidth;
        _cachedWindowHeight = windowHeight;
        _cachedIsResultsVisible = isResultsVisible;
    }

    // The card's internal order follows the direction it grows in: a drop-down puts the search box on top
    // and the list below it, an upward card from the anchored window's bottom edge the other way round. The
    // corner radii and the path banner's border follow the same split, so this is the one place that knows
    // which edge the card is attached by.
    private void ApplyLayoutMode(bool dropDown, bool isResultsVisible)
    {
        if (dropDown)
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

    /// <summary>
    /// Where a dialog's card starts horizontally, in physical pixels.
    /// </summary>
    /// <remarks>
    /// Centered on the dialog, unless that dialog named the field the card feeds -- WPS's is 960px wide with
    /// its file-name box starting 300px in, so centering parked the card's left edge 143px left of the box.
    /// Both dialog placements ask here, which is what keeps a resize from sliding the card sideways.
    /// </remarks>
    internal static double CalculateDialogPhysLeft(
        Core.Hook.ExplorerTracker.RECT dock,
        Core.Hook.ExplorerTracker.RECT? anchor,
        double physWindowWidth,
        double physXamlMargin,
        double physVisibleMargin)
    {
        if (anchor.HasValue)
            return anchor.Value.Right - physWindowWidth + physXamlMargin - physVisibleMargin;

        return dock.Left + ((dock.Right - dock.Left) - physWindowWidth) / 2.0;
    }

    /// <summary>
    /// Where the card's window starts horizontally, in physical pixels.
    /// </summary>
    /// <remarks>
    /// Below the window there is one answer for every host: centered under it, the taskbar allowed to be
    /// covered. Over it, a dialog whose adapter can see its own file list lines up on that list's right edge
    /// -- the same edge a plain window's card docks to, since a plain window's dock rect IS its file list --
    /// and a dialog that cannot see that far in gets the placement it has always had. Each rung names the
    /// edge the card's VISIBLE edge is meant to meet, and pays the window's own transparent margin back to
    /// get the window position that puts it there.
    /// </remarks>
    internal static double CalculatePhysLeft(
        bool isDialog,
        bool hangsBelow,
        Core.Hook.ExplorerTracker.RECT dock,
        Core.Hook.ExplorerTracker.RECT? fileList,
        Core.Hook.ExplorerTracker.RECT? field,
        double physWindowWidth,
        double physXamlMargin,
        double physVisibleMargin)
    {
        if (hangsBelow)
            return CalculateDialogPhysLeft(dock, null, physWindowWidth, physXamlMargin, physVisibleMargin);

        if (fileList.HasValue)
            return fileList.Value.Right - physWindowWidth + physXamlMargin - physVisibleMargin;

        return isDialog
            ? CalculateDialogPhysLeft(dock, field, physWindowWidth, physXamlMargin, physVisibleMargin)
            : dock.Right - physWindowWidth + physXamlMargin - physVisibleMargin;
    }

    /// <summary>Where the card's window starts vertically, in physical pixels.</summary>
    internal static double CalculatePhysTop(
        bool hangsBelow,
        Core.Hook.ExplorerTracker.RECT dock,
        Core.Hook.ExplorerTracker.RECT? fileList,
        double physXamlMarginY,
        double physVisibleMarginY)
    {
        if (hangsBelow)
            return dock.Bottom - physXamlMarginY + physVisibleMarginY;

        return (fileList.HasValue ? fileList.Value.Top : dock.Top) - physXamlMarginY;
    }

    private static bool SameAnchor(Core.Hook.ExplorerTracker.RECT? a, Core.Hook.ExplorerTracker.RECT? b) =>
        a?.Left == b?.Left && a?.Top == b?.Top && a?.Right == b?.Right && a?.Bottom == b?.Bottom;

    internal static double CalculateDesktopWidth(double desktopWidth) => desktopWidth * DesktopWidthRatio;
}
