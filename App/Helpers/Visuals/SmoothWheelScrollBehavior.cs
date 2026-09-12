using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Lertaro.App.Helpers.Visuals;

/// <summary>
/// App-wide smooth wheel scrolling. Registered once at startup via <see cref="EnableGlobally"/>; it
/// intercepts every wheel event that reaches a <see cref="ScrollViewer"/> and eases the scroll instead
/// of applying the stock instant per-notch jump.
///
/// The split is automatic and app-wide:
///   - Pixel scrolling gets the glide: plain ScrollViewers (settings pages, dialogs, ...) are
///     pixel-scrolling by default, and a ListBox opts in via VirtualizingPanel.ScrollUnit="Pixel".
///   - Item-based scrolling is left alone: virtualized result lists and text boxes must not be eased,
///     since animating their offset fights the virtualizing panel and reintroduces the per-frame
///     measure cost this codebase already removed there.
///
/// No per-list wiring is needed beyond that existing ScrollUnit flag -- a plain ScrollViewer glides with
/// zero changes.
/// </summary>
public static class SmoothWheelScrollBehavior
{
    // Velocity added per wheel notch (a notch is Delta == 120), in pixels per second. This is the
    // "start" knob: one tick's initial punch. Lower reads as a gentler first nudge, higher as a more
    // aggressive jump off the mark.
    private const double VelocityPerNotch = 520.0;

    // How fast the glide sheds speed, in decays per second (exponential friction). Lower friction lets
    // repeated ticks stack into a faster glide (the "accelerate" feel) and makes the coast-out after
    // you stop wheeling longer; higher friction is a shorter, more immediate stop.
    private const double Friction = 4.0;

    // The glide is finished once speed falls below this many pixels/second.
    private const double StopVelocity = 12.0;

    private static readonly ConditionalWeakTable<ScrollViewer, Glide> Glides = new();
    private static bool _registered;

    /// <summary>Registers the global wheel handler. Safe to call more than once (idempotent).</summary>
    public static void EnableGlobally()
    {
        if (_registered)
            return;
        _registered = true;

        // Hook the BUBBLING MouseWheelEvent, not PreviewMouseWheelEvent. A class handler on the tunnel
        // event ran before ScrollViewer's own default preview handling, and its mere presence was enough
        // to break the first-scroll routing on item-based lists (a freshly opened results list would not
        // respond to the wheel until the scrollbar had been touched once). The bubbling stage runs after
        // that default handling, so an untouched list keeps its stock behaviour.
        EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            UIElement.MouseWheelEvent,
            new MouseWheelEventHandler(OnMouseWheel),
            handledEventsToo: false);
    }

    private static void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta == 0 || sender is not ScrollViewer scrollViewer)
            return;

        // Glide everything that scrolls by pixel, and leave item-based scrolling alone:
        //   - a plain ScrollViewer (settings pages, dialogs, ...) has CanContentScroll=false by default,
        //     so it glides automatically -- no per-list wiring needed;
        //   - a ListBox that opts into pixel scrolling via ScrollUnit=Pixel also glides (read from the
        //     templated parent, since ScrollUnit lives on the ItemsControl, not its ScrollViewer);
        //   - item-based scrolling (virtualized result lists, text boxes) keeps its stock behaviour.
        var isItemScroll = scrollViewer.CanContentScroll
            && VirtualizingPanel.GetScrollUnit(scrollViewer.TemplatedParent ?? scrollViewer) != ScrollUnit.Pixel;
        if (isItemScroll || scrollViewer.ScrollableHeight <= 0)
            return;

        e.Handled = true;
        // Wheel-up (Delta > 0) scrolls up, i.e. a SMALLER VerticalOffset; the sign is inverted here.
        Glides.GetValue(scrollViewer, static sv => new Glide(sv)).AddVelocity(-e.Delta / 120.0 * VelocityPerNotch);
    }

    // One animator per ScrollViewer, kept alive for the host's lifetime via the weak table. It models
    // momentum rather than a fixed target: the wheel adds velocity, each frame advances the offset by
    // velocity*dt and friction bleeds velocity off, so a fast burst scrolls far and then coasts to a
    // stop -- the "inertial" feel. The render callback is unhooked once it settles, so steady state is
    // zero per-frame work.
    private sealed class Glide
    {
        private readonly ScrollViewer _scrollViewer;
        private readonly System.Diagnostics.Stopwatch _clock = new();
        private double _velocity;
        private double _offset;
        private double _lastSeconds;
        private bool _running;

        public Glide(ScrollViewer scrollViewer)
        {
            _scrollViewer = scrollViewer;
            // The CompositionTarget.Rendering subscription holds this Glide (and therefore the
            // ScrollViewer) alive as long as it is running; if the viewer is torn down mid-glide (the
            // user switches a settings tab), stop immediately so the static render loop does not keep a
            // detached visual alive and spin for nothing.
            scrollViewer.Unloaded += OnScrollViewerUnloaded;
        }

        private void OnScrollViewerUnloaded(object sender, RoutedEventArgs e) => Stop();

        public void AddVelocity(double deltaVelocity)
        {
            _velocity += deltaVelocity;

            if (!_running)
            {
                _running = true;
                _offset = _scrollViewer.VerticalOffset;
                _clock.Restart();
                _lastSeconds = 0;
                CompositionTarget.Rendering += OnRendering;
            }
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            var seconds = _clock.Elapsed.TotalSeconds;
            var dt = seconds - _lastSeconds;
            _lastSeconds = seconds;
            if (dt <= 0)
                return;

            // Frame-rate-independent integration: velocity is in px/s and dt is real seconds, so the
            // same flick glides the same distance on a 60Hz or 240Hz monitor.
            _offset += _velocity * dt;
            var scrollable = _scrollViewer.ScrollableHeight;
            var clamped = Math.Clamp(_offset, 0, scrollable);

            // Hitting an edge absorbs the momentum into the wall rather than letting it build while the
            // offset can no longer move -- otherwise the next wheel-up after bottoming out would still
            // be fighting leftover downward speed.
            if (clamped != _offset)
                _velocity = 0;
            _offset = clamped;
            _scrollViewer.ScrollToVerticalOffset(_offset);

            // Exponential friction, also frame-rate independent.
            _velocity *= Math.Exp(-Friction * dt);

            if (Math.Abs(_velocity) < StopVelocity)
                Stop();
        }

        private void Stop()
        {
            if (!_running)
                return;
            _running = false;
            _clock.Stop();
            CompositionTarget.Rendering -= OnRendering;
            _scrollViewer.Unloaded -= OnScrollViewerUnloaded;
        }
    }
}
