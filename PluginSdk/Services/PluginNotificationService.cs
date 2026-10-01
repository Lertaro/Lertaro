namespace Lertaro.PluginSdk.Services;

/// <summary>
/// Lets a plugin get the user's attention from the background through the host's own tray icon,
/// without coupling plugins to the host UI assembly.
/// </summary>
/// <remarks>
/// Deliberately narrower than the host's own balloon call: the display duration and the icon glyph stay
/// the host's, because the tray icon is one shared resource and a plugin that could pick its own duration
/// would fight every other balloon over it. A plugin that needs a window it owns should use
/// <see cref="Windows.PluginWindow"/> instead of a notification.
/// </remarks>
public static class PluginNotificationService
{
    /// <summary>
    /// Delegate assigned by the host application. Receives the title, the body text and an optional
    /// click callback, and returns whether the notification was accepted for display.
    /// </summary>
    public static Func<string, string, Action?, bool>? ShowFunc { get; set; }

    /// <summary>
    /// Asks the host to show a dismissible background notification. Returns false when no host is wired
    /// (a plugin running outside the launcher) or when the tray icon is not available, so callers that
    /// have a fallback can detect it. Never throws: this is reached from plugin background threads, where
    /// an exception would take the caller's whole loop down with it.
    /// </summary>
    public static bool Show(string title, string text, Action? onClick = null)
    {
        try
        {
            return ShowFunc?.Invoke(title, text, onClick) ?? false;
        }
        catch (Exception ex)
        {
            Logger.Log($"[PluginNotificationService] Host refused the notification: {ex.Message}", LogLevel.Warn);
            return false;
        }
    }
}
