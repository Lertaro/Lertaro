namespace Lertaro.Core.SearchIndex.Fzf;

// Alias-fallback quality-gating (IsAcceptableAliasMatch/WeightAliasMatch and their private helpers)
// lives in FzfPatternAliasMatchExtensions.cs (extension methods, matching TreeBuilder's Checkpoint/Diff
// split and MenuBuilder's ContentExtensions split) instead of a partial class, to keep this file under
// the project's line limit. This file keeps pattern parsing (Parse/ParseText/ParseTermSets) and the core
// text-matching algorithm (TryMatch/TryMatchSingle).
internal sealed class FzfPattern
{
    private FzfPattern(string? targetDrive, FzfTermSet[] termSets)
    {
        TargetDrive = targetDrive;
        TermSets = termSets;
    }

    public string? TargetDrive { get; }
    public FzfTermSet[] TermSets { get; }
    public bool IsEmpty => TermSets.Length == 0;

    // How much text the user actually typed, which is what the alias-fallback quality gate scales its
    // thresholds against (see IsAcceptableAliasMatch). A term set holds ALTERNATIVES -- one OR branch,
    // or one of the spellings an alias provider offers for the same term -- so only one of them can
    // ever be what was typed, and only one is counted.
    //
    // Summing them instead made the gate reject genuine matches as soon as a term had several
    // alternatives: "jiating" expands to six pinyin readings, which inflated the length from 7 to 64
    // and pushed the required score past anything a real match scores, so 家庭... stopped being found
    // while the shorter "jiatin" (four readings) still squeaked through.
    public int GetTotalTermLength()
    {
        var len = 0;
        foreach (var set in TermSets)
        {
            foreach (var term in set.Terms)
            {
                if (term.Inverse)
                    continue;
                len += term.Text.Length;
                break; // the rest of this set are alternative spellings of the same typed text
            }
        }
        return len;
    }

    public static FzfPattern Parse(string query)
    {
        string? targetDrive = null;
        var terms = new List<string>();
        foreach (var rawTerm in query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (rawTerm.Length >= 2 && char.IsLetter(rawTerm[0]) && rawTerm[1] == Path.VolumeSeparatorChar)
            {
                targetDrive = rawTerm[0].ToString();

                // Only the "d:" itself is the filter. Whatever follows it is still text the user typed
                // and meant to search for, so it stays a term -- dropping the whole token made "d:report"
                // quietly search drive D for nothing at all while "d: report" searched it for report,
                // with no way to tell from the results why the two differed. A file name can never
                // contain a colon, so a token in this shape has no other reading to preserve.
                var rest = rawTerm.Substring(2);
                if (rest.Length > 0)
                    terms.Add(rest);
                continue;
            }

            terms.Add(rawTerm);
        }

        return new FzfPattern(targetDrive, ParseTermSets(string.Join(' ', terms)));
    }

    public static FzfPattern ParseText(string query) => new FzfPattern(null, ParseTermSets(query));

    // Offers each alias provider the chance to restate this term in the shape its own aliases use, and
    // adds whatever comes back as ALTERNATIVES within the same term set (an OR): the candidate decides
    // which spelling it satisfies, and TryMatchSingle already stops at the first that hits.
    //
    // This one seam is what keeps every alias-matching site -- the index scan, path segments, display
    // highlighting, the plugin-facing FuzzyMatchService -- working without any of them knowing that a
    // provider's aliases have internal structure. It is also why "syllable" appears nowhere in Core:
    // the provider is asked for strings, not asked about its writing system.
    //
    // Skipped for an inverse term. "!x" means "reject anything matching x", and an OR set is satisfied
    // by ANY alternative, so adding spellings there would widen what gets excluded rather than what
    // gets found -- the opposite of the intent.
    private static void AddAliasQueryForms(List<FzfTerm> current, string lower, FzfTermKind kind, bool inverse, bool caseSensitive)
    {
        if (inverse || caseSensitive || lower.Length == 0)
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
                continue; // a misbehaving provider must not take the whole query down
            }

