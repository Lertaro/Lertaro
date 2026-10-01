using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Lertaro.App.Views.Notifications;
using Lertaro.Core;
using Lertaro.Core.Hook;
using Lertaro.PluginSdk.Abstractions;
using Microsoft.Win32;
using Application = System.Windows.Application;

namespace Lertaro.App.Services.Notifications;

/// <summary>
/// The host's notification service: everything the queue decides gets a window, a countdown and a place
/// on the screen here. Nothing here goes through the shell's notification pipeline, so no system setting,
/// group policy or packaging model can stop a notification from appearing.
/// </summary>
/// <remarks>
/// Threading is the one thing to get right: a plugin may call from any thread, and the launcher's UI
/// thread must never be waited on from one. The queue runs under <see cref="_gate"/> on the calling thread
/// and hands the window work to the dispatcher, so a plugin's thread is never parked behind UI work.
/// </remarks>
internal static class NotificationService
{
    internal const double CardWidthDip = 360;
    internal const double CardMaxHeightDip = 260;
    internal const double EdgeMarginDip = 12;
    internal const double CardGapDip = 8;
    internal const double NoticeHeightDip = 36;
    internal const double NoticeBottomGapDip = 16;

    // Fixed, deliberately: the stack shifting down to make room is not something the user asked to tune.
    private const int RestackAnimationMs = 200;
    private const int TickIntervalMs = 100;

    private static readonly NotificationQueue Queue = new(
        () => FullscreenHelper.IsForegroundWindowFullScreen(),
        Present,
        Discard,
        message => Logger.Log(message, LogLevel.Warn));

    private static readonly Dictionary<NotificationItem, Runner> _runners = [];

    // Guards the queue and the runner table together, so one thread cannot be presenting a card while
    // another closes its neighbour.
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
        // Keyed by the same dll name the component registry uses, so "this plugin is now disabled" and
        // "this plugin's requests" are the same string and line up without a translation table.
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

    /// <summary>Closes everything on screen and ends every outstanding request. Waits for no answer and for
    /// no fade: the launcher is going away.</summary>
    internal static void Shutdown()
    {
        lock (_gate)
        {
            foreach (var runner in _runners.Values.ToArray()) CloseNow(runner);
            Queue.Shutdown();
            StopTicker();
        }
    }

    private static void Present(NotificationItem item) => OnOrOver(() => ShowWindow(item));

    /// <param name="fade">Whether it is going away because someone asked it to end, which fades it out like
    /// a time-out does, or because it was replaced or cancelled, which takes it down right now.</param>
    private static void Discard(NotificationItem item, bool fade) => OnOrOver(() =>
    {
        if (!_runners.TryGetValue(item, out var runner)) return;
        if (fade && !runner.IsClosing)
        {
            runner.IsClosing = true;
            FadeTo(runner.Window, 0.0, FadeSeconds(), () => Finish(runner));
            return;
        }

        CloseNow(runner);
        // Freeing the slot is what lets the queue hand it to the next waiting request.
        Queue.NotifyClosed(item);
        Restack(animated: true);
    });

    /// <summary>Runs the work on the UI thread, or hands it over. Never blocks the caller: a plugin thread
    /// must not wait for the dispatcher, which can be busy with a cross-process read of its own. Work that
    /// arrives from another thread takes the gate there, since the thread that queued it has let go by then.</summary>
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

    private static void ShowWindow(NotificationItem item)
    {
        // The submitting thread handed this over and may have dismissed, replaced or cancelled the item
        // since. Painting it then would leave a window that is in no list, nobody times out, and nobody
        // ever closes.
        if (item.IsSettled) return;

        BindScreenEvents();
        EnsureTicker();

        var window = item.EffectivePosition == NotificationPosition.CardStack
            ? CreateCard(item)
            : (Window)CreateNotice(item);

        _runners[item] = new Runner(item, window);
        window.Opacity = 0;
        // ShowActivated=False on both kinds: a notification that takes the foreground is worse than one
        // that never arrived, for anyone typing.
        window.Show();
        // ActualHeight is only known after the first layout, and the stack is measured in it, so the
        // placement runs twice: once to get the window roughly on screen, once when it knows its own size.
        Restack(animated: false);
        window.ContentRendered += (_, _) =>
        {
            lock (_gate) Restack(animated: false);
        };
        FadeTo(window, 1.0, FadeSeconds(), onDone: null);
    }

