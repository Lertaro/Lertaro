using Lertaro.Core;

namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

/// <summary>
/// A drag the user performed on the inline card, kept as a displacement from the position the card docks to,
/// one displacement per host.
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
/// for this run of the process, and a dialog shares that application's process name with its plain windows
/// without sharing where the card docks, so <see cref="HostKeyFor"/> splits both of those out.
///
/// Split out purely to keep InlineSearchWindowPositioner under the repo's per-file line limit. Takes the
/// window's position as a function rather than the window so the keying is testable without a WPF window.
/// </remarks>
internal sealed class InlineCardDragOffset
{
    /// <summary>The bucket the desktop's displacement lives in, kept apart from explorer.exe's.</summary>
    internal const string DesktopHost = "desktop";

    /// <summary>The suffix that keeps a process's dialogs out of its plain windows' bucket.</summary>
    private const string DialogSuffix = "|dialog";

    private readonly Func<(double Left, double Top)> _position;
    private readonly Dictionary<string, (double X, double Y)> _offsets = new(StringComparer.OrdinalIgnoreCase);
    private string _host = string.Empty;
    private double _baseLeft;
    private double _baseTop;

    // The dock frozen when the current drag began, so the drag's own travel is measured against what the user
    // actually moved the card away from. Null between a drag ending and the next one starting.
    private (string Host, double Left, double Top)? _drag;

    internal InlineCardDragOffset(Func<(double Left, double Top)> position) => _position = position;

    /// <summary>Which bucket the displacement of a dock belongs to.</summary>
    /// <remarks>
    /// A dialog gets its own because the card docks to a different edge there than in the same process's plain
    /// windows -- the dialog's own file list or target field against a window's outer edge -- and a displacement
    /// only means anything added to the edge it was measured from. Recorded over a save dialog and replayed on a
    /// folder window, the card lands off by exactly the difference between those two anchors. The desktop was
    /// split out of explorer.exe for the same reason long ago; the dialog was the case left sharing a bucket.
    /// </remarks>
    internal static string HostKeyFor(string processName, bool isDesktop, bool isDialog) =>
        isDesktop ? DesktopHost : processName + (isDialog ? DialogSuffix : string.Empty);

    /// <summary>Whether the user has moved the card on this host at all.</summary>
    internal bool IsSetFor(string host) => _offsets.ContainsKey(host);

    /// <summary>Drops one host's displacement, so its next placement puts the card back where it docks.</summary>
    /// <remarks>
    /// Logs the miss as well as the hit: the reset gesture reports nothing at all when it clears a bucket nobody
    /// wrote to, which from outside the process is indistinguishable from a reset that did not happen.
    /// </remarks>
    internal void ClearFor(string host)
    {
        if (_offsets.Remove(host))
            Logger.Log($"[InlineCardDrag] Reset '{host}': its displacement is dropped.", LogLevel.Debug);
        else
            Logger.Log($"[InlineCardDrag] Reset '{host}': no displacement stored under this key.", LogLevel.Debug);
    }

    /// <summary>The dock position the displacement is measured from, for the host being docked now.</summary>
    internal void RememberBase(string host, double left, double top)
    {
        _host = host;
        _baseLeft = left;
        _baseTop = top;
    }

    /// <summary>Freezes which dock the user is dragging away from, the moment a real drag starts.</summary>
    internal void BeginDrag() => _drag = (_host, _baseLeft, _baseTop);

    /// <summary>Records the drag that has just finished, against the dock <see cref="BeginDrag"/> froze.</summary>
    /// <remarks>
    /// Frozen at the start rather than read at the end, because a placement pass runs for whichever window the
    /// tracker is on, not for the one under the cursor: a dialog opening, closing or being re-measured mid-drag
    /// re-labels the host and re-anchors the base, and the whole travel then lands in the wrong bucket -- which
    /// is what made a drag performed over one application's dialog move that application's folder window, and
    /// keep doing it for the rest of the run since nothing but a restart clears the table.
    ///
    /// ponytail: a drag that never announced its start records nothing rather than guessing a base. What is
    /// still not handled is the card visibly jumping when a placement pass runs while the mouse is captured;
    /// the upgrade path is for the positioner to stand down between BeginDrag and RememberDrag.
    /// </remarks>
    internal void RememberDrag()
    {
        if (_drag is null)
        {
            Logger.Log("[InlineCardDrag] Drag ended without a start; nothing recorded.", LogLevel.Debug);
            return;
        }

        var (host, baseLeft, baseTop) = _drag.Value;
        _drag = null;

        var (left, top) = _position();
        _offsets[host] = (left - baseLeft, top - baseTop);
        Logger.Log(
            $"[InlineCardDrag] '{host}': dock {baseLeft:F0},{baseTop:F0} -> left at {left:F0},{top:F0} "
            + $"= {(left - baseLeft):F0},{(top - baseTop):F0}.",
            LogLevel.Debug);
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
