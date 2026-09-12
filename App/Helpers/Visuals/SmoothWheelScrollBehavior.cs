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
    // How far one wheel notch scrolls while the wheel is NOT being spun, in pixels -- the precision knob;
    // smaller means finer stepping.
    internal const double NotchPixels = 15.0;

    // How long that lone notch takes to coast to rest, in milliseconds. This doubles as the friction knob:
    // the two are locked together once NotchPixels is fixed (see DerivePunchAndFriction), so raising this
    // is exactly what "lower the friction" means here -- it lengthens the coast, i.e. how much glide a
    // flick carries before dying, and lowers the punch with it, so the start is gentler rather than a rush
    // that then dies. A spin's own stopping is governed separately by StopFriction.
    internal const double ReleaseMilliseconds = 250.0;

    // The glide is finished once speed falls below this many pixels/second.
    internal const double StopVelocity = 12.0;

    // Punch (px/s) and friction (decays/s) for a lone, deliberate notch, solved together from the two
    // knobs above -- its travel and release time jointly fix both, so neither is independent (see
    // DerivePunchAndFriction). That leaves a deliberately small punch: a glide's speed stabilizes at
    // roughly punch / friction, and the feedback below (not this punch) is what lets a fast spin build
    // momentum, so this stays small on purpose to keep the lone step precise and the start unhurried.
    private static readonly (double Punch, double Friction) LoneNotch =
        DerivePunchAndFriction(NotchPixels, ReleaseMilliseconds);

    internal static double VelocityPerNotch => LoneNotch.Punch;

    // Friction (decays/s) for a lone notch: the value that lets its punch cover exactly NotchPixels in
    // exactly ReleaseMilliseconds.
    internal static double Friction => LoneNotch.Friction;

    // How much each notch's punch grows with the speed already built up: punch = lone punch + this *
    // |velocity|. This is the acceleration knob, and it is a feedback rather than a threshold on purpose.
    // A threshold (e.g. "from the second notch, use a big punch") made the speed jump ~12x between the
    // first and second notch -- felt as "slow at first, then suddenly flying". Feedback instead ramps
    // smoothly, and self-regulates: a deliberate lone notch leaves almost no velocity, so the next one
    // still gets the precise lone punch, while a fast spin compounds smoothly toward the cap. Set by
    // simulating the per-notch speed -- at 3.0, notches 100 ms apart climb 132, 291, 483, 715, 994, 1332,
    // ... (even ~1.2x steps) and 80 ms apart reach the cap in about 8 notches. Lower is gentler (1.5 would
    // decay to a ~1100 px/s plateau), higher is more eager.
    internal const double AccelerationGain = 3.0;

    // Friction (decays/s) applied once a spin has gone quiet, to shed the stacked speed promptly instead of
    // coasting on it: from the speed cap this settles in about 340 ms. Kept well above the lone-notch
    // friction on purpose -- the lone-notch value is the gentle coast a single notch should have, not what
    // should let a released spin drift on.
    internal const double StopFriction = 25.0;

    // A notch arriving within this many milliseconds of the previous one counts as the same spin. Must sit
    // above a real wheel's inter-notch gap (roughly 60-120 ms when spinning), or the friction would flip to
    // the stopping value mid-spin and damp out the very acceleration being built. It only gates that
    // friction switch and the notch count -- the punch is a feedback and needs no window. A lone notch
    // keeps the precise behaviour however long the pause.
    internal const double SpinWindowMilliseconds = 150.0;

    // Ceiling on the accumulated speed, in pixels/second, applied with the sign kept. A lone notch is far
    // below it; a spin approaches it and is then held, which is what stops a hyper-scrolling wheel from
    // building an unbounded coast. Chosen as 2x the previous build's per-notch punch (3400). Not a const
    // only so the test pinning that value is not a compile-time tautology.
    internal static readonly double MaxVelocity = 2 * 3400.0;

    /// <summary>
    /// The velocity one notch feeds in at the current speed: the precise lone-notch punch, plus a share of
    /// whatever speed is already there (see <see cref="AccelerationGain"/>). At rest this is exactly the
    /// lone punch, which is what keeps a single deliberate notch at NotchPixels.
    /// </summary>
    internal static double SelectPunch(double currentVelocity) =>
        VelocityPerNotch + AccelerationGain * Math.Abs(currentVelocity);

    // Acceleration is useful only when the new notch continues the current direction; a reversal should
    // bleed the old velocity away at the lone-notch rate instead of turning it into a reverse fling.
    internal static double SelectPunchForDirection(double currentVelocity, double notchDirection) =>
        currentVelocity != 0 && Math.Sign(currentVelocity) != Math.Sign(notchDirection)
            ? SelectPunch(0)
            : SelectPunch(currentVelocity);

    // MouseWheel Delta can contain multiple 120-unit notches in one event, so count the whole batch when
    // deciding whether the glide is a spin that should use stop friction.
    internal static int AccumulateNotchCount(int previousCount, double millisecondsSinceLastNotch, double notches)
    {
        var batchCount = Math.Max(1, (int)Math.Ceiling(Math.Abs(notches)));
        return (millisecondsSinceLastNotch <= SpinWindowMilliseconds ? Math.Max(0, previousCount) : 0) + batchCount;
    }

    /// <summary>
    /// The friction to integrate with right now: the stopping value once a spin has gone quiet, else the
    /// precise lone-notch value.
    /// </summary>
    /// <remarks>
    /// A lone notch (count 1) always keeps the lone-notch friction, however long its glide takes -- that is
    /// what makes its exact NotchPixels / ReleaseMilliseconds behaviour hold, so it must not switch to the
    /// stopping value mid-glide. Only a real spin (two or more notches) switches once notches stop arriving,
    /// which is what sheds its stacked speed promptly instead of carrying it.
    /// </remarks>
    internal static double SelectFriction(int notchCount, double millisecondsSinceLastNotch)
    {
        if (notchCount < 2) return Friction;
        return millisecondsSinceLastNotch <= SpinWindowMilliseconds ? Friction : StopFriction;
    }

    /// <summary>
    /// Solves for the punch and friction that make one notch travel exactly <paramref name="notchPixels"/>
    /// and coast to rest in exactly <paramref name="releaseMilliseconds"/>.
    /// </summary>
    /// <remarks>
    /// They have to be solved together: with v(t) = punch * e^(-friction*t), travel is
    /// (punch - StopVelocity)/friction while the tail is ln(punch/StopVelocity)/friction, so raising
    /// friction to shorten the tail also shortens the travel. Distance gives
    /// punch = StopVelocity + notchPixels * friction; substituting that into the tail equation leaves
    /// friction * T = ln(1 + notchPixels * friction / StopVelocity), which has no closed form, so it is
    /// solved by fixed-point iteration on u = notchPixels * friction / StopVelocity. That converges
    /// quickly because its step factor, notchPixels/(StopVelocity*T)/(1+u), stays below 1.
    /// </remarks>
    internal static (double Punch, double Friction) DerivePunchAndFriction(double notchPixels, double releaseMilliseconds)
    {
        var seconds = releaseMilliseconds / 1000.0;
        var k = notchPixels / (StopVelocity * seconds);
        var u = k;
        for (var i = 0; i < 64; i++)
            u = k * Math.Log(1 + u);

        var friction = u * StopVelocity / notchPixels;
        var punch = StopVelocity + notchPixels * friction;
        return (punch, friction);
    }

    /// <summary>Caps a glide velocity without changing its sign.</summary>
    internal static double ClampVelocity(double velocity) => Math.Clamp(velocity, -MaxVelocity, MaxVelocity);

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
        // Wheel-up (Delta > 0) scrolls up, i.e. a SMALLER VerticalOffset; the sign is inverted here. The
        // notch count (not a velocity) is handed to the Glide, because how much speed a notch is worth
        // depends on whether it is part of a spin -- see SelectPunch.
        Glides.GetValue(scrollViewer, static sv => new Glide(sv)).AddNotches(-e.Delta / 120.0);
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
        // Notch pacing, timed off the wall clock rather than _clock: the gap between notches has to keep
        // counting across glides (a spin can end and a fresh lone notch arrive later), and _clock only
        // runs while a glide is in flight.
        private long _lastNotchTicks;
        private int _notchCount;

        public Glide(ScrollViewer scrollViewer) => _scrollViewer = scrollViewer;

        private void OnScrollViewerUnloaded(object sender, RoutedEventArgs e) => Stop();

        public void AddNotches(double notches)
        {
            // Counted for the friction decision (see SelectFriction) -- the punch itself no longer depends
            // on it, since the feedback in SelectPunch already handles acceleration smoothly.
            var now = Environment.TickCount64;
            _notchCount = AccumulateNotchCount(_notchCount, now - _lastNotchTicks, notches);
            _lastNotchTicks = now;

            // Punch scales with the speed already built, so a spin ramps smoothly instead of jumping: see
            // SelectPunch. Capped as it stacks: see MaxVelocity. A lone notch from rest gets exactly the
            // lone punch and so stays at the precise NotchPixels.
            _velocity = ClampVelocity(_velocity + notches * SelectPunchForDirection(_velocity, notches));

            if (!_running)
            {
                // Reattach for every glide: Stop detaches this handler, and the same viewer can be
                // unloaded and loaded again before the user scrolls it next time.
                _scrollViewer.Unloaded += OnScrollViewerUnloaded;
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

            // Exponential friction, also frame-rate independent. Which friction: the low spinning value
            // while notches are still arriving, else the precise lone-notch value -- see SelectFriction
            // for why the switch (and the idle check that ends a spin) is what lets a fast spin build up
            // without making a released wheel coast.
            var millisecondsSinceNotch = Environment.TickCount64 - _lastNotchTicks;
            _velocity *= Math.Exp(-SelectFriction(_notchCount, millisecondsSinceNotch) * dt);

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
