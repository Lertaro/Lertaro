using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Services.Notifications;

/// <summary>
/// One accepted notification: the request, the decision about how it is being shown, and the task the
/// caller waits on. This is what a plugin holds through <see cref="INotificationHandle"/>.
/// </summary>
/// <remarks>
/// Split out purely to keep NotificationQueue.cs under the repo's per-file line limit. It is the item the
/// queue decides about, not a second owner of anything: every field here is written by the queue, and the only
/// thing the item does on its own is hand a dismissal back to the gate the queue was given.
/// </remarks>
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

    private static int _sequence;

    public NotificationRequest Request { get; } = request;
    public string PluginKey { get; } = pluginKey;
    public string SourceName { get; } = sourceName;

    /// <summary>Arrival order, which is what decides the vertical order of the stack. A replacement is given the
    /// sequence of the card it overwrote, so it lands in that card's place rather than at the top.</summary>
    internal int Sequence { get; set; } = Interlocked.Increment(ref _sequence);

    /// <summary>The position it is actually rendered as, which differs from the request for a card
    /// collapsed into the notice line.</summary>
    public NotificationPosition EffectivePosition { get; set; } = effectivePosition;

    /// <summary>Seconds already clipped to the position's range: how long it will be on screen.</summary>
    public double DurationSeconds { get; set; } = durationSeconds;

    /// <summary>Whether this ever had a window, which decides whether cancelling has anything to take down.</summary>
    internal bool ReachedScreen { get; set; }

    /// <summary>True once an end state has been delivered, so a presentation already handed to the UI thread
    /// can be dropped instead of painted.</summary>
    internal bool IsSettled { get; private set; }

    public Task<NotificationResult> Completion => _completion.Task;

    /// <summary>Closes the notification now. This is the one entry point a plugin reaches without the host
    /// around to lock for it, so it goes through the queue's gated path rather than straight into the lists.</summary>
    public void Dismiss() => owner?.CloseFromHandle(this);

    /// <summary>First end state wins, so a close that arrives after a cancellation cannot rewrite it.</summary>
    internal void Complete(NotificationResult result)
    {
        if (_completion.TrySetResult(result)) IsSettled = true;
    }
}
