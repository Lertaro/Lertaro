using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Lertaro.App.Helpers.Visuals;

/// <summary>
/// App-wide smooth wheel scrolling: MODEL 3.0 (<see cref="Anim3"/>) for the timing, driven by the app-side
/// rules in <see cref="AppScrollModel"/>. Register once at startup via <see cref="EnableGlobally"/>.
/// </summary>
/// <remarks>
/// The split is automatic and app-wide, keyed off each viewer's existing scroll mode:
///   - Pixel scrolling gets the glide: plain ScrollViewers (settings pages, dialogs, ...) are
///     pixel-scrolling by default, and a ListBox opts in via <c>VirtualizingPanel.ScrollUnit="Pixel"</c>.
///   - Item-based scrolling is left alone: virtualized result lists and text boxes must not be eased,
///     since animating their offset fights the virtualizing panel and reintroduces the per-frame measure
///     cost that virtualization exists to remove.
///
/// Only the two senders that report a wheel are animated (see <see cref="WheelDevice"/>): a touchpad is a
/// continuous surface that is already smooth, so easing it a second time is what made it feel wrong.
///
/// TWO AXES, and the reason is measured (Apex common/core.h): the model reads ONE window length per Tick
/// call and divides every window's age by it, so a single axis whose length changed when a roll began would
/// re-spread the windows already in flight -- the rate collapsed 67% at frame 7 of a 25 ms roll (76% at
/// 100 ms, 90% at the 180 ms edge), which is a visible hitch. Giving each axis one length for its whole
/// life and routing each message by its own gap turns that step into 2-13% while the totals stay exact.
/// </remarks>
public static class SmoothWheelScrollBehavior
{
    /// <summary>The Glide setting: how long a LONE notch takes. A roll gets this plus the fixed release.</summary>
    public static double GlideMs { get; set; } = AppScrollModel.GlideMs;

    /// <summary>
    /// How many pixels one wheel DELTA travels. The travel is computed in deltas (that is what the budget
    /// counts), so this is the single conversion to the pixels a ScrollViewer scrolls by.
    /// </summary>
    public static double PixelsPerDelta { get; set; } = 1.0;

    /// <summary>
    /// How fast a touchpad scrolls relative to WPF's own step. 1.0 = what the stock app does, 0.5 = half.
    /// </summary>
    public static double TouchpadSpeed { get; set; } = 0.5;

    /// <summary>What one whole notch is worth in pixels, before <see cref="TouchpadSpeed"/>.</summary>
    private const double PixelsPerNotch = 48.0;

    
    /// <summary>
    /// The named event another program can look for while this behaviour is active, so a global wheel tool
    /// can keep its hands off this app's windows. Session-scoped ("Local\") on purpose: it is a marker for
    /// programs in the same session and needs no privileges. Created while enabled, closed when disabled.
    /// </summary>
    internal const string ActiveEventName = @"Local\Lertaro.SmoothScroll.Active";

    private static System.Threading.EventWaitHandle? _activeEvent;
    private static bool _enabled;
private static readonly ConditionalWeakTable<ScrollViewer, Glide> Glides = new();
    private static readonly System.Diagnostics.Stopwatch SinceStart = System.Diagnostics.Stopwatch.StartNew();
    private static bool _registered;

    private static double NowSeconds() => SinceStart.Elapsed.TotalSeconds;

    /// <summary>
    /// Registers the global wheel handler. Safe to call more than once (idempotent). Call it once from
    /// <c>App.OnStartup</c>, after the dispatcher exists.
    /// </summary>
    public static void EnableGlobally()
    {
        if (_registered) return;
        _registered = true;
        _enabled = true;
        try
        {
            // Held for the process lifetime: the event exists exactly as long as this handle stays open.
            _activeEvent ??= new System.Threading.EventWaitHandle(
                false, System.Threading.EventResetMode.ManualReset, ActiveEventName);
        }
        catch (Exception ex)
        {
            // The marker must never break scrolling: a linkage with another program is not worth a crash.
            Lertaro.Core.Logger.Log($"[SmoothWheelScroll] The active marker could not be created: {ex.Message}");
        }

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

        /// <summary>
    /// Stops smoothing and takes the marker down. Unused today -- this behaviour has no switch of its own --
    /// but it is what a future setting would call, and it keeps the marker honest either way.
    /// </summary>
    public static void DisableGlobally()
    {
        _enabled = false;
        _activeEvent?.Dispose();
        _activeEvent = null;
    }

private static void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_enabled || e.Delta == 0 || sender is not ScrollViewer scrollViewer)
            return;

