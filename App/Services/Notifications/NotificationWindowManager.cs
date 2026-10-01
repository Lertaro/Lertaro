using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Lertaro.App.Helpers.Visuals;
using Lertaro.App.Views.Notifications;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Services.Notifications;

/// <summary>
/// The windows themselves: one per notification, where they sit, how they fade in and out, and how they are
/// taken down again.
/// </summary>
/// <remarks>
/// Split out of <see cref="NotificationService"/>, which is the queue and the clock: this half holds no policy
/// and decides nothing about order or duration, only what is on screen. Every path that leaves a notification
/// ends in <see cref="TearDown"/>, exactly once, and every animation another party waits on carries a watchdog,
/// because a compositor that never reports a frame would otherwise strand both a window and a caller's task.
/// </remarks>
internal sealed class NotificationWindowManager(Func<double> fadeSeconds, Action<NotificationItem> onGone)
{
    internal const double CardWidthDip = 360;
    internal const double CardMaxHeightDip = 260;
    internal const double EdgeMarginDip = 12;
    internal const double CardGapDip = 8;
    internal const double NoticeHeightDip = 36;
    internal const double NoticeBottomGapDip = 16;

    // Fixed, deliberately: the stack shifting down to make room is not something the user asked to tune.
    private const int RestackAnimationMs = 200;

    private readonly Dictionary<NotificationItem, NotificationRunner> _runners = [];

    internal int Count => _runners.Count;

    internal IEnumerable<NotificationRunner> Countdown() => _runners.Values.ToArray();

    /// <summary>Builds and shows the window for an accepted notification, and arms its fade.</summary>
    internal void Present(NotificationItem item)
    {
        // The submitting thread handed this over and may have dismissed, replaced or cancelled the item since.
        // Painting it then would leave a window that is in no list, nobody times out, and nobody ever closes.
        if (item.IsSettled) return;

        var window = item.EffectivePosition == NotificationPosition.CardStack
            ? CreateCard(item)
            : (Window)CreateNotice(item);

        var runner = new NotificationRunner(item, window);
        _runners[item] = runner;
        window.Opacity = 0;
        // ShowActivated=False on both kinds: a notification that takes the foreground is worse than one that
        // never arrived, for anyone typing. Which kind of window this is was decided by the window itself from
        // the active theme's opacity, before it had a handle to commit that with.
        AltTabExcluder.Attach(window);
        window.Show();
        // ActualHeight is only known after the first layout, and the stack is measured in it, so the placement
        // runs twice: once to get the window roughly on screen, once when it knows its own size.
        Restack(animated: false);
        // The fade waits for the first rendered frame on purpose. A window sitting at alpha 0 has nothing
        // composited yet, so raising its alpha before WPF has produced a frame reveals the uninitialised
        // surface instead: a black card that fades in and only then turns into the notification. The watchdog
        // is there because a first frame is not guaranteed to be reported at all, and a notification that never
        // fades in is a notification nobody sees.
        window.ContentRendered += (_, _) => BeginFadeIn(runner);
        OneShot(fadeSeconds() + 0.5, () => BeginFadeIn(runner));
    }

    private void BeginFadeIn(NotificationRunner runner)
    {
        if (!_runners.ContainsKey(runner.Item) || runner.FadeStarted) return;
        runner.FadeStarted = true;
        Restack(animated: false);
        FadeTo(runner.Window, 1.0, fadeSeconds(), () => SettleOpacity(runner.Window));
    }

    /// <summary>Takes a finished fade-in off the layered path. A window whose opacity is animated stays
    /// per-window alpha layered, and a layered window has no ClearType: settling it back to a plain 1 is what
    /// gives the text its crispness for as long as it is actually being read.</summary>
    private static void SettleOpacity(Window window)
    {
        window.BeginAnimation(UIElement.OpacityProperty, null);
        window.Opacity = 1.0;
    }

    /// <summary>Ends a notification politely: fade it out, then take it away. Watchdog included, because the
    /// queue is waiting on this path for both a freed slot and a caller's completed task.</summary>
    internal void FadeOut(NotificationRunner runner)
    {
        if (runner.IsClosing) return;
        runner.IsClosing = true;
        var seconds = fadeSeconds();
        FadeTo(runner.Window, 0.0, seconds, () => Finish(runner));
        OneShot(seconds + 1.0, () => Finish(runner));
    }