    private static NotificationCardWindow CreateCard(NotificationItem item)
    {
        var window = new NotificationCardWindow();
        window.SetContent(item.Request, item.SourceName);
        window.DismissRequested += () => CloseByUser(item);
        window.ReadAllRequested += DismissAllCards;
        return window;
    }

    private static Window CreateNotice(NotificationItem item)
    {
        var window = new NotificationNoticeWindow();
        var area = NotificationPlacement.Resolve().WorkAreaDip;
        // A third of the work area is the line's budget; past it the text ellipsizes rather than pushing the
        // pill across the taskbar.
        window.ConsumeWidth(area.Width / 3 - 2 * EdgeMarginDip);
        window.SetContent(item.Request.Message, item.Request.Level);
        return window;
    }

    private static void CloseByUser(NotificationItem item)
    {
        lock (_gate)
        {
            if (!_runners.TryGetValue(item, out var runner) || runner.IsClosing) return;
            runner.IsClosing = true;
            RunClickCallback(item);
            FadeTo(runner.Window, 0.0, FadeSeconds(), () => Finish(runner));
        }
    }

    /// <summary>The title bar's "mark all read": every visible card goes at once, each as a success. The
    /// queue refills the freed slots straight away, which is why a new batch can arrive immediately.</summary>
    private static void DismissAllCards()
    {
        lock (_gate)
        {
            foreach (var runner in _runners.Values
                         .Where(runner => runner.Item.EffectivePosition == NotificationPosition.CardStack
                                          && !runner.IsClosing)
                         .ToArray())
            {
                runner.IsClosing = true;
                FadeTo(runner.Window, 0.0, FadeSeconds(), () => Finish(runner));
            }
        }
    }

    private static void Finish(Runner runner)
    {
        lock (_gate)
        {
            CloseNow(runner);
            Queue.NotifyClosed(runner.Item);
            Restack(animated: _runners.Count > 0);
            if (_runners.Count == 0) StopTicker();
        }
    }

    private static void CloseNow(Runner runner)
    {
        _runners.Remove(runner.Item);
        // Clearing the animation before Close stops a fade still running from being reported against a
        // window that is going away.
        runner.Window.BeginAnimation(UIElement.OpacityProperty, null);
        runner.Window.Close();
    }

    /// <summary>The caller's click handler is plugin code, and a notification must never carry an exception
    /// back into the launcher's input pipeline.</summary>
    private static void RunClickCallback(NotificationItem item)
    {
        if (item.Request.OnClick == null) return;
        try
        {
            item.Request.OnClick.Invoke();
        }
        catch (Exception ex)
        {
            Logger.Log($"[Notifications] {item.SourceName}'s click callback threw: {ex.Message}", LogLevel.Warn);
        }
    }

