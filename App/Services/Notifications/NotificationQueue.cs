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
/// reasons, not for file length.
/// </remarks>
internal sealed class NotificationQueue(
    Func<bool> isFullscreen,
    Action<NotificationItem> show,
    Action<NotificationItem, bool> hide,
    Action<string> logWarning)
{
    internal const int VisibleCardLimit = 5;
    internal const int PerPluginPendingLimit = 5;
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
    /// dismissed it, the caller closed it through its handle, or it never got on screen.</summary>
    public void NotifyClosed(NotificationItem item)
    {
        item.Complete(NotificationResult.Success);
        if (ReferenceEquals(_notice, item)) _notice = null;
        var hadWindow = item.ReachedScreen;
        _cards.Remove(item);
        _pending.Remove(item);
        // A caller dismissing its own notification has no window to close: only this path ends a
        // notification that is on screen right now, and the paths that already took their window down
        // (a fade-out, a replacement) find nothing left to do here.
        if (hadWindow) hide(item, true);
        if (!_batching) Refill();
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
        Refill();
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
        // Only a notification that reached the screen has a window to take down, and a cancellation is
        // meant to be immediate rather than wait out an animation.
        if (item.ReachedScreen) hide(item, false);
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
            previous.Complete(NotificationResult.Failed(NotificationFailure.Replaced));
            hide(previous, false);
            item.ReachedScreen = true;
            show(item);
            return;
        }

        if (_cards.Count < VisibleCardLimit)
        {
            _cards.Add(item);
            item.ReachedScreen = true;
            show(item);
            return;
        }

        if (_pending.Count(pending => pending.PluginKey == item.PluginKey) >= PerPluginPendingLimit)
        {
            logWarning($"[Notifications] {item.SourceName} already has {PerPluginPendingLimit} cards queued; " +
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
        // record. The newer line appears without a fade, which is what keeps a run of notices from
        // strobing.
        previous.Complete(NotificationResult.Failed(NotificationFailure.Replaced));
        logWarning($"[Notifications] a bottom notice was replaced before its time was up: " +
                   $"{previous.SourceName}, \"{Truncate(previous.Request.Message, 120)}\"");
        hide(previous, false);
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

    private void Refill()
    {
        while (_pending.Count > 0)
        {
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

/// <summary>
/// One accepted notification: the request, the decision about how it is being shown, and the task the
/// caller waits on. This is what a plugin holds through <see cref="INotificationHandle"/>.
/// </summary>
internal sealed class NotificationItem(
    NotificationRequest request,
    string pluginKey,
    string sourceName,
    NotificationPosition effectivePosition,
    double durationSeconds,
    NotificationQueue? owner) : INotificationHandle
{
    // Continuations run elsewhere on purpose: completing this from the UI thread would hand a plugin's
    // continuation the thread that is in the middle of closing the notification's window.
    private readonly TaskCompletionSource<NotificationResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public NotificationRequest Request { get; } = request;
    public string PluginKey { get; } = pluginKey;
    public string SourceName { get; } = sourceName;

    /// <summary>The position it is actually rendered as, which differs from the request for a card
    /// collapsed into the notice line.</summary>
    public NotificationPosition EffectivePosition { get; set; } = effectivePosition;

    /// <summary>Seconds already clipped to range, counted from the start of the fade-in.</summary>
    public double DurationSeconds { get; set; } = durationSeconds;

    /// <summary>Whether this ever had a window, which decides whether cancelling has anything to take down.</summary>
    internal bool ReachedScreen { get; set; }

    /// <summary>True once an end state has been delivered, so a presentation already handed to the UI thread
    /// can be dropped instead of painted.</summary>
    internal bool IsSettled { get; private set; }

    public Task<NotificationResult> Completion => _completion.Task;

    public void Dismiss() => owner?.NotifyClosed(this);

    /// <summary>First end state wins, so a close that arrives after a cancellation cannot rewrite it.</summary>
    internal void Complete(NotificationResult result)
    {
        if (_completion.TrySetResult(result)) IsSettled = true;
    }
}
