using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Services.Notifications;

/// <summary>
/// The showing rules for both notification positions: which request may go on screen now, which one has
/// to wait, which one replaces which, and how long each ended up being.
/// </summary>
/// <remarks>
/// Owns no window, reads no clock and knows nothing about DPI: the service decides when something is
/// actually gone (via <see cref="NotifyClosed"/>) and paints it, and this class keeps the ordering rules
/// in one place so they can be read and tested without a screen. Split out of the service for both
/// reasons, not for file length. The <paramref name="gate"/> it is handed is the service's own, which is
/// what makes dismissing through a handle as thread-safe as submitting through one was.
/// </remarks>
internal sealed class NotificationQueue(
    Func<bool> isFullscreen,
    Action<NotificationItem> show,
    Action<NotificationItem> hide,
    Action<string> logWarning,
    object gate)
{
    internal const int VisibleCardLimit = 5;
    /// <summary>What one plugin may have on screen and waiting at once: five slots plus the five that used to be
    /// the pending cap. Saying it as one number matters now that arrivals are paced, because a burst's cards sit
    /// in the queue instead of on screen, and counting only the queue would quietly halve what a plugin can get
    /// shown.</summary>
    internal const int PerPluginAcceptedLimit = 10;
    internal const double CardMinSeconds = 2;
    internal const double CardMaxSeconds = 30;
    internal const double CardDefaultSeconds = 8;
    internal const double NoticeMinSeconds = 2;
    internal const double NoticeMaxSeconds = 10;
    internal const double NoticeDefaultSeconds = 4;
    /// <summary>Ceiling for a card that has to collapse into the notice line because a fullscreen app owns the screen.</summary>
    internal const double CollapsedCardMaxSeconds = 5;

    private readonly List<NotificationItem> _cards = [];
    private readonly List<NotificationItem> _pending = [];
    private NotificationItem? _notice;

    // Suppresses the refill that a close would otherwise trigger while a bulk cancellation is still
    // pulling items out from under it, so a cancel cannot show a card it is about to cancel.
    private bool _batching;

    /// <summary>Accepts a request and returns the item that carries it. Never throws and never leaves
    /// the caller's task uncompleted: a rejected request comes back already failed.</summary>
    public NotificationItem Submit(NotificationRequest request, string pluginKey, string sourceName)
    {
        if (string.IsNullOrWhiteSpace(request.Title) && string.IsNullOrWhiteSpace(request.Message))
        {
            var rejected = new NotificationItem(request, pluginKey, sourceName, request.Position, 0, null);
            rejected.Complete(NotificationResult.Failed(NotificationFailure.InvalidRequest));
            return rejected;
        }

        var item = new NotificationItem(
            request, pluginKey, sourceName, request.Position,
            ClipDuration(request.Position, request.DurationSeconds), this);

        // A card is judged against the screen once here and again when it leaves the queue: a request
        // admitted before the game went fullscreen would otherwise be painted over it.
        if (request.Position == NotificationPosition.CardStack && isFullscreen())
            ShowNotice(Collapse(item));
        else if (item.EffectivePosition == NotificationPosition.CardStack)
            AdmitCard(item);
        else
            ShowNotice(item);

        return item;
    }

    /// <summary>Called once per notification when it stops existing, whether its time ran out, the user
    /// dismissed it, the caller closed it through its handle, or it never got on screen. The caller already
    /// holds the gate, except when it is a plugin's thread, in which case it came through
    /// <see cref="CloseFromHandle"/>.</summary>
    public void NotifyClosed(NotificationItem item)
    {
        item.Complete(NotificationResult.Success);
        if (ReferenceEquals(_notice, item)) _notice = null;
        var hadWindow = item.ReachedScreen;
        _cards.Remove(item);
        _pending.Remove(item);
        // A caller dismissing its own notification, or clicking its body, ends something still on screen, and
        // this is the only path that reaches the window then: the paths that tore it down themselves (a
        // replacement, a cancellation) find nothing left to do inside.
        if (hadWindow) hide(item);
        if (!_batching) PromoteOne();
    }

