namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

/// <summary>
/// A drag the user performed on the inline card, kept as a displacement from the position the card docks to,
/// one displacement per host application.
/// </summary>
/// <remarks>
/// A displacement rather than an absolute position, because every later positioning pass re-docks the card --
/// the dialog moved, the monitor's DPI changed, the card grew or shrank -- and a remembered absolute position
/// would be thrown away by the next one of those. That is the trap the quick window's own card drag fell into
/// (#255), where dragging the card appeared to do nothing at all.
///
/// Host-scoped because the card is ONE window reused across hosts: a dismissed card is parked, not destroyed
/// (InlineSearchWindowSummonSupport), so that the next summon does not pay 170-258ms of composition for a
/// window the user is already looking at. One global displacement therefore travelled with it -- moving the
/// card inside Directory Opus moved the card inside Explorer. Each application now keeps its own, kept only
/// for this run of the process, and the desktop is its own bucket because its window belongs to explorer.exe,
/// the same process as every Explorer folder window.
///
/// Split out purely to keep InlineSearchWindowPositioner under the repo's per-file line limit. Takes the
/// window's position as a function rather than the window so the keying is testable without a WPF window.
/// </remarks>
internal sealed class InlineCardDragOffset
{
    /// <summary>The bucket the desktop's displacement lives in, kept apart from explorer.exe's.</summary>
    internal const string DesktopHost = "desktop";

    private readonly Func<(double Left, double Top)> _position;
    private readonly Dictionary<string, (double X, double Y)> _offsets = new(StringComparer.OrdinalIgnoreCase);
    private string _host = string.Empty;
    private double _baseLeft;
    private double _baseTop;

    internal InlineCardDragOffset(Func<(double Left, double Top)> position) => _position = position;

    /// <summary>Whether the user has moved the card on this host at all.</summary>
    internal bool IsSetFor(string host) => _offsets.ContainsKey(host);

    /// <summary>Drops one host's displacement, so its next placement puts the card back where it docks.</summary>
    internal void ClearFor(string host) => _offsets.Remove(host);

    /// <summary>The dock position the displacement is measured from, for the host being docked now.</summary>
    internal void RememberBase(string host, double left, double top)
    {
        _host = host;
        _baseLeft = left;
        _baseTop = top;
    }

    /// <summary>Records the drag that has just finished, from where the card has been left, for whichever host
    /// <see cref="RememberBase"/> last named.</summary>
    /// <remarks>
    /// ponytail: the displacement is only known once the drag ENDS, so a content refresh that re-sizes the card
    /// mid-drag re-anchors it and that part of the travel is lost (the drag then keeps following the cursor from
    /// there). Rare enough to leave: the empty state's opened-folders snapshot is requested once per window, and
    /// nothing else changes the card's size while the mouse is captured. The upgrade path is a drag-started
    /// signal from SearchBoxControl that makes the positioner stand down until the drag ends.
    /// </remarks>
    internal void RememberDrag()
    {
        var (left, top) = _position();
        _offsets[_host] = (left - _baseLeft, top - _baseTop);
    }

    /// <summary>Applies one host's displacement to a dock position, in physical pixels. A host the user has
    /// never moved is returned as the dock left it.</summary>
    internal (double Left, double Top) ApplyPhysical(
        string host,
        double left,
        double top,
        double dpiScaleX,
        double dpiScaleY,
        Rectangle workingArea,
        double windowWidth,
        double windowHeight) =>
        _offsets.TryGetValue(host, out var offset)
            ? Apply(left, top, offset.X, offset.Y, dpiScaleX, dpiScaleY, workingArea, windowWidth, windowHeight)
            : (left, top);

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
