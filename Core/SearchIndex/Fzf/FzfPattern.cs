namespace Lertaro.Core.SearchIndex.Fzf;

// Alias-fallback quality-gating (IsAcceptableAliasMatch/WeightAliasMatch and their private helpers)
// lives in FzfPatternAliasMatchExtensions.cs (extension methods, matching TreeBuilder's Checkpoint/Diff
// split and MenuBuilder's ContentExtensions split) instead of a partial class, to keep this file under
// the project's line limit. Pattern parsing is delegated to FzfPatternParser for the same reason; this
// file keeps the immutable pattern state and core text-matching algorithm.
internal sealed class FzfPattern
{
    internal FzfPattern(string? targetDrive, FzfTermSet[] termSets)
    {
        TargetDrive = targetDrive;
        TermSets = termSets;
    }

    public string? TargetDrive { get; }
    public FzfTermSet[] TermSets { get; }
    public bool IsEmpty => TermSets.Length == 0;

    // True when every term was matched as a PRECISE run rather than as a scattered subsequence -- the
    // ordinary "fuzzy matching is switched off" query, and also an all-explicit-operator one. Alias
    // fallback then has to respect the provider's syllable boundaries (see AliasMatchRules), which is what
    // stops "ex" being read as the tail of "xue" plus the head of "xi".
    //
    // Keyed off the terms rather than SearchContext.FuzzyMatchEnabled so a term that explicitly flips
    // itself back to a subsequence ("'jtqin", under fuzzy-off) stays exempt: for that term the user did ask
    // for a loose match, and applying the boundary rule would contradict what the operator means.
    public bool RequiresAlignedAliases
    {
        get
        {
            foreach (var set in TermSets)
            {
                foreach (var term in set.Terms)
                {
                    if (term.Inverse)
                        continue;
                    if (term.Kind == FzfTermKind.Fuzzy)
                        return false;
                }
            }
            return true;
        }
    }

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

    public static FzfPattern Parse(string query) => FzfPatternParser.Parse(query);

    public static FzfPattern ParseText(string query) => FzfPatternParser.ParseText(query);

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

}

internal readonly record struct FzfTermSet(FzfTerm[] Terms);
// AliasForm marks a spelling an alias provider supplied for a term the user typed, rather than
// something the user typed themselves. It exists so display highlighting can tell the two apart: a
// user-written OR ("a | b") highlights every branch that matches, but a provider's rewriting of one
// term is an internal detail whose text (pinyin, boundaries and all) appears nowhere in the candidate,
// and marking it lights up characters that have nothing to do with what was typed.
internal readonly record struct FzfTerm(FzfTermKind Kind, bool Inverse, string Text, bool CaseSensitive, bool AliasForm = false);
internal readonly record struct FzfPatternResult(int Score, int MinBegin, int MinEnd, int MaxEnd, bool ValidOffsetFound);
