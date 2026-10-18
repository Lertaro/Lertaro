using Lertaro.App.Helpers;

namespace Lertaro.App.Views.QuickSearchWindow.Helpers;

/// <summary>Reads the clipboard for the quick window and suppresses consecutive duplicate imports.</summary>
internal sealed class QuickSearchClipboardSupport
{
    private string? _lastClipboardText;

    internal bool TryGetNewText(out string text)
    {
        text = string.Empty;
        if (!ClipboardTextHelper.TryReadText(out var clipboardText)) return false;

        var searchText = SearchTextPasteFormatter.FormatForSearch(clipboardText);
        var previousText = _lastClipboardText;
        _lastClipboardText = searchText;
        if (!ShouldApply(searchText, previousText)) return false;

        text = searchText!;
        return true;
    }

    internal static bool ShouldApply(string? clipboardText, string? previousText)
        => !string.IsNullOrWhiteSpace(clipboardText)
            && !string.Equals(clipboardText, previousText, StringComparison.Ordinal);

    internal static bool ShouldReadClipboard(string? initialQuery) => initialQuery is null;
}
