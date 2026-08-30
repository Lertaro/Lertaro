namespace Lertaro.App.Helpers;

/// <summary>Applies the search box's multi-line paste behavior to any text entry point.</summary>
internal static class SearchTextPasteFormatter
{
    internal static string? FormatForSearch(string? text)
    {
        if (!TryGetLines(text, out var normalizedText, out var lines))
            return text;

        return lines.Length >= 2 ? string.Join(" | ", lines) : normalizedText.Split('\n')[0];
    }

    internal static bool TryFormatMultiLine(string? text, out string formatted)
    {
        formatted = string.Empty;
        if (!TryGetLines(text, out _, out var lines) || lines.Length < 2)
            return false;

        formatted = string.Join(" | ", lines);
        return true;
    }

    private static bool TryGetLines(string? text, out string normalizedText, out string[] lines)
    {
        normalizedText = text ?? string.Empty;
        lines = [];
        if (string.IsNullOrEmpty(text) || (!text.Contains('\r') && !text.Contains('\n')))
            return false;

        normalizedText = text.Replace("\r\n", "\n").Replace('\r', '\n');
        lines = normalizedText.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();
        return true;
    }
}