    /// <summary>Takes a notification down without an animation: a replacement or a cancellation is meant to be
    /// immediate, so waiting for a fade would both flicker and delay the line that is replacing it.</summary>
    internal void TakeDown(NotificationItem item, bool fade)
    {
        if (!_runners.TryGetValue(item, out var runner)) return;
        if (fade)
        {
            FadeOut(runner);
            return;
        }
        Finish(runner);
    }

    private void Finish(NotificationRunner runner)
    {
        if (!TearDown(runner)) return;
        onGone(runner.Item);
    }

    /// <summary>Removes the runner and closes its window. The removal is the claim: whoever takes it first owns
    /// the teardown, so a watchdog and the animation it was watching cannot both hand the same item over.</summary>
    private bool TearDown(NotificationRunner runner)
    {
        if (!_runners.Remove(runner.Item)) return false;
        // Clearing the animation before Close stops a fade still running from being reported against a window
        // that is going away.
        runner.Window.BeginAnimation(UIElement.OpacityProperty, null);
        runner.Window.Close();
        return true;
    }

    /// <summary>The title bar's "mark all read": every visible card goes at once, each fading in parallel.</summary>
    internal void DismissAllCards()
    {
        foreach (var runner in _runners.Values
                     .Where(runner => runner.Item.EffectivePosition == NotificationPosition.CardStack && !runner.IsClosing)
                     .ToArray())
        {
            FadeOut(runner);
        }
    }

    /// <summary>Closes everything on screen now, for when the launcher is going away. No animation: the process
    /// is leaving, and waiting for one would hold the exit up.</summary>
    internal void CloseEverything()
    {
        foreach (var runner in _runners.Values.ToArray())
        {
            if (!TearDown(runner)) continue;
            onGone(runner.Item);
        }
    }

    /// <summary>Hides or shows every notification, which is what a session lock is for. The countdown itself is
    /// the service's business, so nothing here touches it.</summary>
    internal void HideForSession(bool hidden)
    {
        foreach (var runner in _runners.Values)
            runner.Window.Visibility = hidden ? Visibility.Hidden : Visibility.Visible;
    }

    /// <summary>Places every visible notification: cards stack upward from the bottom-right corner of the anchor
    /// screen, oldest nearest the edge so a new one appears above it, and the notice sits at the bottom centre.
    /// A card the user dragged keeps the corner they left it in.</summary>
    internal void Restack(bool animated)
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

    /// <summary>Drops the drag mark and re-anchors, for when the screen a notification sat on is gone. Remaining
    /// time is untouched: a display change is not the notification's fault.</summary>
    internal void Reanchor()
    {
        foreach (var runner in _runners.Values) runner.ResetDrag();
        Restack(animated: false);
    }

    private void MoveTo(NotificationRunner runner, double left, double top, bool animated)
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

    private static void FadeTo(Window window, double to, double seconds, Action onDone)
    {
        if (seconds <= 0)
        {
            window.Opacity = to;
            onDone();
            return;
        }

        var fade = new DoubleAnimation(to, TimeSpan.FromSeconds(seconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        // Completed only fires for a timeline that actually ran, so it is attached before the fade starts.
        var dispatcher = window.Dispatcher;
        fade.Completed += (_, _) => dispatcher.BeginInvoke(onDone);
        window.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>Runs <paramref name="action"/> once, on the UI thread, after the given slack. Every animation
    /// that something else waits on gets one of these.</summary>
    private static void OneShot(double seconds, Action action)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(0.25, seconds)) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
    }

    private NotificationCardWindow CreateCard(NotificationItem item)
    {
        var window = new NotificationCardWindow();
        window.SetContent(item.Request, item.SourceName);
        window.DismissRequested += () => CloseByUser(item);
        window.ReadAllRequested += DismissAllCards;
        return window;
    }

    private Window CreateNotice(NotificationItem item)
    {
        var window = new NotificationNoticeWindow();
        var area = NotificationPlacement.Resolve().WorkAreaDip;
        // A third of the work area is the line's budget; past it the text ellipsizes rather than pushing the
        // pill across the taskbar.
        window.ConsumeWidth(area.Width / 3 - 2 * EdgeMarginDip);
        window.SetContent(item.Request.Message, item.Request.Level);
        return window;
    }

    /// <summary>A card closed by the person reading it: the same end as its time running out, plus whatever the
    /// sender asked to happen on a click.</summary>
    private void CloseByUser(NotificationItem item)
    {
        if (!_runners.TryGetValue(item, out var runner) || runner.IsClosing) return;
        RunClickCallback(item);
        FadeOut(runner);
    }

    /// <summary>The caller's click handler is plugin code, and a notification must never carry an exception back
    /// into the launcher's input pipeline.</summary>
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
}
