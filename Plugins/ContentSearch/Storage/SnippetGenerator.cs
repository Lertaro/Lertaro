using System.Text;
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

        var tokens = string.IsNullOrWhiteSpace(query)
            ? Array.Empty<string>()
            : query.Split(new[] { ' ', '+', '"' }, StringSplitOptions.RemoveEmptyEntries);

        var firstMatchIndex = -1;
        var matchedTokenLength = 0;

        foreach (var token in tokens)
        {
            var idx = content.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && (firstMatchIndex < 0 || idx < firstMatchIndex))
            {
                firstMatchIndex = idx;
                matchedTokenLength = token.Length;
            }
        }

        if (firstMatchIndex < 0 && !string.IsNullOrWhiteSpace(query))
        {
            // Bounded probe for fuzzy match to avoid allocating large arrays on LOH for multi-MB content
            var probeLen = Math.Min(content.Length, 1000);
            var probe = content.Substring(0, probeLen);
            var mask = FuzzyMatchService.GetHighlightMask(probe, query);
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
            var probeLen = Math.Min(content.Length, maxLength * 2);
            var norm = NormalizeWhitespace(content.Substring(0, probeLen));
            var len = Math.Min(norm.Length, maxLength);
            return norm.Substring(0, len).Trim();
        }

        var contextBefore = Math.Max(0, (maxLength - matchedTokenLength) / 3);
        var rawStart = Math.Max(0, firstMatchIndex - contextBefore);
        var rawEnd = Math.Min(content.Length, rawStart + maxLength * 2);

        var rawSlice = content.Substring(rawStart, rawEnd - rawStart);
        var normSlice = NormalizeWhitespace(rawSlice);

        var tokenToFind = tokens.Length > 0 ? tokens[0] : query;
        var tokenInNorm = normSlice.IndexOf(tokenToFind, StringComparison.OrdinalIgnoreCase);
        string snippet;
        if (tokenInNorm >= 0)
        {
            var sStart = Math.Max(0, tokenInNorm - contextBefore);
            var sLen = Math.Min(normSlice.Length - sStart, maxLength);
            snippet = normSlice.Substring(sStart, sLen).Trim();
        }
        else
        {
            var sLen = Math.Min(normSlice.Length, maxLength);
            snippet = normSlice.Substring(0, sLen).Trim();
        }

        var prefix = rawStart > 0 ? "..." : "";
        var suffix = rawEnd < content.Length ? "..." : "";

        return $"{prefix}{snippet}{suffix}";
    }

    public static string NormalizeWhitespace(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var sb = new StringBuilder(Math.Min(text.Length, 1024));
        var prevWasSpace = false;
        foreach (var ch in text)
        {
            if (ch == '\r' || ch == '\n' || ch == '\t' || ch == '\f' || ch == '\v' || char.IsWhiteSpace(ch))
            {
                if (!prevWasSpace)
                {
                    sb.Append(' ');
                    prevWasSpace = true;
                }
            }
            else
            {
                sb.Append(ch);
                prevWasSpace = false;
            }
        }
        return sb.ToString().Trim();
    }
}
