using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.ContentSearch.Storage;

/// <summary>
/// Extracts concise, context-rich text snippets surrounding search query keywords.
/// </summary>
public static class SnippetGenerator
{
    public const int DefaultSnippetLength = 120;

    public static string CreateSnippet(string content, string query, int maxLength = DefaultSnippetLength)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        if (string.IsNullOrWhiteSpace(query))
        {
            var len = Math.Min(content.Length, maxLength);
            return content.Substring(0, len).Replace("\r\n", " ").Replace('\n', ' ').Trim();
        }

        var normalizedContent = content.Replace("\r\n", " ").Replace('\n', ' ').Replace('\t', ' ');
        var tokens = query.Split(new[] { ' ', '+', '"' }, StringSplitOptions.RemoveEmptyEntries);

        var firstMatchIndex = -1;
        var matchedTokenLength = 0;

        foreach (var token in tokens)
        {
            var idx = normalizedContent.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && (firstMatchIndex < 0 || idx < firstMatchIndex))
            {
                firstMatchIndex = idx;
                matchedTokenLength = token.Length;
            }
        }

        if (firstMatchIndex < 0)
        {
            var mask = FuzzyMatchService.GetHighlightMask(normalizedContent, query);
            if (mask != null)
            {
                for (var i = 0; i < mask.Length; i++)
                {
                    if (mask[i])
                    {
                        firstMatchIndex = i;
                        matchedTokenLength = Math.Max(1, query.Length);
                        break;
                    }
                }
            }
        }

        if (firstMatchIndex < 0)
        {
            var len = Math.Min(normalizedContent.Length, maxLength);
            return normalizedContent.Substring(0, len).Trim();
        }

        var contextBefore = Math.Max(0, (maxLength - matchedTokenLength) / 3);
        var start = Math.Max(0, firstMatchIndex - contextBefore);
        var end = Math.Min(normalizedContent.Length, start + maxLength);

        var snippet = normalizedContent.Substring(start, end - start).Trim();
        var prefix = start > 0 ? "..." : "";
        var suffix = end < normalizedContent.Length ? "..." : "";

        return $"{prefix}{snippet}{suffix}";
    }
}