    /// <summary>The one countdown for everything on screen. Per-item timers would each need their own pause
    /// and freeze bookkeeping; one tick subtracting elapsed time handles hover, the session lock and a busy
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
        // A locked session counts for nothing, so the reference point moves with it and the remaining time
        // is whatever it was when the screen went dark.
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
            foreach (var runner in _runners.Values.ToArray())
            {
                if (runner.IsClosing) continue;
                // Hovering holds the time, not the animation: a card mid fade-in keeps fading in.
                if (runner.Window.IsMouseOver) continue;

                runner.RemainingMs -= elapsedMs;
                if (runner.RemainingMs > 0) continue;

                runner.IsClosing = true;
                FadeTo(runner.Window, 0.0, FadeSeconds(), () => Finish(runner));
            }
        }
    }

    /// <summary>Places every visible notification: cards stack upward from the bottom-right corner of the
    /// anchor screen, oldest nearest the edge so a new one appears above it, and the notice sits at the
    /// bottom centre. A card the user dragged keeps the corner they left it in.</summary>
    private static void Restack(bool animated)
    {
        if (_runners.Count == 0) return;
        var area = NotificationPlacement.Resolve().WorkAreaDip;

        foreach (var runner in _runners.Values.Where(r => r.Item.EffectivePosition == NotificationPosition.BottomNotice))
        {
            var notice = runner.Window;
            notice.Left = area.X + (area.Width - notice.ActualWidth) / 2;
            notice.Top = area.Bottom - NoticeHeightDip - NoticeBottomGapDip;
        }

        var bottom = area.Bottom - EdgeMarginDip;
        foreach (var runner in _runners.Values
                     .Where(r => r.Item.EffectivePosition == NotificationPosition.CardStack && !r.IsPinnedByDrag)
                     .OrderBy(r => r.Sequence))
        {
            var card = runner.Window;
            card.MaxHeight = Math.Min(CardMaxHeightDip, area.Height * 0.5);
            card.Left = area.Right - CardWidthDip - EdgeMarginDip;
            var top = bottom - card.ActualHeight;
            MoveTo(runner, card.Left, top, animated);
            bottom = top - CardGapDip;
        }
    }

    private static void MoveTo(Runner runner, double left, double top, bool animated)
    {
        var card = runner.Window;
        if (!animated || runner.IsMoving)
        {
            card.Left = left;
            card.Top = top;
            return;
        }

        runner.IsMoving = true;
        card.Left = left;
        var move = new DoubleAnimation(top, TimeSpan.FromMilliseconds(RestackAnimationMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        move.Completed += (_, _) =>
        {
            card.BeginAnimation(Window.TopProperty, null);
            card.Top = top;
            runner.IsMoving = false;
        };
        card.BeginAnimation(Window.TopProperty, move);
    }

    private static void FadeTo(Window window, double to, double seconds, Action? onDone)
    {
        if (seconds <= 0)
        {
            window.Opacity = to;
            onDone?.Invoke();
            return;
        }

        var fade = new DoubleAnimation(to, TimeSpan.FromSeconds(seconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        if (onDone != null)
        {
            // Completed only fires for a timeline that actually ran, so it is attached before the fade starts.
            var dispatcher = window.Dispatcher;
            fade.Completed += (_, _) => dispatcher.BeginInvoke(onDone);
        }
        window.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>The shared fade setting, clamped again here: a hand-edited settings file must not be able to
    /// ask for a fade longer than the notification it is animating.</summary>
    private static double FadeSeconds()
    {
        var seconds = UserSettings.Load().NotificationFadeSeconds;
        if (double.IsNaN(seconds) || double.IsInfinity(seconds)) seconds = 1.0;
        return Math.Clamp(seconds, UiMetrics.MinNotificationFadeSeconds, UiMetrics.MaxNotificationFadeSeconds);
    }

    /// <summary>Names the sender on the card. A plugin cannot choose this: it comes from the assembly the
    /// SDK facade saw at the call site, which is the one attribution a plugin cannot forge.</summary>
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
                foreach (var runner in _runners.Values)
                    runner.Window.Visibility = locked ? Visibility.Hidden : Visibility.Visible;
                // Logged at the default level because whether this fires at all is the only way to tell a
                // frozen countdown from a fast one after the fact.
                Logger.Log($"[Notifications] session {(locked ? "locked" : "unlocked")}: " +
                           $"{_runners.Count} notification(s) {(locked ? "hidden and their countdowns frozen" : "shown again, counting down from where they stopped")}.",
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
                foreach (var runner in _runners.Values) runner.ResetDrag();
                Restack(animated: false);
            }
        }));
    }

    /// <summary>One shown notification: its window, and what is left of its time.</summary>
    private sealed class Runner(NotificationItem item, Window window)
    {
        private static int _sequence;

        public NotificationItem Item { get; } = item;
        public Window Window { get; } = window;
        public double RemainingMs { get; set; } = item.DurationSeconds * 1000;
        public bool IsClosing { get; set; }
        public bool IsMoving { get; set; }

        /// <summary>Arrival order, which is what decides the vertical order of the stack.</summary>
        public int Sequence { get; } = Interlocked.Increment(ref _sequence);

        public bool IsPinnedByDrag => Window is NotificationCardWindow { IsUserMoved: true };

        public void ResetDrag()
        {
            if (Window is NotificationCardWindow card) card.ClearUserMove();
        }
    }
}
