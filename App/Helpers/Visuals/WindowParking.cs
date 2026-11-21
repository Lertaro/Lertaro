using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Lertaro.App.Views.InlineSearchWindow.Helpers;
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
        // Parked is dismissed, and a parked window stays a visible top-level window at -32000: without
        // WS_EX_TOOLWINDOW the shell lists it in Alt+Tab as an untitled entry carrying the process name.
        // ShowInTaskbar=false does not do that, because WPF answers it by pointing the owner at a hidden
        // window and the shell still lists a visible window whose owner is hidden. The bit belongs here
        // rather than at each call site: every window this app parks is one the user has put away, and the
        // anchors that keep the tray and action menus alive are parked exactly like the cards are.
        AltTabExcluder.Attach(window);
        window.Left = OffScreen;
        window.Top = OffScreen;
    }

    /// <summary>Lands a shown window's top-left on a physical screen point, in that point's own monitor
    /// scale.</summary>
    /// <remarks>
    /// Both passes are needed and neither is enough alone: the DIP write is converted with the SYSTEM scale for
    /// a window that has never been shown, which <see cref="WindowCentering"/> measured landing a window 641px
    /// off on a mixed-DPI desktop, and the physical write alone leaves WPF's own Left/Top stale for anything it
    /// later lays out against. Takes a physical point rather than reading one because the caller is the one who
    /// knows where its surface is going.
    /// </remarks>
    internal static void PlaceAtPoint(Window window, int physicalX, int physicalY)
    {
        var (scaleX, scaleY) = InlineCardSpace.DpiScaleFor(
            InlineCardSpace.MonitorForPoint(new System.Drawing.Point(physicalX, physicalY)));
        window.Left = physicalX / scaleX;
        window.Top = physicalY / scaleY;

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, IntPtr.Zero, physicalX, physicalY, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    /// <summary>
    /// Shows a card one screen-height above its target and places it after a render pass. Set its target X
    /// first: parking only Y preserves the nearest monitor and DPI. Moving into place must not resize or
    /// recreate the surface.
    ///
    /// Measured first-frame delay fell from 170–258 ms to about 30 ms. Parking prepares layout/drawing
    /// data, but presentation still requires an on-screen window; longer waits did not remove the
    /// remaining 22–31 ms.
    /// </summary>
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

    /// <summary>
    /// Parks a WPF-positioned window above the screen during its first render, then restores it. Call
    /// before Show or ShowDialog. SourceInitialized captures WPF's resolved startup position; queue
    /// restoration so the modal dispatcher can run it.
    ///
    /// Move only Y to retain the target monitor's DPI. If supplied, place runs after HWND creation but
    /// before capturing the rectangle, allowing physical-pixel placement.
    /// </summary>
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
