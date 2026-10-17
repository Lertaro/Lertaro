namespace Lertaro.PluginSdk.Services;

/// <summary>
/// Reads the clipboard's text through the host, so a plugin gets the same text the quick window's
/// own auto-fill imports instead of opening the clipboard itself.
/// </summary>
public static class ClipboardService
{
    /// <summary>
    /// Delegate assigned by the host application. Returns the clipboard's plain text, or
    /// <see langword="null"/> when it holds none or could not be read.
    /// </summary>
    public static Func<string?>? GetTextFunc { get; set; }

    /// <summary>
    /// Returns the clipboard's plain text, or <see langword="null"/> when there is none, the clipboard is
    /// temporarily owned by another process, or no host is wired (a plugin running outside the launcher).
    /// Safe to call from a plugin's own thread: the host reads it on the UI thread, which is the STA
    /// apartment the clipboard needs. It never throws, because a clipboard that will not open is worth
    /// less than the caller's loop it would take down.
    /// </summary>
    public static string? GetText()
    {
        try
        {
            return GetTextFunc?.Invoke();
        }
        catch (Exception ex)
        {
            Logger.Log($"[ClipboardService] Host failed to read the clipboard: {ex.Message}", LogLevel.Warn);
            return null;
        }
    }
}
