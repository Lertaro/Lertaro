namespace Lertaro.App.Views.QuickSearchWindow.Helpers;

/// <summary>Reads the clipboard for the quick window and suppresses consecutive duplicate imports.</summary>
internal sealed class QuickSearchClipboardSupport
{
    private string? _lastClipboardText;

    internal bool TryGetNewText(out string text)
    {
        text = string.Empty;
        if (!TryReadClipboardText(out var clipboardText)) return false;

        var previousText = _lastClipboardText;
        _lastClipboardText = clipboardText;
        if (!ShouldApply(clipboardText, previousText)) return false;

        text = clipboardText!;
        return true;
    }

    internal static bool ShouldApply(string? clipboardText, string? previousText)
        => !string.IsNullOrWhiteSpace(clipboardText)
            && !string.Equals(clipboardText, previousText, StringComparison.Ordinal);

    internal static bool ShouldReadClipboard(string? initialQuery) => initialQuery is null;

    private static bool TryReadClipboardText(out string? text)
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
            // The clipboard can be temporarily owned by another process. A failed read must not erase
            // the remembered value, or the next successful read would look like a new clipboard copy.
            return false;
        }
    }
}
