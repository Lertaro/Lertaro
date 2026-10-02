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
internal sealed class NotificationWindowManager(
    Action<NotificationItem> onGone,
    Action<IReadOnlyList<NotificationItem>> onGoneBatch)
{
    internal const double CardWidthDip = 360;
    internal const double CardMaxHeightDip = 260;
    internal const double EdgeMarginDip = 12;
    internal const double CardGapDip = 8;
    internal const double NoticeHeightDip = 36;
    internal const double NoticeBottomGapDip = 16;

    // Above the reach of any desktop, so a window sitting there is simply not seen.
    private const double OffScreenParkTopDip = -32000;

    // Fixed, deliberately: the stack shifting down to make room is not something the user asked to tune.
    private const int RestackAnimationMs = 200;

    // How far above its slot a new card starts on its way into it. Above, not below: a new card lands on top of
    // the stack, where there is nothing to overlap, whereas rising from below would drag it through the card under
    // it for all but the eight DIP of the gap. It also runs against the direction the rest of the stack moves,
    // which is the whole point -- a card dropping into place reads as an arrival, a card sliding down reads as
    // something that was already here and had to make room.
    private const double EntryDropDip = 40;

    private readonly Dictionary<NotificationItem, NotificationRunner> _runners = [];

    internal int Count => _runners.Count;

    internal IEnumerable<NotificationRunner> Countdown() => _runners.Values.ToArray();

    /// <summary>Builds and shows the window for an accepted notification.</summary>
    internal void Present(NotificationItem item)
    {
        // The submitting thread handed this over and may have dismissed, replaced or cancelled the item since.
        // Painting it then would leave a window that is in no list, nobody times out, and nobody ever closes.
        if (item.IsSettled) return;

        var area = NotificationPlacement.Resolve().WorkAreaDip;
        var isCard = item.EffectivePosition == NotificationPosition.CardStack;
        var window = isCard ? CreateCard(item) : (Window)CreateNotice(item, area);

        var runner = new NotificationRunner(item, window) { Arriving = isCard };
        _runners[item] = runner;
        // ShowActivated=False on both kinds: a notification that takes the foreground is worse than one that
        // never arrived, for anyone typing. Which kind of window this is was decided by the window itself from
        // the active theme's opacity, before it had a handle to commit that with.
        // Parked before it is ever shown, because Show() hands the window to the OS and the OS paints it -- at
        // WPF's cascade position, which an outside sampler caught twice at y=26 and y=182 before the card leapt
        // 570 to 730px into the stack. The stack cannot know a card's height until the window exists, so there is
        // no correct position to place it at here; there is only a position nobody can see.
        window.Left = isCard ? area.Right - CardWidthDip - EdgeMarginDip : area.X + area.Width / 2;
        window.Top = OffScreenParkTopDip;
        AltTabExcluder.Attach(window);
        window.Show();
        // ActualHeight is only known after the first layout, and the stack is measured in it. Nothing was
        // rendered yet at alpha 1, so the layout is flushed here and the card is placed at its real size on the
        // first pass; re-laying out when the first frame is up is left as the correction it now is, not as the
        // visible jump the window would otherwise make from the bottom edge of the screen.
        window.UpdateLayout();
        Restack(animated: false);
        // Re-laid out when the first frame is up, and animated this time. A card's height is not final until it
        // has actually been arranged: the body text re-wraps against the pixel-rounded content width and can come
        // out a line taller than the pre-render measure said -- the sampler saw exactly that, a 196px slide
        // stopping at 177 and the last 19px arriving as a step, because this pass corrected every card above the
        // new one with animated:false. The correction cannot be avoided, so it is drawn instead.
        window.ContentRendered += (_, _) => Restack(animated: true);
    }

    /// <summary>Takes a notification's window down without reporting it, for a caller that reports a batch of
    /// them at once. Removal from the table is the claim, so nothing can be handed over twice.</summary>
    private bool TakeWindow(NotificationRunner runner)
    {
        if (!_runners.Remove(runner.Item)) return false;
        runner.Window.Close();
        return true;
    }

    /// <summary>Takes a notification down by its item, for the queue's side of a dismissal. An item with no
    /// window here is one that was still queued, and the queue has already dropped it.</summary>
    internal void TakeDown(NotificationItem item)
    {
        if (_runners.TryGetValue(item, out var runner)) Close(runner);
    }

    /// <summary>Closes a runner and tells the owner its slot is free.</summary>
    internal void Close(NotificationRunner runner)
    {
        if (TakeWindow(runner)) onGone(runner.Item);
    }

    /// <summary>Closes a whole group of notifications as one event. Several ending together is the normal case,
    /// not an edge: a batch given the same duration is due on the same tick. Reporting each separately re-lays out
    /// the stack and promotes a waiting card once per close, which is what made the survivors re-slide four times
    /// inside twelve milliseconds instead of taking one move.</summary>
    internal void CloseBatch(IReadOnlyList<NotificationRunner> runners)
    {
        var gone = new List<NotificationItem>(runners.Count);
        foreach (var runner in runners)
        {
            if (TakeWindow(runner)) gone.Add(runner.Item);
        }

        if (gone.Count > 0) onGoneBatch(gone);
    }

    /// <summary>The title bar's "mark all read": every visible card goes, each as a success. The queue offers the
    /// freed slots straight away, which is why a new batch can arrive immediately.</summary>
    internal void DismissAllCards()
    {
        CloseBatch(_runners.Values
            .Where(r => r.Item.EffectivePosition == NotificationPosition.CardStack)
            .ToArray());
    }

    /// <summary>Closes everything on screen now, for when the launcher is going away. The queue has already
    /// ended the requests by the time this runs, so it takes the windows down without reporting each one back.</summary>
    internal void CloseEverything()
    {
        foreach (var runner in _runners.Values.ToArray()) TakeWindow(runner);
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
    /// A card the user dragged keeps the corner they left it in, and the stack treats the band it occupies as
    /// taken rather than sliding under it.</summary>
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
                     .Where(r => r.Item.EffectivePosition == NotificationPosition.CardStack)
                     .OrderBy(r => r.Item.Sequence))
        {
            var card = runner.Window;
            // The card's own XAML holds it to CardMaxHeightDip, which is why this pass normally measures nothing.
            // What is left here is the rarer half-the-screen guard for a work area so short that a card could not
            // be read at all, and that changes only when the display does -- so the layout is flushed just then,
            // rather than once per card per restack, which is forced synchronous layout on the UI thread's clock.
            var cap = Math.Min(CardMaxHeightDip, area.Height * 0.5);
            if (cap < CardMaxHeightDip && card.MaxHeight != cap)
            {
                card.MaxHeight = cap;
                card.UpdateLayout();
            }

            if (runner.IsPinnedByDrag)
            {
                // The user owns this one's position and the stack may not move it. It can still stop counting the
                // band it sits in as free, which is all this line does.
                bottom = Math.Min(bottom, card.Top - CardGapDip);
                continue;
            }

            card.Left = area.Right - CardWidthDip - EdgeMarginDip;
            var top = bottom - card.ActualHeight;
            // A card that has never been on screen drops into its slot from above rather than blinking there.
            // Consumed here, on the first pass that places it, so the flag says "this one is arriving" to exactly
            // one restack and never to the ones that follow.
            var entering = runner.Arriving;
            runner.Arriving = false;
            MoveTo(runner, card.Left, top, animated: animated || entering, entering: entering);
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

    private void MoveTo(NotificationRunner runner, double left, double top, bool animated, bool entering = false)
    {
        var card = runner.Window;
        card.Left = left;

        // A slide already heading for this same place is left running. Every restack walks the whole stack, and
        // the restack that places a newly presented card is not about these windows at all -- a new card lands on
        // top of the stack and moves nothing below it. Releasing a movement that is merely passing through is the
        // jump the user sees, and it is what promoting a card into a freed slot did to the slide that promotion
        // was meant to make room for.
        if (runner.Moving is { } ongoing && Math.Abs(ongoing.Target - top) < 1) return;

        // Read before releasing: while a slide is in charge the window reports where it has got to, and clearing
        // the animation first would drop the card back to where that slide started. An arriving card has no slide
        // yet, so it is simply given one to start from -- forty DIP above the slot it drops into.
        var from = entering ? top - EntryDropDip : card.Top;
        card.BeginAnimation(Window.TopProperty, null);
        runner.Moving = null;
        if (!animated || Math.Abs(from - top) < 1)
        {
            card.Top = top;
            return;
        }

        card.Top = from;
        var move = new DoubleAnimation(top, TimeSpan.FromMilliseconds(RestackAnimationMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        // A restack that lands with the stack heading somewhere else takes the animation over rather than waiting
        // for it, so the callback has to be able to tell that it is no longer the slide in charge. Without that
        // check the superseded one would still report back and hand the card to a position the stack has already
        // moved past.
        move.Completed += (_, _) =>
        {
            if (!ReferenceEquals(runner.Moving?.Animation, move)) return;
            runner.Moving = null;
            card.BeginAnimation(Window.TopProperty, null);
            card.Top = top;
        };
        runner.Moving = new NotificationRunner.Slide(move, top);
        card.BeginAnimation(Window.TopProperty, move);
    }

    private NotificationCardWindow CreateCard(NotificationItem item)
    {
        var window = new NotificationCardWindow();
        window.SetContent(item.Request, item.SourceName);
        // Every card that is built here is being built because it is about to be shown for the first time, so the
        // rim marks exactly the arrivals and nothing else.
        window.FlashArrival();
        window.DismissRequested += () => CloseByUser(item);
        window.ReadAllRequested += DismissAllCards;
        return window;
    }

    private Window CreateNotice(NotificationItem item, Rect area)
    {
        var window = new NotificationNoticeWindow();
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
