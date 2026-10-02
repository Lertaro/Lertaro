using System.Diagnostics;
using System.Reflection;
using System.Windows.Threading;
using Lertaro.App.Services.Theme;
using Lertaro.Core;
using Lertaro.Core.Hook;
using Lertaro.PluginSdk.Abstractions;
using Microsoft.Win32;
using Application = System.Windows.Application;

namespace Lertaro.App.Services.Notifications;

/// <summary>
/// The host's notification service: the queue rules, the countdown, and the identity of whoever asked. Nothing
/// here goes through the shell's notification pipeline, so no system setting, group policy or packaging model
/// can stop a notification from appearing.
/// </summary>
/// <remarks>
/// Threading is the one thing to get right: a plugin may call from any thread, and the launcher's UI thread
/// must never be waited on from one. <see cref="_gate"/> is the only lock in this feature and every entry point
/// takes it, including the handle a plugin dismisses through; the window work is then handed to the dispatcher,
/// so a plugin's thread is never parked behind UI work. The clock (<see cref="_ticker"/>, <see cref="_lastTick"/>)
/// is the exception: it is touched on the UI thread only, which is what lets a tick run without waiting behind
/// a presentation. The screen half of the job is <see cref="NotificationWindowManager"/>: this class decides
/// what may be shown, in what order and for how long, and never touches a window itself.
/// </remarks>
internal static class NotificationService
{
    private const int TickIntervalMs = 100;

    // Guards the queue and the window manager together, so one thread cannot be presenting a card while another
    // closes its neighbour. The queue is handed the same object rather than keeping its own: a plugin holds a
    // handle, and dismissing through it has to take the lock that submitting took. One lock, not two, because
    // the queue's show and hide callbacks re-enter this monitor on the submitting thread, and a second lock
    // would be taken in the opposite order by whichever thread queued the work.
    private static readonly object _gate = new();

    private static readonly NotificationQueue Queue = new(
        ForegroundIsFullScreen,
        Present,
        TakeDown,
        // The write belongs on a worker: it is a file write under Logger's own lock, and a plugin thread that
        // paid for it here would park every other Show call, and the UI thread's countdown tick, behind it.
        message => ThreadPool.QueueUserWorkItem(_ => Logger.Log(message, LogLevel.Warn)),
        _gate);

    private static readonly NotificationWindowManager Windows = new(OnNotificationGone, OnNotificationsGone);

    private static DispatcherTimer? _ticker;
    private static long _lastTick;
    private static bool _sessionLocked;
    private static bool _screenEventsBound;
    private static long _screenReadStamp;
    private static bool _screenIsFullScreen;

    /// <summary>Whether a fullscreen app owns the screen, held for one tick. The queue asks this at admission and
    /// again on every dequeue, from whichever thread is holding the gate, and each read is five P/Invokes into DWM
    /// about somebody else's window. A stale answer costs only that a game which has just gone fullscreen keeps
    /// real cards for up to a tick, which is the granularity the countdown already has.</summary>
    private static bool ForegroundIsFullScreen()
    {
        var now = Stopwatch.GetTimestamp();
        if ((now - _screenReadStamp) * 1000.0 / Stopwatch.Frequency < TickIntervalMs) return _screenIsFullScreen;
        _screenReadStamp = now;
        _screenIsFullScreen = FullscreenHelper.IsForegroundWindowFullScreen();
        return _screenIsFullScreen;
    }

    /// <summary>Accepts a request on behalf of the plugin that made it. Returns null only when there is no
    /// running launcher to draw on, which is how a plugin outside the launcher sees an absent host.</summary>
    internal static INotificationHandle? Show(NotificationRequest request, Assembly source)
    {
        if (Application.Current?.Dispatcher == null) return null;
        // Keyed by the same dll name the component registry uses, so "this plugin is now disabled" and "this
        // plugin's requests" are the same string and line up without a translation table.
        var pluginKey = System.IO.Path.GetFileName(
            string.IsNullOrEmpty(source.Location) ? source.GetName().Name + ".dll" : source.Location);
        // Named before the lock is taken: this walks the plugin list and may log, and the gate serialises every
        // plugin's Show call behind both of them.
        var sourceName = DescribeSource(source);
        lock (_gate)
        {
            return Queue.Submit(request, pluginKey, sourceName);
        }
    }

