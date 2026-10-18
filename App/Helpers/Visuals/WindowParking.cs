using System.Windows;

namespace Lertaro.App.Helpers.Visuals;

/// <summary>
/// Where this app puts a window it never wants looked at, and how it tells that state apart from a window
/// that is actually being shown.
/// </summary>
/// <remarks>
/// A card is parked by coordinate rather than by hiding it: hiding costs the next summon a full re-render of
/// its visual tree, measured at 227-258ms for a card with the inline panel's content against 23-26ms for
/// moving an already-visible one (scratch/blackprobe). Parking is therefore the dismissal, and a move out of
/// the parking spot is the summon.
/// </remarks>
internal static class WindowParking
{
    /// <summary>-32000 rather than merely off the edge of a screen: it is where Windows parks windows it has
    /// disabled, and the value window enumerators treat as "not a candidate". A hidden window still answers
    /// GetWindowRect with the rectangle it last occupied, and PixPin's element picker drew its selection box
    /// from exactly that.</summary>
    internal const double OffScreen = -32000;

    /// <summary>Parks both coordinates, never one alone. A window whose Left is still unset is created with
    /// CW_USEDEFAULT for x, and Windows then ignores y as well -- which puts the window at the cascade position
    /// near the top of the screen, visibly and unpainted.</summary>
    internal static void Park(Window window)
    {
        window.Left = OffScreen;
        window.Top = OffScreen;
    }

    /// <summary>Whether a window sits in its parking spot, which is this app's "not on screen" for a card that
    /// is kept visible between summons. Read from the position rather than remembered in a flag, so a summon or
    /// a dismissal that takes a different path cannot leave the answer out of step with the window.</summary>
    internal static bool IsParked(Window window) =>
        window.Left <= OffScreen || window.Top <= OffScreen;
}
