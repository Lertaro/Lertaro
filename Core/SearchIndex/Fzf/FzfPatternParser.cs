namespace Lertaro.Core.SearchIndex.Fzf;

// Split out from FzfPattern to keep the pattern state/matching file under the repository's 300-line
// limit. This class owns parsing only and constructs the immutable FzfPattern through its internal
// constructor; matching remains on the pattern itself.
internal static class FzfPatternParser
{
    public static FzfPattern Parse(string query)
    {
        string? targetDrive = null;
        var terms = new List<string>();
        foreach (var rawTerm in query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (rawTerm.Length >= 2 && char.IsLetter(rawTerm[0]) && rawTerm[1] == Path.VolumeSeparatorChar)
            {
                targetDrive = rawTerm[0].ToString();
                // Only the drive spec is a filter; text after the colon remains a search term.
                var rest = rawTerm.Substring(2);
                if (rest.Length > 0)
                    terms.Add(rest);
                continue;
            }

            terms.Add(rawTerm);
        }

        return Build(targetDrive, string.Join(' ', terms));
    }

    public static FzfPattern ParseText(string query) => Build(null, query);

    // Decides which of the two precedence readings the query gets and materializes the matching shape.
    //
    // OR-first (SearchContext.AndFirstPrecedence == false, the historical reading) is "conjunction of
    // disjunctions" and is exactly what the flat TermSets has always represented, so it is built the
    // only way it ever was.
    //
    // AND-first is "disjunction of conjunctions", which the flat shape cannot express in general
    // ("report | summary 2024" means report OR (summary AND 2024), not (report OR summary) AND 2024).
    // It only needs the extra OrGroups shape when the query actually mixes '|' with spaces; a query of
    // bare spaces or bare pipes means the same thing under both readings, and stays on the flat fast
    // path that every consumer of FzfPattern already understands.
    private static FzfPattern Build(string? targetDrive, string query)
    {
        var sets = ParseTermSets(query);

        if (!SearchContext.AndFirstPrecedence)
            return new FzfPattern(targetDrive, sets);

        var groups = ParseAndGroups(query, out var sawSpace, out var sawPipe);
        return sawPipe && sawSpace
            ? new FzfPattern(targetDrive, sets, groups)
            : new FzfPattern(targetDrive, sets);
    }

