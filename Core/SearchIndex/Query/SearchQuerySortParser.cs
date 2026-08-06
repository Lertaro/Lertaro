namespace Lertaro.Core.SearchIndex.Query;

// Splits an optional trailing "<query> :a,b,c" or "<query> ::\"hello world\"" or "<query> ::hello\\ world"
// suffix off a raw search query into raw tokens -- deliberately dumb: it has no idea what a token means 
// (that's up to whichever IQueryTokenProvider plugin claims it).
public static class SearchQuerySortParser
{
    public static string Strip(string query, out IReadOnlyList<string> tokens, char prefixChar = ':')
    {
        tokens = Array.Empty<string>();

        var trimmed = query.TrimEnd();
        if (trimmed.Length < 2)
        {
            return query;
        }

        var idx = FindTrailingTokenPrefixIndex(trimmed, prefixChar);
        if (idx >= 0)
        {
            var tokenSegment = trimmed[idx..];
            if (TryParseTokenSegment(tokenSegment, prefixChar, out var parsedTokens))
            {
                tokens = parsedTokens;
                return trimmed[..idx].TrimEnd();
            }
        }

        return query;
    }

    private static int FindTrailingTokenPrefixIndex(string query, char prefixChar)
    {
        // Scan backwards for a prefixChar (e.g. ':') that starts a trailing token segment.
        // The prefixChar must be preceded by start-of-string or unescaped whitespace.
        var activeQuote = '\0';
        var candidateIndex = -1;

        for (var i = query.Length - 1; i >= 0; i--)
        {
            var c = query[i];
            if (IsEscaped(query, i))
            {
                continue;
            }

            if (activeQuote != '\0')
            {
                if (c == activeQuote)
                {
                    activeQuote = '\0';
                }
            }
            else if (c == '"' || c == '\'')
            {
                activeQuote = c;
            }
            else if (c == prefixChar)
            {
                if (i == 0 || (char.IsWhiteSpace(query[i - 1]) && !IsEscaped(query, i - 1)))
                {
                    candidateIndex = i;
                    while (candidateIndex > 0 && query[candidateIndex - 1] == prefixChar &&
                           (candidateIndex - 1 == 0 || (char.IsWhiteSpace(query[candidateIndex - 2]) && !IsEscaped(query, candidateIndex - 2))))
                    {
                        candidateIndex--;
                    }
                    break;
                }
            }
        }

        return candidateIndex;
    }

    private static bool IsEscaped(string text, int index)
    {
        var backslashCount = 0;
        for (var i = index - 1; i >= 0 && text[i] == '\\'; i--)
        {
            backslashCount++;
        }

        return backslashCount % 2 != 0;
    }

    private static bool TryParseTokenSegment(string segment, char prefixChar, out IReadOnlyList<string> tokens)
    {
        tokens = Array.Empty<string>();
        if (segment.Length < 2 || segment[0] != prefixChar)
        {
            return false;
        }

        var payload = segment[1..];
        var rawTokens = SplitByCommaRespectingQuotes(payload);
        if (rawTokens.Count == 0 || rawTokens.Any(t => string.IsNullOrWhiteSpace(t) || HasUnquotedUnescapedSpaces(t)))
        {
            return false;
        }

        var list = new List<string>(rawTokens.Count);
        foreach (var t in rawTokens)
        {
            list.Add(UnquoteToken(t));
        }

        tokens = list;
        return true;
    }

    private static bool HasUnquotedUnescapedSpaces(string token)
    {
        var trimmed = token.Trim();
        if (IsQuoted(trimmed))
        {
            return false;
        }

        var firstQuote = IndexOfUnescapedQuote(trimmed);
        var lastQuote = LastIndexOfUnescapedQuote(trimmed);
        if (firstQuote >= 0 && lastQuote > firstQuote)
        {
            var outside = trimmed[..firstQuote] + trimmed[(lastQuote + 1)..];
            return ContainsUnescapedSpace(outside);
        }

        return ContainsUnescapedSpace(trimmed);
    }

    private static bool ContainsUnescapedSpace(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]) && !IsEscaped(text, i))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsQuoted(string s) =>
        (s.StartsWith('"') && s.EndsWith('"') && s.Length >= 2 && !IsEscaped(s, s.Length - 1)) ||
        (s.StartsWith('\'') && s.EndsWith('\'') && s.Length >= 2 && !IsEscaped(s, s.Length - 1));

    private static int IndexOfUnescapedQuote(string s)
    {
        for (var i = 0; i < s.Length; i++)
        {
            if ((s[i] == '"' || s[i] == '\'') && !IsEscaped(s, i))
            {
                return i;
            }
        }

        return -1;
    }

    private static int LastIndexOfUnescapedQuote(string s)
    {
        for (var i = s.Length - 1; i >= 0; i--)
        {
            if ((s[i] == '"' || s[i] == '\'') && !IsEscaped(s, i))
            {
                return i;
            }
        }

        return -1;
    }

    private static List<string> SplitByCommaRespectingQuotes(string text)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var activeQuote = '\0';

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (IsEscaped(text, i))
            {
                current.Append(c);
                continue;
            }

            if (activeQuote != '\0')
            {
                if (c == activeQuote)
                {
                    activeQuote = '\0';
                }
                current.Append(c);
            }
            else if (c == '"' || c == '\'')
            {
                activeQuote = c;
                current.Append(c);
            }
            else if (c == ',' && activeQuote == '\0')
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0 || result.Count > 0)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    private static string UnquoteToken(string token)
    {
        var trimmed = token.Trim();
        if (IsQuoted(trimmed))
        {
            return UnescapeString(trimmed[1..^1]);
        }

        var firstQuoteIndex = IndexOfUnescapedQuote(trimmed);
        var lastQuoteIndex = LastIndexOfUnescapedQuote(trimmed);
        if (firstQuoteIndex > 0 && lastQuoteIndex > firstQuoteIndex)
        {
            var prefix = trimmed[..firstQuoteIndex];
            var inner = trimmed[(firstQuoteIndex + 1)..lastQuoteIndex];
            var suffix = trimmed[(lastQuoteIndex + 1)..];
            return prefix + UnescapeString(inner) + suffix;
        }

        return UnescapeString(trimmed);
    }

    private static string UnescapeString(string s)
    {
        if (!s.Contains('\\'))
        {
            return s;
        }

        var sb = new System.Text.StringBuilder(s.Length);
        var escaped = false;

        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (escaped)
            {
                sb.Append(c);
                escaped = false;
            }
            else if (c == '\\')
            {
                escaped = true;
            }
            else
            {
                sb.Append(c);
            }
        }

        if (escaped)
        {
            sb.Append('\\');
        }

        return sb.ToString();
    }

    // Strips a leading "*" -- the marker that opts one search out of the user's own exclusion rules
    // (see SearchService.SearchStreamingAsync's bypassExclusions parameter). Callers must run this
    // BEFORE the query is used for anything else (the actual search call, AND whatever gets stored as
    // an AppSearchResult's SearchQuery for highlighting) -- the character itself is never part of the
    // match/highlight text, only a query-string-level signal.
    public static string StripExclusionBypass(string query, out bool bypassExclusions)
    {
        bypassExclusions = query.Length > 0 && query[0] == '*';
        return bypassExclusions ? query[1..] : query;
    }
}