    /// <summary>Offers the screen one more waiting card, for whoever runs the clock. The queue keeps no time of
    /// its own, so the service asks this on its tick and a burst is fed a card at a time.</summary>
    internal void Feed() => PromoteOne();

    /// <summary>Whether anything is still waiting to be offered. The clock must keep running while this is true,
    /// because the tick is the only thing that promotes: an empty screen is not proof of an empty queue once
    /// presentations are handed to the dispatcher, and a stopped clock there strands every waiting request with a
    /// task that never finishes.</summary>
    internal bool HasWaiting => _pending.Count > 0;

    /// <summary>Ends a notification on its caller's behalf. Locked because a plugin holds the handle and may
    /// call from its own thread while the launcher's UI thread is walking the same lists from the countdown.</summary>
    internal void CloseFromHandle(NotificationItem item)
    {
        lock (gate) NotifyClosed(item);
    }

    /// <summary>Ends a batch the screen already took down, which is "mark all read" and several notifications
    /// coming due on one tick: deciding about the freed slots once is the whole point, because deciding per card
    /// promotes and re-lays out the stack again while the rest of the batch is still being pulled out from under
    /// it.</summary>
    public void CloseBatch(IReadOnlyList<NotificationItem> items)
    {
        _batching = true;
        try
        {
            foreach (var item in items) NotifyClosed(item);
        }
        finally
        {
            _batching = false;
        }
        PromoteOne();
    }

    /// <summary>Cancels everything the given plugin still has outstanding, shown or queued.</summary>
    public void CancelPlugin(string pluginKey)
    {
        const NotificationFailure reason = NotificationFailure.CancelledByPluginUnload;
        _batching = true;
        try
        {
            foreach (var item in _pending.Where(item => item.PluginKey == pluginKey).ToArray())
                FinishCancelled(item, _pending, reason);
            foreach (var item in _cards.Where(item => item.PluginKey == pluginKey).ToArray())
                FinishCancelled(item, _cards, reason);
            if (_notice?.PluginKey == pluginKey)
                FinishCancelled(_notice, null, reason, clearNotice: true);
        }
        finally
        {
            _batching = false;
        }
        PromoteOne();
    }

    /// <summary>Ends every outstanding request because the launcher is closing. Takes the animation, and
    /// with it any wait for an answer, with it.</summary>
    public void Shutdown()
    {
        const NotificationFailure reason = NotificationFailure.HostShuttingDown;
        _batching = true;
        try
        {
            foreach (var item in _pending.ToArray()) FinishCancelled(item, _pending, reason);
            foreach (var item in _cards.ToArray()) FinishCancelled(item, _cards, reason);
            if (_notice != null) FinishCancelled(_notice, null, reason, clearNotice: true);
        }
        finally
        {
            _batching = false;
        }
    }

    private void FinishCancelled(NotificationItem item, List<NotificationItem>? from, NotificationFailure reason, bool clearNotice = false)
    {
        from?.Remove(item);
        if (clearNotice) _notice = null;
        item.Complete(NotificationResult.Failed(reason));
        // Only a notification that reached the screen has a window to take down.
        if (item.ReachedScreen) hide(item);
    }

    private void AdmitCard(NotificationItem item)
    {
        var id = item.Request.Id;
        var previous = id == null
            ? null
            : _cards.FirstOrDefault(card => string.Equals(card.Request.Id, id, StringComparison.Ordinal));

        if (previous != null)
        {
            _cards[_cards.IndexOf(previous)] = item;
            // The stack is laid out by arrival order, not by the list's, so a replacement that keeps a fresh
            // sequence number jumps to the top of the pile instead of overwriting the card it replaced.
            item.Sequence = previous.Sequence;
            previous.Complete(NotificationResult.Failed(NotificationFailure.Replaced));
            hide(previous);
            item.ReachedScreen = true;
            show(item);
            return;
        }

        if (_cards.Count == 0 && _pending.Count == 0)
        {
            _cards.Add(item);
            item.ReachedScreen = true;
            show(item);
            return;
        }

        // Everything else waits its turn behind the clock, which feeds the screen one card per tick. Admitting a
        // burst straight to the slots was admitting a burst that all goes off together: five cards given the same
        // eight seconds in the same frame come due on the same tick later, and the stack above them falls five
        // slots in one move. Spacing the arrivals spaces their expiries with the same mechanism, and a card still
        // spends the time it was given -- counted from when it appeared, as it always was.
        if (_cards.Count(c => c.PluginKey == item.PluginKey)
            + _pending.Count(p => p.PluginKey == item.PluginKey) >= PerPluginAcceptedLimit)
        {
            logWarning($"[Notifications] {item.SourceName} already has {PerPluginAcceptedLimit} cards accepted; " +
                       $"dropped \"{Truncate(item.Request.Message, 120)}\" as QueueFull.");
            item.Complete(NotificationResult.Failed(NotificationFailure.QueueFull));
            return;
        }

        _pending.Add(item);
    }

