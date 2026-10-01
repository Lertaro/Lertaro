namespace Lertaro.PluginSdk.Abstractions;

/// <summary>
/// The caller's view of one notification that the host has accepted. The completion task always
/// finishes, including when the notification is never rendered, so an <c>await</c> on it cannot park
/// a plugin's thread forever.
/// </summary>
public interface INotificationHandle
{
    /// <summary>
    /// Resolves when the notification stops existing: it was shown and its time ran out or the user
    /// dismissed it, or it never got on screen at all and <see cref="NotificationResult.Failure"/>
    /// says why.
    /// </summary>
    Task<NotificationResult> Completion { get; }

    /// <summary>
    /// Closes the notification now, as if the user had dismissed it. A no-op once it is already gone,
    /// and never reverses a result that has already been delivered.
    /// </summary>
    void Dismiss();
}
