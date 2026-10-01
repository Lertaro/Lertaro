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
/// must never be waited on from one. The queue runs under <see cref="_gate"/> on the calling thread and hands
/// the window work to the dispatcher, so a plugin's thread is never parked behind UI work. The screen half of
/// the job is <see cref="NotificationWindowManager"/>: this class decides what may be shown, in what order and
/// for how long, and never touches a window itself.
/// </remarks>
internal static class NotificationService
{
    private const int TickIntervalMs = 100;

    private static readonly NotificationWindowManager Windows = new(OnNotificationGone);

    private static readonly NotificationQueue Queue = new(
        () => FullscreenHelper.IsForegroundWindowFullScreen(),
        Present,
        TakeDown,
        message => Logger.Log(message, LogLevel.Warn));

    // Guards the queue and the window manager together, so one thread cannot be presenting a card while another
    // closes its neighbour.
    private static readonly object _gate = new();

    private static DispatcherTimer? _ticker;
    private static long _lastTick;
    private static bool _sessionLocked;
    private static bool _screenEventsBound;

    /// <summary>Accepts a request on behalf of the plugin that made it. Returns null only when there is no
    /// running launcher to draw on, which is how a plugin outside the launcher sees an absent host.</summary>
    internal static INotificationHandle? Show(NotificationRequest request, Assembly source)
    {
        if (Application.Current?.Dispatcher == null) return null;
        // Keyed by the same dll name the component registry uses, so "this plugin is now disabled" and "this
        // plugin's requests" are the same string and line up without a translation table.
        var pluginKey = System.IO.Path.GetFileName(
            string.IsNullOrEmpty(source.Location) ? source.GetName().Name + ".dll" : source.Location);
        lock (_gate)
        {
            return Queue.Submit(request, pluginKey, DescribeSource(source));
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
            // The queue first, because it ends the requests without letting a freed slot promote the next
            // waiting card while the process is on its way out. The sweep after it catches any window whose
            // teardown was handed over rather than run inline.
            Queue.Shutdown();
            Windows.CloseEverything();
            StopTicker();
        }
    }

    private static void Present(NotificationItem item) => OnOrOver(() =>
    {
        lock (_gate)
        {
            BindScreenEvents();
            EnsureTicker();
            Windows.Present(item);
        }
    });

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
            Queue.NotifyClosed(item);
            Windows.Restack(animated: Windows.Count > 0);
            if (Windows.Count == 0) StopTicker();
        }
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
        _ticker = new DispatcherTimer(DispatcherPriority.Background)
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
        // A locked session counts for nothing, so the reference point moves with it and the remaining time is
        // whatever it was when the screen went dark.
        if (_sessionLocked)
        {
            _lastTick = Stopwatch.GetTimestamp();
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var elapsedMs = (now - _lastTick) * 1000.0 / Stopwatch.Frequency;
        _lastTick = now;
        if (elapsedMs <= 0) return;

        lock (_gate)
        {
            foreach (var runner in Windows.Countdown())
            {
                // Hovering holds the time: a card the pointer is resting on is not being read yet.
                if (runner.Window.IsMouseOver) continue;

                runner.RemainingMs -= elapsedMs;
                if (runner.RemainingMs > 0) continue;

                Windows.Close(runner);
            }
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
        SystemEvents.DisplaySettingsChanged += (_, _) => OnDisplaySettingsChanged();
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

    private static void OnDisplaySettingsChanged()
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
