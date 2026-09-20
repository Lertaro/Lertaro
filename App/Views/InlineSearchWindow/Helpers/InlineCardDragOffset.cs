namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

/// <summary>
/// A drag the user performed on the inline card, kept as a displacement from the position the card docks to.
/// </summary>
/// <remarks>
/// A displacement rather than an absolute position, because every later positioning pass re-docks the card --
/// the dialog moved, the monitor's DPI changed, the card grew or shrank -- and a remembered absolute position
/// would be thrown away by the next one of those. That is the trap the quick window's own card drag fell into
/// (#255), where dragging the card appeared to do nothing at all. Split out purely to keep
/// InlineSearchWindowPositioner under the repo's per-file line limit; it holds two numbers and always answers
/// for the one window that owns it.
/// </remarks>
internal sealed class InlineCardDragOffset
{
    private readonly Lertaro.App.InlineSearchWindow _window;
    private double _baseLeft;
    private double _baseTop;
    private double _offsetX;
    private double _offsetY;

    internal InlineCardDragOffset(Lertaro.App.InlineSearchWindow window) => _window = window;

    /// <summary>Whether the user has moved this card at all.</summary>
    internal bool IsSet => _offsetX != 0 || _offsetY != 0;

    /// <summary>The dock position the displacement is measured from. Recorded before it is applied.</summary>
    internal void RememberBase(double left, double top)
    {
        _baseLeft = left;
        _baseTop = top;
    }

    /// <summary>Records the drag that has just finished, from where the card has been left.</summary>
    /// <remarks>
    /// ponytail: the displacement is only known once the drag ENDS, so a content refresh that re-sizes the card
    /// mid-drag re-anchors it and that part of the travel is lost (the drag then keeps following the cursor from
    /// there). Rare enough to leave: the empty state's opened-folders snapshot is requested once per window, and
    /// nothing else changes the card's size while the mouse is captured. The upgrade path is a drag-started
    /// signal from SearchBoxControl that makes the positioner stand down until the drag ends.
    /// </remarks>
    internal void RememberDrag()
    {
        _offsetX = _window.Left - _baseLeft;
        _offsetY = _window.Top - _baseTop;
    }

    /// <summary>Applies this card's own displacement to a dock position, in physical pixels.</summary>
    internal (double Left, double Top) ApplyPhysical(
        double left,
        double top,
        double dpiScaleX,
        double dpiScaleY,
        Rectangle workingArea,
        double windowWidth,
        double windowHeight) =>
        Apply(left, top, _offsetX, _offsetY, dpiScaleX, dpiScaleY, workingArea, windowWidth, windowHeight);

    /// <summary>
    /// The displacement arithmetic on its own, so it is testable without a window: scaled by the monitor's DPI
    /// and kept on the working area.
    /// </summary>
    /// <remarks>
    /// The displacement is in DIP (it comes from a WPF Window.Left/Top delta) while the dock position is in
    /// physical pixels, so the two are only compatible after this scaling -- skipping it would move the card by
    /// the wrong distance on any display that is not at 100%.
    ///
    /// Clamped on its own, unlike the dock position: a dock has already picked where the card belongs, while a
    /// displacement the user chose only has to stay reachable -- which includes after a later re-dock (a dialog
    /// dragged to another monitor) moves the position it is added to. An empty working area (no tracked window
    /// and not the desktop) has nothing to clamp against, so the displacement is applied as it is.
    /// </remarks>
    internal static (double Left, double Top) Apply(
        double left,
        double top,
        double offsetX,
        double offsetY,
        double dpiScaleX,
        double dpiScaleY,
        Rectangle workingArea,
        double windowWidth,
        double windowHeight)
    {
        left += offsetX * dpiScaleX;
        top += offsetY * dpiScaleY;

        if (workingArea.Width <= 0 || workingArea.Height <= 0)
            return (left, top);

        return (
            Math.Clamp(left, workingArea.Left, Math.Max(workingArea.Left, workingArea.Right - windowWidth)),
            Math.Clamp(top, workingArea.Top, Math.Max(workingArea.Top, workingArea.Bottom - windowHeight)));
    }
}