        // Glide everything that scrolls by pixel, and leave item-based scrolling alone:
        //   - a plain ScrollViewer has CanContentScroll=false by default, so it glides automatically;
        //   - a ListBox that opts into pixel scrolling via ScrollUnit=Pixel also glides (read from the
        //     templated parent, since ScrollUnit lives on the ItemsControl, not its ScrollViewer);
        //   - item-based scrolling (virtualized result lists, text boxes) keeps its stock behaviour.
        var isItemScroll = scrollViewer.CanContentScroll
            && VirtualizingPanel.GetScrollUnit(scrollViewer.TemplatedParent ?? scrollViewer) != ScrollUnit.Pixel;
        if (isItemScroll || scrollViewer.ScrollableHeight <= 0)
            return;

        var device = WheelDevice.Classify(e.Delta);

        if (device is WheelDevice.Kind.Notched or WheelDevice.Kind.FreeSpin)
        {
            e.Handled = true;
            Glides.GetValue(scrollViewer, static sv => new Glide(sv)).FeedMessage(e.Delta);
            return;
        }

        // A touchpad (and "unknown") is NOT eased -- a continuous surface is already smooth and a second
        // easing layer on top is what makes it feel wrong -- but it IS scaled down: WPF's own handling of a
        // wheel message is a whole line step whatever the delta, and a touchpad sends many small deltas per
        // finger movement, so the stock path runs several times faster than the finger. One path only, for
        // EVERY touchpad message: scroll delta/120 of a notch immediately (no animation, so nothing is added
        // on top of the finger), times TouchpadSpeed.
        Glides.GetValue(scrollViewer, static sv => new Glide(sv)).ScrollTouchpad(e);
    }
    // One animator per ScrollViewer, kept alive for the host's lifetime via the weak table. The render
    // callback is unhooked once it settles, so steady state is zero per-frame work.
    private sealed class Glide
    {
        private readonly ScrollViewer _scrollViewer;
        private readonly System.Diagnostics.Stopwatch _clock = new();
        private readonly Anim3.Glide _lone = new();   // windows at the Glide length: a lone message
        private readonly Anim3.Glide _roll = new();   // windows at Glide + the fixed release: a message in a roll
        private readonly Anim3.Params _loneParams = new();
        private readonly Anim3.Params _rollParams = new();
        private readonly AppScrollModel.SpeedBudget _budget = new();
        private double _offset;
        private double _lastSeconds;
        private double _lastMessageSeconds = double.NegativeInfinity;
        private double _touchpadTail; // sub-pixel remainder on the touchpad path
        private bool _running;

        public Glide(ScrollViewer scrollViewer)
        {
            _scrollViewer = scrollViewer;
            // The CompositionTarget.Rendering subscription holds this Glide (and therefore the
            // ScrollViewer) alive as long as it is running; if the viewer is torn down mid-glide (the
            // user switches a tab), stop immediately so the static render loop does not keep a detached
            // visual alive and spin for nothing.
            scrollViewer.Unloaded += OnScrollViewerUnloaded;
        }

        private void OnScrollViewerUnloaded(object sender, RoutedEventArgs e) => Stop();

        /// <summary>
        /// The touchpad path: immediate, proportional, and scaled down. NOT eased -- nothing here animates, so
        /// what the finger does is what the view does, only by <see cref="TouchpadSpeed"/> of the stock step.
        /// The sub-pixel remainder is carried, so a long slow slide does not lose its fractions.
        /// </summary>
        public void ScrollTouchpad(MouseWheelEventArgs e)
        {
            e.Handled = true;
            _touchpadTail += e.Delta / 120.0 * PixelsPerNotch * TouchpadSpeed;
            var whole = Math.Truncate(_touchpadTail);
            if (whole == 0.0) return;

            _touchpadTail -= whole; // wheel-up (Delta > 0) scrolls up, i.e. a smaller offset
            var scrollable = _scrollViewer.ScrollableHeight;
            _scrollViewer.ScrollToVerticalOffset(
                Math.Clamp(_scrollViewer.VerticalOffset - whole, 0, scrollable));
        }


        /// <summary>One wheel message: decide how far it travels, then hand that amount to its axis.</summary>
        public void FeedMessage(int delta)
        {
            var now = NowSeconds();
            var gapMs = double.IsNegativeInfinity(_lastMessageSeconds) ? 0.0 : (now - _lastMessageSeconds) * 1000.0;
            _lastMessageSeconds = now;

            var magnitude = Math.Abs((double)delta);
            if (magnitude <= 0.0) return;

            // HOW FAR: the speed budget decides, and nothing is scaled after it.
            var budget = _budget.Add(magnitude, gapMs);
            var travel = AppScrollModel.Travel(magnitude, budget, AppScrollModel.RampUpDeltas,
                AppScrollModel.SlowStepDeltas, AppScrollModel.TopSpeedMul);

            // HOW LONG and WHAT SHAPE: the release model picks the axis, the app's ease rule shapes it.
            var baseWindow = AppScrollModel.BaseWindowMs(GlideMs);
            var rolling = AppScrollModel.IsRoll(gapMs, baseWindow);
            var window = rolling ? AppScrollModel.RollWindowMs(baseWindow) : baseWindow;
            var parameters = rolling ? _rollParams : _loneParams;
            parameters.WindowMs = window;
            parameters.PayoutEase = AppScrollModel.AppEaseFor(gapMs, window);

            // The sign comes from the message: wheel-up (Delta > 0) scrolls up, i.e. a SMALLER offset.
            // Wheel-up (Delta > 0) scrolls up, i.e. a SMALLER VerticalOffset, so the model's positive
            // travel (which means "down" on the wire, as in Apex) is NEGATED for WPF here.
            var signedTravel = delta > 0 ? -travel : travel;
            (rolling ? _roll : _lone).Feed(signedTravel * PixelsPerDelta, parameters);

            if (_running) return;
            _running = true;
            _offset = _scrollViewer.VerticalOffset;
            _clock.Restart();
            _lastSeconds = 0;
            CompositionTarget.Rendering += OnRendering;
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            var seconds = _clock.Elapsed.TotalSeconds;
            var dt = seconds - _lastSeconds;
            _lastSeconds = seconds;
            if (dt <= 0) return;

            // Each axis is ticked with the one length it has always had; nothing re-scales a running window.
            var baseWindow = AppScrollModel.BaseWindowMs(GlideMs);
            _loneParams.WindowMs = baseWindow;
            _loneParams.PayoutEase = 0.0;             // the ease is stored per window and applied inside Tick
            _rollParams.WindowMs = AppScrollModel.RollWindowMs(baseWindow);
            _rollParams.PayoutEase = 0.0;
            _offset += _lone.Tick(dt, _loneParams) + _roll.Tick(dt, _rollParams);

            var scrollable = _scrollViewer.ScrollableHeight;
            var clamped = Math.Clamp(_offset, 0, scrollable);

            // Hitting an edge absorbs the momentum into the wall rather than letting it build while the
            // offset can no longer move -- otherwise the next wheel-up after bottoming out would still be
            // fighting leftover downward speed.
            if (clamped != _offset)
            {
                _lone.Reset();
                _roll.Reset();
            }

            _offset = clamped;
            _scrollViewer.ScrollToVerticalOffset(_offset);

            if (!_lone.Active && !_roll.Active) Stop();
        }

        private void Stop()
        {
            if (!_running) return;
            _running = false;
            _clock.Stop();
            CompositionTarget.Rendering -= OnRendering;
            _scrollViewer.Unloaded -= OnScrollViewerUnloaded;
        }
    }
}
