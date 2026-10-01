using System.Reflection;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.PluginSdk.Services;

/// <summary>
/// Lets a plugin get the user's attention from the background through the host's own notifications,
/// without coupling plugins to the host UI assembly.
/// </summary>
/// <remarks>
/// The host, not the plugin, decides how long anything stays on screen and where: a request outside a
/// position's allowed range is clipped to it. Every entry point here is safe to call from a plugin's own
/// background thread and never throws, because an escaping exception would take the caller's loop down
/// with it. A plugin that needs a window it owns should use <see cref="Windows.PluginWindow"/> instead,
/// and one that needs an answer should use <see cref="PluginMessageBoxService"/>.
/// </remarks>
public static class PluginNotificationService
{
    /// <summary>
    /// Delegate assigned by the host application. Receives the request together with the assembly that
    /// made it, and returns the handle for it, or <see langword="null"/> when nothing served it.
    /// </summary>
    public static Func<NotificationRequest, Assembly, INotificationHandle?>? ShowRequestFunc { get; set; }

    /// <summary>
    /// Asks the host to show a notification and returns the handle for it right away. The handle's
    /// completion task always finishes, so awaiting it cannot park a caller thread; a notification that
    /// never got on screen ends as a failure saying why.
    /// </summary>
    public static INotificationHandle Show(NotificationRequest request) =>
        TryShow(request, Assembly.GetCallingAssembly())
        ?? new FinishedNotificationHandle(NotificationResult.Failed(NotificationFailure.Unavailable));

    /// <summary>
    /// Same as <see cref="Show(NotificationRequest)"/>, for callers that only want the end state.
    /// </summary>
    public static Task<NotificationResult> ShowAsync(NotificationRequest request) =>
        TryShow(request, Assembly.GetCallingAssembly())?.Completion
        ?? Task.FromResult(NotificationResult.Failed(NotificationFailure.Unavailable));

    /// <summary>
    /// Asks the host to show a dismissible background notification. Returns false when no host is wired
    /// (a plugin running outside the launcher), so callers that have a fallback can detect it.
    /// </summary>
    /// <remarks>
    /// Kept for the plugins already written against it; it is the two-argument shape of
    /// <see cref="Show(NotificationRequest)"/> and returns whether the notification was accepted for
    /// display rather than a handle to it.
    /// </remarks>
    public static bool Show(string title, string text, Action? onClick = null) =>
        TryShow(new NotificationRequest { Title = title, Message = text, OnClick = onClick },
            Assembly.GetCallingAssembly()) != null;

    // GetCallingAssembly is read in each public frame above, never inside this one: reached from here it
    // would name PluginSdk itself, and a source name handed over as a string is one a plugin could forge.
    // The host maps the assembly it receives to the name shown on the card, which is what makes the
    // attribution something a plugin cannot choose.
    private static INotificationHandle? TryShow(NotificationRequest request, Assembly source)
    {
        try
        {
            return ShowRequestFunc?.Invoke(request, source);
        }
        catch (Exception ex)
        {
            Logger.Log($"[PluginNotificationService] Host refused the notification: {ex.Message}", LogLevel.Warn);
            return null;
        }
    }

    private sealed class FinishedNotificationHandle(NotificationResult result) : INotificationHandle
    {
        public Task<NotificationResult> Completion { get; } = Task.FromResult(result);

        public void Dismiss() { }
    }
}
