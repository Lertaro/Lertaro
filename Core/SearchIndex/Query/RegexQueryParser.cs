namespace Lertaro.Core.SearchIndex.Query;

// Splits a query into the ordinary text part and the "regex:/.../" clauses it contains.
//
// This runs BEFORE FzfPatternParser for a reason: a regex like "^ab\.c\..{3}$" contains backslashes and
// slashes, and Lertaro's path-mode detection switches the whole query to full-path matching the moment
// it sees one of those. Having the regex swallowed here means path mode never fires for it.
//
// SearchQueryParser.Parse calls this too, for that exact reason: it is the caller that MAKES the path-mode
// decision, so it has to see the query without its regex clauses. Before it did, "lertaro regex:/\.exe$/"
// was read as a full path named "lertaro regex:\.exe$" and matched nothing at all.
//
// Only the clause PATTERNS come out of here. The required literal the index prefilter needs is extracted
// once per pattern by FzfPattern -- the single consumer (see FzfPattern.ComputeRequiredRegexLiteral).
// Extracting one here as well was dead work: the value was carried out in the clause and dropped again on
// the way to the pattern, which then recomputed it from the same text.
public static class RegexQueryParser
{
    private const string Prefix = "regex:";

    // Returns the query with every regex clause removed, plus the clause patterns themselves (null when
    // there were none). The clauses are ANDed with whatever text is left, matching the documented
    // "<regex> <word>" behaviour.
    public static string Split(string query, out string[]? patterns)
    {
        patterns = null;
        if (string.IsNullOrEmpty(query) || !query.Contains(Prefix, StringComparison.Ordinal))
            return query;

        List<string>? found = null;
        var remaining = new System.Text.StringBuilder(query.Length);

        var index = 0;
        while (index < query.Length)
        {
            var start = query.IndexOf(Prefix, index, StringComparison.Ordinal);
            if (start < 0 || !IsAtTokenBoundary(query, start))
            {
                remaining.Append(query, index, query.Length - index);
                break;
            }

            remaining.Append(query, index, start - index);

            var bodyStart = start + Prefix.Length;
            if (!TryReadDelimited(query, bodyStart, out var pattern, out var end))
            {
                // "regex:" without a well-formed /.../ body -- keep it as ordinary text rather than
                // silently swallowing the rest of the query.
                remaining.Append(Prefix);
                index = bodyStart;
                continue;
            }

            (found ??= new List<string>()).Add(pattern);
            remaining.Append(' ');
            index = end;
        }

        patterns = found?.ToArray();
        return remaining.ToString().Trim();
    }

    // The clause must start its own token, so "aregex:/x/" stays literal text.
    private static bool IsAtTokenBoundary(string query, int start)
        => start == 0 || char.IsWhiteSpace(query[start - 1]);

    // Reads the "/.../" body. A backslash inside escapes the next character, so "/a\/b/" is one clause.
    private static bool TryReadDelimited(string query, int bodyStart, out string pattern, out int end)
    {
        pattern = string.Empty;
        end = bodyStart;
        if (bodyStart >= query.Length || query[bodyStart] != '/')
            return false;

        var builder = new System.Text.StringBuilder();
        for (var i = bodyStart + 1; i < query.Length; i++)
        {
            var c = query[i];
            if (c == '\\' && i + 1 < query.Length)
            {
                builder.Append(c).Append(query[i + 1]);
                i++;
                continue;
            }

            if (c == '/')
            {
                pattern = builder.ToString();
                end = i + 1;
                return true;
            }

            builder.Append(c);
        }

        return false;
    }
}