    /// <summary>Cancels a plugin's outstanding requests, for when its last enabled component goes off.</summary>
    internal static void CancelPlugin(string pluginKey)
    {
        lock (_gate) Queue.CancelPlugin(pluginKey);
    }

    /// <summary>Closes everything on screen and ends every outstanding request. The launcher is going away, so
    /// this waits for nothing.</summary>
    internal static void Shutdown()
    {
        lock (_gate)
        {
            UnbindScreenEvents();
            // The queue first, because it ends the requests without letting a freed slot promote the next
            // waiting card while the process is on its way out. The sweep after it catches any window whose
            // teardown was handed over rather than run inline.
            Queue.Shutdown();
            Windows.CloseEverything();
            StopTicker();
        }
    }

    private static void Present(NotificationItem item)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;
        // Always handed over, and at Background rather than Normal. A dump of the launcher mid-flood caught the UI
        // thread inside window.Show() -- which creates the HWND, applies the rounded-corner attribute and runs the
        // card's first layout -- in the very operation that had just started the surviving cards sliding. Those
        // frames are drawn at Render, which is above Background and below the countdown's own priority, so the
        // slide gets to finish before a window is ever built, and the build happens in its own operation instead
        // of eating the frames of the movement it was meant to make room for.
        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            lock (_gate)
            {
                BindScreenEvents();
                EnsureTicker();
                Windows.Present(item);
            }
        }));
    }

    private static void TakeDown(NotificationItem item) => OnOrOver(() =>
    {
        lock (_gate) Windows.TakeDown(item);
    });

    /// <summary>Called once a notification's window is really gone: frees its slot, which is what lets the queue
    /// hand it to the next waiting request, and re-lays out whatever is left.</summary>
    private static void OnNotificationGone(NotificationItem item)
    {
        lock (_gate)
        {
            // Slide first, then promote. A refill presents its card wherever the stack is going to end up, and
            // doing that before the slide starts leaves the survivors snapped into their new places with nothing
            // left to animate.
            Windows.Restack(animated: Windows.Count > 0);
            Queue.NotifyClosed(item);
            StopTickerIfIdle();
        }
    }

    /// <summary>Called once a batch of windows is really gone, which is the title bar's "mark all read": the
    /// queue promotes into the freed slots once instead of once per card, so five cards going means one slide
    /// rather than five jumps.</summary>
    private static void OnNotificationsGone(IReadOnlyList<NotificationItem> items)
    {
        if (items.Count == 0) return;
        lock (_gate)
        {
            Windows.Restack(animated: Windows.Count > 0);
            Queue.CloseBatch(items);
            StopTickerIfIdle();
        }
    }

    /// <summary>Stops the clock only when the screen is empty and nothing is waiting behind it. An empty screen
    /// no longer means an emptied queue: a promotion hands its presentation to the dispatcher, so the card the
    /// queue just accepted has no window yet, and stopping here would leave the rest of the queue with nothing to
    /// promote it -- requests whose tasks never finish, which is the one promise the API cannot break.</summary>
    private static void StopTickerIfIdle()
    {
        if (Windows.Count == 0 && !Queue.HasWaiting) StopTicker();
    }

    /// <summary>Runs the work on the UI thread, or hands it over. Never blocks the caller: a plugin thread must
    /// not wait for the dispatcher, which can be busy with a cross-process read of its own. Work that arrives
    /// from another thread takes the gate there, since the thread that queued it has let go by then.</summary>
    private static void OnOrOver(Action work)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;
        if (dispatcher.CheckAccess())
        {
            // Reached with the gate already held by whoever submitted the request.
            work();
            return;
        }
        dispatcher.BeginInvoke(new Action(() =>
        {
            lock (_gate) work();
        }));
    }

    /// <summary>The one countdown for everything on screen. Per-item timers would each need their own pause and
    /// freeze bookkeeping; one tick subtracting elapsed time handles hover, the session lock and a busy
    /// dispatcher the same way. The cost is that granularity is the tick, which is 100ms.</summary>
    private static void EnsureTicker()
    {
        if (_ticker != null) return;
        // Normal, not Background: this is a clock, and a background-priority clock does not run while anything
        // else is pending. Time keeps passing regardless, so a starved stretch comes back as one tick with a huge
        // delta and every overdue notification ends in the same frame -- a burst of windows vanishing at once.
        _ticker = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(TickIntervalMs)
        };
        _ticker.Tick += (_, _) => Tick();
        _ticker.Start();
        _lastTick = Stopwatch.GetTimestamp();
    }

    private static void StopTicker()
    {
        _ticker?.Stop();
        _ticker = null;
    }

    private static void Tick()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsedMs = (now - _lastTick) * 1000.0 / Stopwatch.Frequency;
        _lastTick = now;

        lock (_gate)
        {
            // A locked session counts for nothing, and the reference point already moved with it above, so the
            // remaining time is whatever it was when the screen went dark. That flag is written by the session
            // handler under this gate, which is why the check lives here and not out with the timestamp.
            if (_sessionLocked || elapsedMs <= 0) return;

            List<NotificationRunner>? doomed = null;
            foreach (var runner in Windows.Countdown())
            {
                // Hovering holds the time: a card the pointer is resting on is not being read yet. The cursor is
                // asked directly rather than through IsMouseOver, which only updates when a mouse message is
                // routed and is therefore false for a card that appeared under a pointer that never moved.
                if (NotificationPlacement.IsPointerOver(runner.Window)) continue;

                runner.RemainingMs -= elapsedMs;
                if (runner.RemainingMs > 0) continue;
                (doomed ??= []).Add(runner);
            }

            // One event for the whole group: cards given the same duration are due together, and closing them one
            // at a time re-laid out the stack once per close.
            if (doomed != null) Windows.CloseBatch(doomed);

            // One waiting card per tick, so a burst is fed a card at a time instead of in one frame. The queue
            // keeps no clock of its own; this is the clock, and it is already running whenever anything is on
            // screen with something still waiting behind it.
            Queue.Feed();
        }
    }

    /// <summary>Names the sender on the card. A plugin cannot choose this: it comes from the assembly the SDK
    /// facade saw at the call site, which is the one attribution a plugin cannot forge.</summary>
    private static string DescribeSource(Assembly source)
    {
        var name = source.GetName().Name ?? string.Empty;
        if (name.StartsWith("Lertaro.App", StringComparison.OrdinalIgnoreCase)) return "Lertaro";

        try
        {
            var plugin = Plugin.PluginManager.Instance.Plugins
                .FirstOrDefault(candidate => candidate.GetType().Assembly == source);
            if (plugin != null) return plugin.Name;
        }
        catch (Exception ex)
        {
            // The manager may not be built yet on an early call; the assembly name is a usable label.
            Logger.Log($"[Notifications] source lookup failed for {name}: {ex.Message}", LogLevel.Debug);
        }

        return name.StartsWith("Lertaro.Plugins.", StringComparison.OrdinalIgnoreCase)
            ? name["Lertaro.Plugins.".Length..]
            : name;
    }

    private static void BindScreenEvents()
    {
        if (_screenEventsBound) return;
        _screenEventsBound = true;
        // Subscribed on the UI thread on purpose: SystemEvents needs a message pump on whichever thread
        // registers, and Present can be reached from any plugin thread.
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    /// <summary>SystemEvents outlives the window that registered for it, and a handler left attached past
    /// shutdown is the shape that faults when its own message window is torn down first. The notifications are
    /// already gone by then, so there is nothing left for either handler to act on.</summary>
    private static void UnbindScreenEvents()
    {
        if (!_screenEventsBound) return;
        _screenEventsBound = false;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
    }

    private static void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason != SessionSwitchReason.SessionLock && e.Reason != SessionSwitchReason.SessionUnlock) return;
        var locked = e.Reason == SessionSwitchReason.SessionLock;
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            lock (_gate)
            {
                _sessionLocked = locked;
                Windows.HideForSession(locked);
                // Logged at the default level because whether this fires at all is the only way to tell a frozen
                // countdown from a merely fast one after the fact.
                Logger.Log($"[Notifications] session {(locked ? "locked" : "unlocked")}: " +
                           $"{Windows.Count} notification(s) {(locked ? "hidden and their countdowns frozen" : "shown again, counting down from where they stopped")}.",
                    LogLevel.Info);
            }
        }));
    }

    private static void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            lock (_gate)
            {
                // A screen that goes away takes the card's chosen corner with it, so the drag is dropped and
                // everything is re-anchored to what is left. Countdowns are untouched.
                Windows.Reanchor();
            }
        }));
    }
}
