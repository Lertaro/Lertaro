using System;
using System.Windows;
using System.Windows.Threading;

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
    /// near the top of the screen, visibly and unpainted. Once a window has a real x, parking y alone is
    /// enough, and it is what <see cref="ShowThenPlace"/> does on purpose.</summary>
    internal static void Park(Window window)
    {
        window.Left = OffScreen;
        window.Top = OffScreen;
    }

    /// <summary>Shows a card one screen-height above where it belongs and runs <paramref name="place"/> once
    /// WPF has had a render pass at that size.</summary>
    /// <remarks>
    /// The gap this closes is the summon itself. An opaque window has no pixels until its render thread presents
    /// a frame, and the frame is not ready when Show returns: measured on a card-shaped tree, content reached
    /// the screen 170-258ms after a Show at the place it belongs, against 30ms after this order
    /// (scratch/blackprobe). The render cost is the same in both -- this only moves it where nobody can see it.
    ///
    /// The y-only park is deliberate and depends on the caller having placed the card at its target x first:
    /// a window that intersects no monitor takes the scale of the nearest one, and keeping the target's column
    /// is what makes that the monitor the card is about to be shown on. The move down into place then changes
    /// neither the size nor the DPI context, so it cannot recreate the surface while the user is looking at it.
    /// </remarks>
    internal static void ShowThenPlace(Window window, Action place)
    {
        window.Top = OffScreen;
        window.Show();
        AfterFrame(window, place);
    }

    /// <summary>Runs <paramref name="action"/> after WPF has had a render pass at the window's current size.</summary>
    /// <remarks>
    /// Two dispatcher passes rather than one, because the render queue is flushed at Render priority and Loaded
    /// is the first priority behind it; that pair is what the measurement was taken with. Exposed separately for
    /// the surfaces that already show themselves from the parking row and only need the landing pass deferred --
    /// the notification stack, whose placement is a stack-wide pass rather than one window's.
    /// </remarks>
    internal static void AfterFrame(Window window, Action action)
    {
        window.Dispatcher.BeginInvoke(
            new Action(() => window.Dispatcher.BeginInvoke(action, DispatcherPriority.Loaded)),
            DispatcherPriority.Render);
    }
}
