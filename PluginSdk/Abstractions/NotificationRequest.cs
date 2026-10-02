namespace Lertaro.PluginSdk.Abstractions;

/// <summary>How urgent the notification is. Drives the icon and the semantic colour, nothing else.</summary>
public enum NotificationLevel
{
    Info,
    Warn,
    Error
}

/// <summary>
/// Where on screen the notification goes. Each value has its own queue and its own rules; they never
/// block one another.
/// </summary>
public enum NotificationPosition
{
    /// <summary>
    /// Bottom-right stack of cards, the default. Up to five visible at once, the rest queued, a card
    /// shows its source, and clicking its body closes it.
    /// </summary>
    CardStack,

    /// <summary>
    /// Bottom-centre single line. No title, no source, no controls: a new one replaces whatever was
    /// showing immediately, with no teardown to wait out.
    /// </summary>
    BottomNotice
}

/// <summary>Why a notification never reached the user, or was cut short before its time was up.</summary>
public enum NotificationFailure
{
    /// <summary>No host to serve the request, which only happens outside the running launcher.</summary>
    Unavailable,

    /// <summary>Replaced by a newer request carrying the same <see cref="NotificationRequest.Id"/>.</summary>
    Replaced,

    /// <summary>Dropped because the calling plugin already had its fill of queued requests.</summary>
    QueueFull,

    /// <summary>The plugin was disabled or unloaded while its notification was showing or queued.</summary>
    CancelledByPluginUnload,

    /// <summary>Nothing to show: both <see cref="NotificationRequest.Title"/> and
    /// <see cref="NotificationRequest.Message"/> were empty.</summary>
    InvalidRequest,

    /// <summary>The launcher is closing and took the notification with it.</summary>
    HostShuttingDown
}

/// <summary>
/// The outcome of one notification: either it was shown and ended normally, or it failed with a reason.
/// </summary>
/// <param name="Succeeded">True when the notification reached the screen and ended on its own terms.</param>
/// <param name="Failure">Why it did not, or <see langword="null"/> when it succeeded.</param>
public readonly record struct NotificationResult(bool Succeeded, NotificationFailure? Failure = null)
{
    public static readonly NotificationResult Success = new(true);

    public static NotificationResult Failed(NotificationFailure failure) => new(false, failure);
}

/// <summary>What to show, where, and for how long.</summary>
public sealed class NotificationRequest
{
    /// <summary>
    /// Optional. Two requests with the same Id address the same notification: in the card stack a new
    /// one replaces the visible one (the earlier caller learns it was <see cref="NotificationFailure.Replaced"/>),
    /// so a plugin repeating a status update cannot fill the screen with copies of itself.
    /// </summary>
    public string? Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public NotificationLevel Level { get; init; } = NotificationLevel.Info;

    public NotificationPosition Position { get; init; } = NotificationPosition.CardStack;

    /// <summary>
    /// How long the notification stays on screen. Leave it <see langword="null"/> for the position's own
    /// default. An explicit value outside the position's range is clipped to the nearest bound rather than
    /// rejected, so a negative or an uncomparable one lands on the shorter end of that range.
    /// </summary>
    public double? DurationSeconds { get; init; }

    /// <summary>
    /// Runs when the user dismisses the card by any route -- clicking its body or its close button are the
    /// same outcome to the host -- in addition to closing it. Not run when the countdown simply expires, nor
    /// by <see cref="INotificationHandle.Dismiss"/>. Ignored by
    /// <see cref="NotificationPosition.BottomNotice"/>, which has no interactive parts.
    /// </summary>
    public Action? OnClick { get; init; }
}
