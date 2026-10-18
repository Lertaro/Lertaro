namespace Lertaro.App.Helpers;

/// <summary>
/// Reads the clipboard's plain text the one way the app does it, for the quick window's auto-fill and
/// for <see cref="Lertaro.PluginSdk.Services.ClipboardService"/> alike.
/// </summary>
internal static class ClipboardTextHelper
{
    /// <summary>
    /// False is returned only when the read itself failed; a true with null text means the clipboard
    /// simply holds no text. A caller that remembers the last text it saw must keep that memory across a
    /// false, or the next successful read would look like a new clipboard copy.
    /// </summary>
    /// <remarks>
    /// Needs an STA thread -- WPF's clipboard is COM-backed. Both callers reach it from the UI thread.
    /// </remarks>
    internal static bool TryReadText(out string? text)
    {
        text = null;
        try
        {
            if (!System.Windows.Clipboard.ContainsText()) return true;
            text = System.Windows.Clipboard.GetText();
            return true;
        }
        catch (Exception)
        {
            // The clipboard can be temporarily owned by another process.
            return false;
        }
    }
}
