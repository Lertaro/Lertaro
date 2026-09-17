namespace Lertaro.Core.SearchIndex.Fzf;

// The one query parser for every raw-string entry point on FuzzyMatcher's seam -- plugin catalogs, bookmark
// titles, the shell-menu filter, the settings search boxes. Nothing puts a search pipeline in front of those
// callers, so the string they hand in is the whole query, operators and all. That is why this is a separate,
// opt-in entry point instead of a mode of FzfPattern.Parse: the file-search pipeline strips its own operator
// syntax (the `\`/`<`/`>` tokens, the ':' exclusion, the '?' inversion) BEFORE a pattern is ever built, so a
// character that reaches this seam is a literal by the time it arrives.
//
// Every entry point on the seam must still share ONE parser. If IsMatch honoured a trigger while the
// highlight/rank paths read it as text, a caller could get "matched" together with an empty mask -- a row
// that lights nothing on the very characters the match was made of. What the search box also reads is
// therefore read through TermTriggers rather than re-implemented; the legacy operators below are this
// seam's alone.
//
// ponytail: five legacy shapes, no nesting, no escaping. Widen the grammar only for a caller that needs it.
internal static class RawQueryPatternParser
{
    // A query that cannot be parsed becomes the shared empty pattern, which every entry point already
    // treats as "no match".
    public static FzfPattern Parse(string query)
        => TryParse(query, out var fzf) ? fzf : FzfPatternShapeExtensions.Empty;

    private static bool TryParse(string pattern, out FzfPattern fzf)
    {
        fzf = FzfPatternShapeExtensions.Empty;
        var text = pattern.Trim();
        if (text.Length == 0)
            return false;

        var fuzzyEnabled = SearchContext.FuzzyMatchEnabled;
        var sets = new List<FzfTermSet>();
        foreach (var raw in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.Length == 0)
                continue;

            var inverse = false;
            // The precision trigger first, through the helper the search pipeline also uses: "?abc" has to
            // mean the same thing here as in the search box, or the catalog rows this seam filters would
            // vanish from a query the file list still honours. The legacy operators below may still override
            // the kind afterwards, exactly as they already do today.
            var (body, kind) = TermTriggers.ReadPrecision(raw);

            if (body.StartsWith("!", StringComparison.Ordinal))
            {
                inverse = true;
                kind = FzfTermKind.Exact;
                body = body[1..];
            }

            if (body != "$" && body.EndsWith("$", StringComparison.Ordinal))
            {
                kind = FzfTermKind.Suffix;
                body = body[..^1];
            }

            if (body.Length > 2 && body.StartsWith("'", StringComparison.Ordinal) && body.EndsWith("'", StringComparison.Ordinal))
            {
                kind = FzfTermKind.ExactBoundary;
                body = body[1..^1];
            }
            else if (body.StartsWith("'", StringComparison.Ordinal))
            {
                // The quote FLIPS exactness, and a suffix anchor already owns the kind.
                if (kind != FzfTermKind.Suffix)
                    kind = fuzzyEnabled && !inverse ? FzfTermKind.Exact : FzfTermKind.Fuzzy;
                body = body[1..];
            }
            else if (body.StartsWith("^", StringComparison.Ordinal))
            {
                kind = kind == FzfTermKind.Suffix ? FzfTermKind.Equal : FzfTermKind.Prefix;
                body = body[1..];
                if (body.StartsWith("'", StringComparison.Ordinal))
                    body = body[1..];
            }

            if (body.Length == 0)
                continue;

            // One set PER WORD: space-separated words stay a conjunction (each its own 1-term set) while a
            // word's alias spellings remain OR alternatives inside that word's set -- the same shape
            // FzfPatternParser builds, which is why the expansion is reused rather than re-derived. Note a
            // word typed in full pinyin also needs the provider's CJK query forms to match a Chinese name.
            var lower = body.ToLowerInvariant();
            var terms = new List<FzfTerm> { new(kind, inverse, lower, CaseSensitive: false) };
            if (!inverse)
                FzfPatternParser.AddAliasQueryForms(terms, lower, kind);
            sets.Add(new FzfTermSet(terms.ToArray()));
        }

        if (sets.Count == 0)
            return false;

        fzf = FzfPatternShapeExtensions.FromTermSets(sets.ToArray());
        return true;
    }
}
