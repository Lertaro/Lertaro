using Lertaro.Core.SearchIndex.Query;

namespace Lertaro.Core.SearchIndex.Fzf;

// Split out from FzfPattern to keep the pattern state/matching file under the repository's 300-line
// limit. This class owns parsing only and constructs the immutable FzfPattern through its internal
// constructor; matching remains on the pattern itself.
internal static class FzfPatternParser
{
    public static FzfPattern Parse(string query)
    {
        var text = RegexQueryParser.Split(query, out var regexes);
        return ParseCore(text, regexes);
    }

    internal static FzfPattern Parse(string query, string[]? regexes)
    {
        var text = RegexQueryParser.Split(query, out _);
        return ParseCore(text, regexes);
    }

    private static FzfPattern ParseCore(string text, string[]? regexes)
    {
        string? targetDrive = null;
        var terms = new List<string>();
        foreach (var rawTerm in text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            // A drive spec is exactly "X:" -- one ASCII letter followed by the half-width colon, and
            // nothing else. "d:report" remains literal text; only the bare "d:" token selects a drive.
            if (rawTerm.Length == 2 && IsAsciiLetter(rawTerm[0]) && rawTerm[1] == Path.VolumeSeparatorChar)
            {
                targetDrive = rawTerm[0].ToString();
                continue;
            }

            terms.Add(rawTerm);
        }

        return Build(targetDrive, string.Join(' ', terms), regexes);
    }

    private static bool IsAsciiLetter(char c) => (uint)(c | 0x20) - 'a' <= 'z' - 'a';

    // ParseText is the no-drive entry point: it still honours regex clauses, since those are a property
    // of the query text rather than of the drive it targets.
    public static FzfPattern ParseText(string query)
    {
        var text = RegexQueryParser.Split(query, out var regexes);
        return Build(null, text, regexes);
    }

    internal static FzfPattern ParseText(string query, string[]? regexes)
        => Build(null, query, regexes);

    // Decides which of the two precedence readings the query gets and materializes the matching shape.
    //
    // A regex clause carries no ordinary terms of its own -- RegexQueryParser stripped its "/" delimiters
    // and took the pattern out of the query -- so a regex-only query reaches this with an empty term
    // string and is handled as the pattern's regex-only case.
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
    private static FzfPattern Build(string? targetDrive, string query, string[]? regexes)
    {
        var sets = ParseTermSets(query);

        if (!SearchContext.AndFirstPrecedence)
            return new FzfPattern(targetDrive, sets, null, regexes);

        var groups = ParseAndGroups(query, out var sawSpace, out var sawPipe);
        return sawPipe && sawSpace
            ? new FzfPattern(targetDrive, sets, groups, regexes)
            : new FzfPattern(targetDrive, sets, null, regexes);
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
    private static FzfTermGroup[] ParseAndGroups(string query, out bool sawSpace, out bool sawPipe)
    {
        sawSpace = false;
        sawPipe = false;
        var groups = new List<FzfTermGroup>();
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<FzfTermGroup>();

        query = query.Replace("\\ ", "\t");
        var currentGroup = new List<FzfTermSet>();
        var currentSet = new List<FzfTerm>();

        foreach (var rawToken in MergeQuotedPhrases(query.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
        {
            var token = rawToken.Replace('\t', ' ');
            if (token == "|")
            {
                sawPipe = true;
                if (currentSet.Count > 0)
                {
                    currentGroup.Add(new FzfTermSet(currentSet.ToArray()));
                    currentSet.Clear();
                }
                if (currentGroup.Count > 0)
                {
                    groups.Add(new FzfTermGroup(currentGroup.ToArray()));
                    currentGroup.Clear();
                }
                continue;
            }

            if (currentSet.Count > 0)
            {
                sawSpace = true;
                currentGroup.Add(new FzfTermSet(currentSet.ToArray()));
                currentSet.Clear();
            }
            AddToken(token, currentSet);
        }

        if (currentSet.Count > 0)
            currentGroup.Add(new FzfTermSet(currentSet.ToArray()));
        if (currentGroup.Count > 0)
            groups.Add(new FzfTermGroup(currentGroup.ToArray()));

        return groups.ToArray();
    }

    // Turns one already-phrase-merged token into the FzfTerm(s) it denotes: whatever TermTriggers reads from
    // its first character (a ':' exclusion, a '?' precision inversion, or neither), plus the alias
    // spellings a provider offers for it.
    //
    // The historical operator prefixes are gone -- '!' "'" '^' '$' no longer mean anything and are searched
    // as literal characters. What is left is otherwise handled before this point: a bare "d:" drive spec is
    // split off in Parse, and the query-token scanner deliberately leaves ':' alone (see QueryTokenScanner),
    // so a colon still leading a word here is a user-written exclusion.
    private static void AddToken(string token, List<FzfTerm> current)
    {
        var (body, kind, inverse) = TermTriggers.Read(token);

        // A lone ":" or "?" is not an operator on the empty string -- drop the word entirely, so a stray
        // trigger ("report :") leaves the rest of the query untouched instead of adding a term that can
        // never match.
        if (body.Length == 0)
            return;

        // Matching is always case-insensitive: uppercase input no longer activates fzf smart case.
        var positive = body.ToLowerInvariant();
        current.Add(new FzfTerm(kind, inverse, positive, CaseSensitive: false));

        // No alias expansion for an exclusion: a pinyin spelling must not be able to exclude a file the
        // user never named (mirrors the old '!', which skipped the same expansion).
        if (!inverse)
            AddAliasQueryForms(current, positive, kind);
    }

    // The provider-supplied spellings of one typed word, added to that word's own term set as OR alternatives.
    //
    // A word typed as one run of letters is not present verbatim in an alias that marks syllable boundaries
    // ("wangfei" against "wang\u0002fei"), so without these forms a pinyin query could only reach a CJK name
    // through the fuzzy reading -- an exact or '?'-inverted term would find nothing at all. Only the term's
    // own words get them: the exclusion path skips this call, because a pinyin spelling must not be able to
    // exclude a file the user never named.
    internal static void AddAliasQueryForms(List<FzfTerm> current, string lower, FzfTermKind kind)
    {
        if (lower.Length == 0)
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

    // A word that OPENS a quoted phrase: the "'..." form. The quote characters are ordinary text now (see
    // TermTriggers), but the merger still has to keep a quoted phrase as ONE token -- without it "'final
    // report'" would become two ANDed words and match names the documentation says it cannot match (it shows
    // that query finding nothing, because no file name contains an apostrophe). A leading '!' is not an
    // operator any more, so "!'a b'" is two words rather than a quoted phrase.
    private static int QuoteStartIndex(string token)
        => token.StartsWith("'", StringComparison.Ordinal) ? 0 : -1;

    private static bool IsSelfClosingQuote(string token, int open)
        => token.Length > open + 2 && token.EndsWith("'", StringComparison.Ordinal);
}
