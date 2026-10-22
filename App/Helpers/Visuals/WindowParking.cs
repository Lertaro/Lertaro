using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Lertaro.Core;

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
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

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
    /// (scratch/blackprobe). What the parked interval buys is the window's cold layout and drawing data; the
    /// present itself is gated on being on screen, so the gap does not go to zero by waiting longer -- holds of
    /// 0, 30, 60, 100, 150, 200 and 260ms all landed 22-31ms before their first frame. This turns a visible
    /// gap of about ten frames into one or two, which is the whole of what it does.
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
        AfterFrame(window, () =>
        {
            place();
            WatchFirstFrame(window);
        });
    }

    /// <summary>Logs how long the window stays on screen with nothing in it -- the black rectangle, in ms.</summary>
    /// <remarks>
    /// Subscribe after the window has been placed, not before: ContentRendered is never raised while a window is
    /// in the parking row (measured: 32 of 32 shows parked at -32000 timed out), so from here its delay is the
    /// gap the user sees, and on a machine whose compositor fills an unpainted client area with the window
    /// class's black brush, that number is the complaint. Nothing else in this repo can measure it: the
    /// compositor on the development machine shows the desktop through instead, so a gap and a park look alike.
    /// </remarks>
    private static void WatchFirstFrame(Window window)
    {
        var placedAt = Environment.TickCount64;
        void OnContentRendered(object? sender, EventArgs e)
        {
            window.ContentRendered -= OnContentRendered;
            Logger.Log($"[WindowPark] {window.GetType().Name} first frame "
                + $"{Environment.TickCount64 - placedAt}ms after it was placed", LogLevel.Info);
        }
        window.ContentRendered += OnContentRendered;
    }

    /// <summary>Runs <paramref name="action"/> after WPF has had a render pass at the window's current size.</summary>
    /// <remarks>
    /// Two dispatcher passes rather than one, because the render queue is flushed at Render priority and Loaded
    /// is the first priority behind it; that pair is what the measurement was taken with. Exposed separately for
    /// the surfaces that already show themselves from the parking row and only need the landing pass deferred --
    /// the notification stack, whose placement is a stack-wide pass rather than one window's.
    /// </remarks>
    internal static void AfterFrame(Window window, Action action) => window.Dispatcher.BeginInvoke(
            new Action(() => window.Dispatcher.BeginInvoke(action, DispatcherPriority.Loaded)),
            DispatcherPriority.Render);

    /// <summary>Lifts a window WPF positions by itself into the parking row for the length of its first render
    /// pass, and drops it back where it belongs once that pass is through. Call before Show or ShowDialog.</summary>
    /// <remarks>
    /// The other order is for the cards, which are placed by the app; a CenterOwner dialog has no caller-side
    /// position to keep, and a dialog cannot be placed from its caller at all because ShowDialog does not return
    /// until it is answered. So the parking is done from SourceInitialized, where WPF has already worked the
    /// startup location out (measured: a CenterOwner dialog reports its centered 542x353 inside that handler,
    /// while before Show it is NaN), and the restore is queued from here so the modal loop is what runs it.
    ///
    /// Only the vertical coordinate moves, for the same reason ShowThenPlace parks on y alone: the x keeps the
    /// window in the monitor column whose scale it was placed for.
    ///
    /// <paramref name="place"/> is for the caller that does have a position in mind but cannot reach it before
    /// Show, because the handle -- and so any placement that has to be in physical pixels -- does not exist yet:
    /// SourceInitialized is the first moment it does, and nothing has been painted. Running it here, ahead of the
    /// capture, means the rect restored below is the placed one rather than the one WPF chose.
    /// </remarks>
    internal static void ParkUntilPainted(Window window, Action<Window>? place = null)
    {
        // A window that already has a handle has been shown once, will not raise SourceInitialized again, and
        // has a warm visual tree to show -- this is a first-show gate, and checking here is what stops a
        // caller re-showing a cached window from stacking a handler that can never fire.
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero) return;

        void OnSourceInitialized(object? sender, EventArgs e)
        {
            window.SourceInitialized -= OnSourceInitialized;
            place?.Invoke(window);

            var hwnd = new WindowInteropHelper(window).Handle;
            GetWindowRect(hwnd, out var resting);
            window.Top = OffScreen;
            var shownAt = Environment.TickCount64;
            AfterFrame(window, () =>
            {
                // Info, and it says whether the park held: an owned dialog is placed by WPF at show time, so
                // if anything re-applies that placement behind our back, this is the only place that can tell.
                // The two look identical on a machine whose compositor shows the desktop through an unpainted
                // window instead of the class background, which is why this is not left to be inferred.
                var held = window.Top <= OffScreen;
                // Restored in physical pixels rather than by writing the captured DIP Top back: that number was
                // read at the scale the window was created with, and by now the window has been resized to its
                // monitor's own scale, so the same DIP means a different place. See WindowCentering for what that
                // measures as on a mixed-DPI desktop.
                SetWindowPos(hwnd, IntPtr.Zero, resting.Left, resting.Top, 0, 0,
                    SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                var restoredAt = Environment.TickCount64;
                Logger.Log($"[WindowPark] {window.GetType().Name} restored after "
                    + $"{restoredAt - shownAt}ms, parkHeld={held}", LogLevel.Info);
                WatchFirstFrame(window);
            });
        }

        window.SourceInitialized += OnSourceInitialized;
    }
}