    // OR-first shape: sets are ANDed, terms inside a set are OR alternatives. A '|' merges the terms
    // around it into the same set (the pipe binds tighter than the space).
    private static FzfTermSet[] ParseTermSets(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<FzfTermSet>();

        query = query.Replace("\\ ", "\t");
        var sets = new List<FzfTermSet>();
        var current = new List<FzfTerm>();
        var afterBar = false;

        foreach (var rawToken in MergeQuotedPhrases(query.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
        {
            var token = rawToken.Replace('\t', ' ');
            if (current.Count > 0 && !afterBar && token == "|")
            {
                afterBar = true;
                continue;
            }

            // A term that is not the pipe's right-hand operand opens a new set: the pipe to its left
            // (if any) already widened the current set as far as that pipe reaches.
            if (current.Count > 0 && !afterBar)
            {
                sets.Add(new FzfTermSet(current.ToArray()));
                current.Clear();
            }

            afterBar = false;
            AddToken(token, current);
        }

        if (current.Count > 0)
            sets.Add(new FzfTermSet(current.ToArray()));

        return sets.ToArray();
    }

    // AND-first shape: space-separated terms AND together into ONE group, and a '|' closes that group
    // and starts the next -- the space binds tighter than the pipe, so "report | summary 2024" is the
    // disjunction [report] OR [summary AND 2024]. Groups are the outer OR, terms inside a group the
    // inner AND, which is the exact mirror of the OR-first shape above where the two roles swap.
    private static FzfTermSet[] ParseAndGroups(string query, out bool sawSpace, out bool sawPipe)
    {
        sawSpace = false;
        sawPipe = false;
        var groups = new List<FzfTermSet>();
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<FzfTermSet>();

        query = query.Replace("\\ ", "\t");
        var current = new List<FzfTerm>();

        foreach (var rawToken in MergeQuotedPhrases(query.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
        {
            var token = rawToken.Replace('\t', ' ');
            if (token == "|")
            {
                sawPipe = true;
                if (current.Count > 0)
                {
                    groups.Add(new FzfTermSet(current.ToArray()));
                    current.Clear();
                }
                continue;
            }

            if (current.Count > 0)
                sawSpace = true;
            AddToken(token, current);
        }

        if (current.Count > 0)
            groups.Add(new FzfTermSet(current.ToArray()));

        return groups.ToArray();
    }

    // Turns one already-phrase-merged token into the FzfTerm(s) it denotes: operator prefixes
    // (inverse/exact/prefix/suffix/boundary) plus the alias spellings a provider offers for it.
    private static void AddToken(string token, List<FzfTerm> current)
    {
        var fuzzyEnabled = SearchContext.FuzzyMatchEnabled;
        var kind = fuzzyEnabled ? FzfTermKind.Fuzzy : FzfTermKind.Exact;
        var inverse = false;
        if (token.StartsWith("!", StringComparison.Ordinal))
        {
            inverse = true;
            kind = FzfTermKind.Exact;
            token = token.Substring(1);
        }

        if (token != "$" && token.EndsWith("$", StringComparison.Ordinal))
        {
            kind = FzfTermKind.Suffix;
            token = token.Substring(0, token.Length - 1);
        }

        if (token.Length > 2 && token.StartsWith("'", StringComparison.Ordinal) && token.EndsWith("'", StringComparison.Ordinal))
        {
            kind = FzfTermKind.ExactBoundary;
            token = token.Substring(1, token.Length - 2);
        }
        else if (token.StartsWith("'", StringComparison.Ordinal))
        {
            // The quote flips exactness, while a suffix anchor already owns the term kind.
            if (kind != FzfTermKind.Suffix)
                kind = fuzzyEnabled && !inverse ? FzfTermKind.Exact : FzfTermKind.Fuzzy;
            token = token.Substring(1);
        }
        else if (token.StartsWith("^", StringComparison.Ordinal))
        {
            kind = kind == FzfTermKind.Suffix ? FzfTermKind.Equal : FzfTermKind.Prefix;
            token = token.Substring(1);
            if (token.StartsWith("'", StringComparison.Ordinal))
                token = token.Substring(1);
        }

        if (token.Length == 0)
            return;

        // Matching is always case-insensitive: uppercase input no longer activates fzf smart case.
        var lower = token.ToLowerInvariant();
        current.Add(new FzfTerm(kind, inverse, lower, CaseSensitive: false));
        AddAliasQueryForms(current, lower, kind, inverse);
    }

    private static void AddAliasQueryForms(List<FzfTerm> current, string lower, FzfTermKind kind, bool inverse)
    {
        if (inverse || lower.Length == 0)
            return;

        foreach (var provider in AliasProviderRegistry.GetActiveProviders())
        {
            IEnumerable<string> forms;
            try
            {
                forms = provider.GetQueryForms(lower);
            }
            catch
            {
                continue;
            }

            foreach (var form in forms)
            {
                if (!string.IsNullOrEmpty(form) && form != lower)
                    current.Add(new FzfTerm(kind, false, form, false, AliasForm: true));
            }
        }
    }

    private static List<string> MergeQuotedPhrases(string[] tokens)
    {
        var merged = new List<string>(tokens.Length);
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            var open = QuoteStartIndex(token);
            if (open < 0 || IsSelfClosingQuote(token, open))
            {
                merged.Add(token);
                continue;
            }

            var close = -1;
            for (var j = i + 1; j < tokens.Length; j++)
            {
                if (tokens[j] == "|")
                    break;
                if (tokens[j].EndsWith("'", StringComparison.Ordinal))
                {
                    close = j;
                    break;
                }
            }

            if (close < 0)
            {
                merged.Add(token);
                continue;
            }

            merged.Add(string.Join(' ', tokens, i, close - i + 1));
            i = close;
        }
        return merged;
    }

    private static int QuoteStartIndex(string token)
    {
        if (token.StartsWith("'", StringComparison.Ordinal))
            return 0;
        return token.Length > 1 && token[0] == '!' && token[1] == '\'' ? 1 : -1;
    }

    private static bool IsSelfClosingQuote(string token, int open)
        => token.Length > open + 2 && token.EndsWith("'", StringComparison.Ordinal);
}
