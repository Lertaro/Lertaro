namespace Lertaro.Core.SearchIndex.Query;

// Splits a query into the ordinary text part and the "/.../" regex clauses it contains.
//
// This runs BEFORE FzfPatternParser for a reason: a regex like "^ab\.c\..{3}$" contains backslashes and
// slashes, and Lertaro's path-mode detection switches the whole query to full-path matching the moment
// it sees one of those. Having the regex swallowed here means path mode never fires for it.
//
// SearchQueryParser.Parse calls this too, for that exact reason: it is the caller that MAKES the path-mode
// decision, so it has to see the query without its regex clauses. Before it did, "lertaro /\.exe$/" was
// read as a full path named "lertaro /\.exe$" and matched nothing at all.
//
// A clause is a whole WORD that starts AND ends with '/': "/\.exe$/", "/^report.*\.md$/". Both ends are
// required because '/' is also Windows' alternate path separator, so a query full of slashes is far more
// likely to be a path than a pattern. Three shapes therefore stay ordinary text, and each is the shape a
// path actually takes:
//
//   C:/Users/me      the opening '/' is not at a word start (that is what IsAtTokenBoundary keeps out)
//   /mnt/c/Users     the word does not END with '/'
//   /usr/local/      an unescaped '/' inside the body
//
// The last one is the delimiter convention every /.../-delimited regex uses (sed, JavaScript literals):
// a '/' the pattern must match literally is written "\/", which is passed to the engine verbatim. So
// "/a\/b/" is the clause "a\/b" while "/usr/local/" stays a path.
//
// Only the clause PATTERNS come out of here. The required literal the index prefilter needs is extracted
// once per pattern by FzfPattern -- the single consumer (see FzfPattern.ComputeRequiredRegexLiteral).
// Extracting one here as well was dead work: the value was carried out in the clause and dropped again on
// the way to the pattern, which then recomputed it from the same text.
public static class RegexQueryParser
{
    private const char Delimiter = '/';

    // Returns the query with every regex clause removed, plus the clause patterns themselves (null when
    // there were none). The clauses are ANDed with whatever text is left, matching the documented
    // "<regex> <word>" behaviour.
    public static string Split(string query, out string[]? patterns)
    {
        patterns = null;
        if (string.IsNullOrEmpty(query) || !query.Contains(Delimiter))
            return query;

        List<string>? found = null;
        var remaining = new System.Text.StringBuilder(query.Length);

        var index = 0;
        while (index < query.Length)
        {
            var start = query.IndexOf(Delimiter, index);
            if (start < 0 || !IsAtTokenBoundary(query, start))
            {
                remaining.Append(query, index, query.Length - index);
                break;
            }

            if (!TryReadClause(query, start, out var pattern, out var end))
            {
                // A '/' that does not open a well-formed clause -- keep it as ordinary text rather than
                // silently swallowing part of the query.
                remaining.Append(query, index, start + 1 - index);
                index = start + 1;
                continue;
            }

            remaining.Append(query, index, start - index);
            (found ??= new List<string>()).Add(pattern);
            remaining.Append(' ');
            index = end;
        }

        patterns = found?.ToArray();
        return remaining.ToString().Trim();
    }

    // The clause must start its own token, so "a/x/" stays literal text -- a '/' in the middle of a word is
    // a path separator, never a delimiter.
    private static bool IsAtTokenBoundary(string query, int start)
        => start == 0 || char.IsWhiteSpace(query[start - 1]);

    // Reads the "/.../" clause starting at its opening delimiter, which the caller has already checked sits
    // at a token boundary. False for anything that is not a whole "/.../" word, which leaves the caller to
    // treat it as text -- see the class comment for the three path shapes that rely on that.
    private static bool TryReadClause(string query, int start, out string pattern, out int end)
    {
        pattern = string.Empty;
        end = start;

        var wordEnd = FindWordEnd(query, start);
        // Three characters is the shortest clause there is ("/a/"): the body must not be empty, so "//" is
        // a UNC path's opening, not an empty pattern that would match everything.
        if (wordEnd - start < 3 || query[wordEnd - 1] != Delimiter)
            return false;

        var builder = new System.Text.StringBuilder(wordEnd - start - 2);
        for (var i = start + 1; i < wordEnd - 1; i++)
        {
            var c = query[i];
            if (c == Delimiter)
                return false;

            // A backslash escapes the next character, so "\/" is a literal delimiter inside the pattern and
            // every other escape belongs to the regex engine. Both are carried through verbatim.
            if (c == '\\' && i + 1 < wordEnd - 1)
            {
                builder.Append(c).Append(query[i + 1]);
                i++;
                continue;
            }

            builder.Append(c);
        }

        pattern = builder.ToString();
        end = wordEnd;
        return true;
    }

    // The end of the word the clause opens, so the closing delimiter can be required to be its last
    // character. An escaped space is text rather than a separator, the same convention QueryTokenScanner
    // applies when it splits a query into words.
    private static int FindWordEnd(string query, int start)
    {
        for (var i = start; i < query.Length; i++)
        {
            if (char.IsWhiteSpace(query[i]) && !IsEscaped(query, i))
                return i;
        }

        return query.Length;
    }

    private static bool IsEscaped(string text, int index)
    {
        var backslashCount = 0;
        for (var i = index - 1; i >= 0 && text[i] == '\\'; i--)
            backslashCount++;

        return backslashCount % 2 != 0;
    }
}