    private void ShowNotice(NotificationItem item)
    {
        var previous = _notice;
        _notice = item;
        if (previous == null)
        {
            item.ReachedScreen = true;
            show(item);
            return;
        }

        // The line the user had not finished reading is gone with no trace on screen, so it is worth a
        // record, and the newer line simply paints over the old one.
        previous.Complete(NotificationResult.Failed(NotificationFailure.Replaced));
        logWarning($"[Notifications] a bottom notice was replaced before its time was up: " +
                   $"{previous.SourceName}, \"{Truncate(previous.Request.Message, 120)}\"");
        hide(previous);
        item.ReachedScreen = true;
        show(item);
    }

    /// <summary>Turns a card into notice-shaped output: the title and the source line have nowhere to go,
    /// and the time on screen is capped, so all three are logged while the text is still known.</summary>
    private NotificationItem Collapse(NotificationItem item)
    {
        item.EffectivePosition = NotificationPosition.BottomNotice;
        item.DurationSeconds = Math.Min(item.DurationSeconds, CollapsedCardMaxSeconds);
        logWarning($"[Notifications] a card was collapsed into the bottom notice because a fullscreen app owns " +
                   $"the screen, losing its title and source: {item.SourceName}, " +
                   $"title \"{Truncate(item.Request.Title, 80)}\", text \"{Truncate(item.Request.Message, 120)}\"");
        return item;
    }

    private void PromoteOne()
    {
        if (_pending.Count == 0) return;

        // One waiting card per pass, and the service's clock runs the next pass while room remains. A burst of
        // requests given the same duration is admitted in one frame, and if every free slot were filled in that
        // same frame their countdowns would all be due on the same tick several seconds later -- which is what
        // arrived as four cards vanishing and the whole stack re-sliding inside twelve milliseconds.
        if (isFullscreen())
        {
            // Feed the collapsed cards through the single notice line one at a time instead of all at
            // once, or each would replace the one before it before anyone could read it.
            if (_notice != null) return;
            ShowNotice(Collapse(TakePending()));
            return;
        }

        if (_cards.Count >= VisibleCardLimit) return;
        var next = TakePending();
        _cards.Add(next);
        next.ReachedScreen = true;
        show(next);
    }

    private NotificationItem TakePending()
    {
        var item = _pending[0];
        _pending.RemoveAt(0);
        return item;
    }

    /// <summary>Clamps an explicit duration to the position's own closed range, and supplies that
    /// position's default when the caller gave none. A value the caller did pass is never rejected, only
    /// moved to the nearest bound, so 0 and a negative both land on the lower bound. A value that cannot be
    /// compared at all lands there too: it asked for a duration, and the shortest legal one is the only
    /// answer that keeps it from sitting on screen far longer than intended.</summary>
    internal static double ClipDuration(NotificationPosition position, double? requested)
    {
        var (min, max, fallback) = position == NotificationPosition.CardStack
            ? (CardMinSeconds, CardMaxSeconds, CardDefaultSeconds)
            : (NoticeMinSeconds, NoticeMaxSeconds, NoticeDefaultSeconds);

        if (requested is not { } value) return fallback;
        if (double.IsNaN(value) || value < min) return min;
        return value > max ? max : value;
    }

    private static string Truncate(string? text, int limit)
    {
        if (string.IsNullOrEmpty(text)) return "(empty)";
        return text.Length <= limit ? text : string.Concat(text.AsSpan(0, limit), "...");
    }
}
