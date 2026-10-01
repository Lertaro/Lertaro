using System.Windows;
using System.Windows.Media.Animation;
using Lertaro.App.Helpers.Visuals;
using Lertaro.App.Views.Notifications;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Services.Notifications;

/// <summary>
/// The windows themselves: one per notification, where they sit, and how they are taken down.
/// </summary>
/// <remarks>
/// Split out of <see cref="NotificationService"/>, which is the queue and the clock: this half holds no policy
/// and decides nothing about order or duration, only what is on screen.
///
/// There is deliberately no fade. Fading a whole window means animating its opacity, and an opaque window only
/// does that by becoming a layered one first -- which costs ClearType and a per-pixel composite on every frame,
/// and flips between the two states at each end of the animation. Both of those were observed on screen here:
/// soft text at small sizes, and a black rectangle where the card had not been painted yet. So notifications
/// appear and go away instantly, and the only animation left is the 200ms slide of the stack making room, which
/// moves a window rather than compositing one.
/// </remarks>
internal sealed class NotificationWindowManager(Action<NotificationItem> onGone)
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

    /// <summary>Builds and shows the window for an accepted notification.</summary>
    internal void Present(NotificationItem item)
    {
        // The submitting thread handed this over and may have dismissed, replaced or cancelled the item since.
        // Painting it then would leave a window that is in no list, nobody times out, and nobody ever closes.
        if (item.IsSettled) return;

        var window = item.EffectivePosition == NotificationPosition.CardStack
            ? CreateCard(item)
            : (Window)CreateNotice(item);

        _runners[item] = new NotificationRunner(item, window);
        // ShowActivated=False on both kinds: a notification that takes the foreground is worse than one that
        // never arrived, for anyone typing. Which kind of window this is was decided by the window itself from
        // the active theme's opacity, before it had a handle to commit that with.
        AltTabExcluder.Attach(window);
        window.Show();
        // ActualHeight is only known after the first layout, and the stack is measured in it, so the placement
        // runs twice: once to get the window roughly on screen, once when it knows its own size.
        Restack(animated: false);
        window.ContentRendered += (_, _) => Restack(animated: false);
    }

    /// <summary>Takes a notification down by its item, for the queue's side of a dismissal. An item with no
    /// window here is one that was still queued, and the queue has already dropped it.</summary>
    internal void TakeDown(NotificationItem item)
    {
        if (_runners.TryGetValue(item, out var runner)) Close(runner);
    }

    /// <summary>Closes a runner and tells the owner its slot is free. Removal from the table is the claim, so
    /// nothing can be handed over twice.</summary>
    internal void Close(NotificationRunner runner)
    {
        if (!_runners.Remove(runner.Item)) return;
        runner.Window.Close();
        onGone(runner.Item);
    }

    /// <summary>The title bar's "mark all read": every visible card goes, each as a success. The queue refills
    /// the freed slots straight away, which is why a new batch can arrive immediately.</summary>
    internal void DismissAllCards()
    {
        foreach (var runner in _runners.Values
                     .Where(r => r.Item.EffectivePosition == NotificationPosition.CardStack)
                     .ToArray())
        {
            Close(runner);
        }
    }

    /// <summary>Closes everything on screen now, for when the launcher is going away. The queue has already
    /// ended the requests by the time this runs, so it takes the windows down without reporting each one back.</summary>
    internal void CloseEverything()
    {
        foreach (var runner in _runners.Values.ToArray())
        {
            if (!_runners.Remove(runner.Item)) continue;
            runner.Window.Close();
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
        // A slide that never reports back only leaves this card excluded from the next animation, so it needs
        // no watchdog: the position itself is already decided by the stack's order.
        move.Completed += (_, _) =>
        {
            card.BeginAnimation(Window.TopProperty, null);
            card.Top = top;
            runner.IsMoving = false;
        };
        card.BeginAnimation(Window.TopProperty, move);
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
        if (!_runners.TryGetValue(item, out var runner)) return;
        RunClickCallback(item);
        Close(runner);
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