            foreach (var form in forms)
            {
                if (!string.IsNullOrEmpty(form) && form != lower)
                    current.Add(new FzfTerm(kind, false, form, false, AliasForm: true));
            }
        }
    }

    // One already-parsed term set lifted into a pattern of its own, so a caller can ask "which
    // candidates satisfy THIS term" instead of only "which satisfy the whole query". Reuses the parsed
    // term verbatim rather than re-parsing its text, which would have to re-derive kind/case-sensitivity
    // from a string the operators were already stripped from.
    internal static FzfPattern ForTermSet(FzfPattern source, int index)
        => new(source.TargetDrive, new[] { source.TermSets[index] });

    public bool TryMatch(ReadOnlySpan<char> text, out FzfPatternResult result, FzfScoringScheme scheme, FzfSlab? slab = null)
    {
        if (text.Contains('|'))
        {
            // ponytail: handle polyphonic aliases by matching each segment independently to prevent
            // incorrect cross-boundary match failure. Slicing (not Substring) keeps this allocation-free.
            var bestResult = default(FzfPatternResult);
            var matchedAny = false;
            var start = 0;
            while (start < text.Length)
            {
                var len = text.Slice(start).IndexOf('|');
                if (len < 0)
                    len = text.Length - start;

                if (TryMatchSingle(text.Slice(start, len), out var segmentResult, scheme, slab))
                {
                    if (segmentResult.ValidOffsetFound)
                    {
                        segmentResult = new FzfPatternResult(
                            segmentResult.Score,
                            segmentResult.MinBegin + start,
                            segmentResult.MinEnd + start,
                            segmentResult.MaxEnd + start,
                            true
                        );
                    }

                    if (!matchedAny || segmentResult.Score > bestResult.Score)
                    {
                        bestResult = segmentResult;
                        matchedAny = true;
                    }
                }

                start += len + 1;
            }

            result = bestResult;
            return matchedAny;
        }

        return TryMatchSingle(text, out result, scheme, slab);
    }

    // Text never contains '|' here: the segmented branch above slices it away, and real file names
    // can't contain it (invalid in Windows paths) -- so no cross-'|' span check is needed.
    private bool TryMatchSingle(ReadOnlySpan<char> text, out FzfPatternResult result, FzfScoringScheme scheme, FzfSlab? slab = null)
    {
        var totalScore = 0;
        var minBegin = int.MaxValue;
        var minEnd = int.MaxValue;
        var maxEnd = 0;
        var validOffsetFound = false;

        foreach (var set in TermSets)
        {
            var matched = false;
            FzfMatchResult best = default;
            foreach (var term in set.Terms)
            {
                var current = FzfAlgorithm.Match(term.Kind, text, term.Text, term.CaseSensitive, scheme, slab);
                if (current.IsMatch)
                {
                    if (term.Inverse)
                    {
                        matched = false;
                        best = default;
                        break;
                    }

                    matched = true;
                    best = current;
                    break;
                }

                if (term.Inverse)
                {
                    matched = true;
                    best = new FzfMatchResult(0, 0, 0);
                }
            }

            if (!matched)
            {
                result = default;
                return false;
            }

            totalScore += best.Score;
            if (best.Start < best.End)
            {
                minBegin = Math.Min(minBegin, best.Start);
                minEnd = Math.Min(minEnd, best.End);
                maxEnd = Math.Max(maxEnd, best.End);
                validOffsetFound = true;
            }
        }

        result = new FzfPatternResult(totalScore, minBegin, minEnd, maxEnd, validOffsetFound);
        return true;
    }

    private static FzfTermSet[] ParseTermSets(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<FzfTermSet>();

        query = query.Replace("\\ ", "\t");
        var sets = new List<FzfTermSet>();
        var current = new List<FzfTerm>();
        var switchSet = false;
        var afterBar = false;

        foreach (var rawToken in MergeQuotedPhrases(query.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
        {
            var token = rawToken.Replace('\t', ' ');
            if (current.Count > 0 && !afterBar && token == "|")
            {
                switchSet = false;
                afterBar = true;
                continue;
            }

            afterBar = false;
            // Mirrors fzf's own --exact mode ("if !fuzzy { typ = termExact }" in its parseTerms):
            // with fuzzy matching switched off, a bare term must match as a contiguous substring
            // rather than a scattered subsequence.
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
                // "'" flips exactness rather than setting it, so it stays useful in both modes: it
                // makes a term exact while fuzzy matching is on, and hands one term back to fuzzy
                // matching while it is off (fzf's own "Flip exactness" branch does the same).
                // A trailing "$" already claimed the kind ("'foo$"). A suffix match is exact by
                // nature, so the "'" adds nothing there and must not overwrite Suffix -- doing so
                // silently discarded the end anchor the user explicitly typed.
                if (kind != FzfTermKind.Suffix)
                    kind = fuzzyEnabled && !inverse ? FzfTermKind.Exact : FzfTermKind.Fuzzy;
                token = token.Substring(1);
            }
            else if (token.StartsWith("^", StringComparison.Ordinal))
            {
                kind = kind == FzfTermKind.Suffix ? FzfTermKind.Equal : FzfTermKind.Prefix;
                token = token.Substring(1);
                // "^'abc": Prefix/Equal are already exact, so a "'" here is a redundant operator
                // rather than text. Left in, it searched for a literal apostrophe no name contains.
                if (token.StartsWith("'", StringComparison.Ordinal))
                    token = token.Substring(1);
            }

            if (token.Length == 0)
                continue;

            if (switchSet)
            {
                sets.Add(new FzfTermSet(current.ToArray()));
                current.Clear();
            }

            var lower = token.ToLowerInvariant();
            var caseSensitive = token != lower;
            current.Add(new FzfTerm(kind, inverse, caseSensitive ? token : lower, caseSensitive));
            AddAliasQueryForms(current, lower, kind, inverse, caseSensitive);
            switchSet = true;
        }

        if (current.Count > 0)
            sets.Add(new FzfTermSet(current.ToArray()));

        return sets.ToArray();
    }

    // Reassembles a quoted phrase whose content contains spaces ("'cad acb'"). Necessary because the
    // split above runs BEFORE any operator parsing: such a query otherwise became the two unrelated
    // terms Exact("cad") and Fuzzy("acb'"), the second searching for a literal apostrophe no real name
    // contains, so the whole query could never match anything -- which is what the documented
    // "'final report'" form actually did.
    //
    // Merging is deliberately gated on the quotes sitting at token BOUNDARIES: an opening quote that
    // starts a token (after an optional "!"), a closing quote that ends a later one. An apostrophe
    // mid-word therefore never opens a phrase, leaving an ordinary query like "don't stop" untouched.
    // The lookahead also stops at a bare "|", so an OR of two quoted terms ("'foo | 'bar'") keeps
    // parsing as an OR instead of collapsing into one phrase that swallows the separator.
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
                merged.Add(token); // unmatched opening quote: leave the old term-by-term reading alone
                continue;
            }

            merged.Add(string.Join(' ', tokens, i, close - i + 1));
            i = close;
        }
        return merged;
    }

    // Index of a phrase-opening "'" (0, or 1 when the token is negated with "!"), or -1 for none.
    private static int QuoteStartIndex(string token)
    {
        if (token.StartsWith("'", StringComparison.Ordinal))
            return 0;
        return token.Length > 1 && token[0] == '!' && token[1] == '\'' ? 1 : -1;
    }

    // "'read'" / "!'read'" already carry their own closing quote, so they need no lookahead.
    private static bool IsSelfClosingQuote(string token, int open)
        => token.Length > open + 2 && token.EndsWith("'", StringComparison.Ordinal);
}

internal readonly record struct FzfTermSet(FzfTerm[] Terms);
// AliasForm marks a spelling an alias provider supplied for a term the user typed, rather than
// something the user typed themselves. It exists so display highlighting can tell the two apart: a
// user-written OR ("a | b") highlights every branch that matches, but a provider's rewriting of one
// term is an internal detail whose text (pinyin, boundaries and all) appears nowhere in the candidate,
// and marking it lights up characters that have nothing to do with what was typed.
internal readonly record struct FzfTerm(FzfTermKind Kind, bool Inverse, string Text, bool CaseSensitive, bool AliasForm = false);
internal readonly record struct FzfPatternResult(int Score, int MinBegin, int MinEnd, int MaxEnd, bool ValidOffsetFound);
