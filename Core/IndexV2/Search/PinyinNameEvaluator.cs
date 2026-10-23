using System.Runtime.CompilerServices;
using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Core.IndexV2.Search;

// Decides one query against one candidate NAME with the WinShun-style live matcher: every plain
// positive term is tried literally first (the user's own precision) and, when that fails, against a
// live provider's reading NFA (each character consumes any non-empty prefix of one of its readings;
// see IAliasLiveMatcherProvider). What matched literally keeps fzf's own score; only what had to be read
// is scored by the reference's additive table (pinyin -12-if-rare + 26/16/4,
// whole-name-spelled-out 40, coverage 25*n/len), so the
// frequency signal reaches the number the scan ranks by while a search no reading was needed for is
// ranked exactly as it was before matching went live. A term the readings CAN decide is matched literally
// only as a run, even with fuzzy matching on: letting it scatter across the name's own letters is what
// made one pinyin query answer differently in the two modes.
//
// The old alias-fallback quality gate is not re-added here. Its span half is vacuous now -- every
// character of a walk consumes at least one letter, so a matched run is never wider than the word -- and
// its score half cannot be expressed in this table: a mid-name abbreviation is worth 4 points whether it
// is 截图's own "jietu" or a coincidence, so any floor above 4 deletes genuine hits. Coincidental reads
// are demoted instead, by the tier and position keys the windows already rank on.
//
// This replaces FzfPattern.TryMatch as the name tier wherever a live provider is registered: the
// result is still an FzfPatternResult, so FzfResultRank and every window consume it unchanged. The
// term/set semantics are preserved exactly -- within a set the first satisfying alternative wins
// (alias forms after the typed term), an inverse term vetoes by literal presence, an inverse-only
// set is satisfied by absence, and an AND-first query needs any one of its OR groups.
//
// Without any live provider (or when nothing in the pattern is an eligible word) this declines --
// HasProviders is false and TryScore returns false -- so callers keep their existing name tier and
// nothing about pure-literal search changes.
internal sealed class PinyinNameEvaluator
{
    private static readonly ConditionalWeakTable<FzfPattern, PinyinNameEvaluator> Cache = new();

    private readonly FzfPattern _pattern;
    private readonly Dictionary<string, int> _wordIndex;
    private readonly ProviderTable[] _providers;
    private readonly FzfTerm? _loneWord; // the single plain positive term, when there is exactly one
    private readonly bool[] _eligible; // per word: some provider can actually decide it by reading
    private readonly string? _joinedTerm;

    private sealed class ProviderTable
    {
        public required byte ProviderId { get; init; }
        public required string ProviderName { get; init; }
        public required IAliasTermMatcher?[] Full { get; init; }
        public required IAliasTermMatcher?[] Common { get; init; }
        public IAliasTermMatcher? JoinedFull { get; init; }
        public IAliasTermMatcher? JoinedCommon { get; init; }
    }

    internal readonly record struct PinyinMatchWitness(
        bool IsJoined,
        byte ProviderId,
        string? ProviderName,
        int Start,
        int End,
        int Tier);

    private readonly record struct BranchStats(bool ReadingOnly, int Covered);

    private PinyinNameEvaluator(FzfPattern pattern, Dictionary<string, int> wordIndex,
        ProviderTable[] providers, FzfTerm? loneWord, bool[] eligible, string? joinedTerm)
    {
        _pattern = pattern;
        _wordIndex = wordIndex;
        _providers = providers;
        _loneWord = loneWord;
        _eligible = eligible;
        _joinedTerm = joinedTerm;
    }

    public bool HasProviders => _providers.Length != 0;
    internal bool HasJoinedBranch => _joinedTerm != null && _providers.Any(table => table.JoinedFull != null);

    public static PinyinNameEvaluator ForPattern(FzfPattern pattern)
        => Cache.GetValue(pattern, static p =>
            Build(p, AliasProviderRegistry.GetActiveProviders().OfType<IAliasLiveMatcherProvider>()));

    /// <summary>Builds an evaluator against an explicit provider list (tests); production uses ForPattern.</summary>
    internal static PinyinNameEvaluator Build(FzfPattern pattern, IEnumerable<IAliasLiveMatcherProvider> providers)
    {
        var words = new List<FzfTerm>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var positives = 0;
        var plainCount = 0;
        FzfTerm? firstPlain = null;
        foreach (var set in pattern.EffectiveSets)
        {
            foreach (var term in set.Terms)
            {
                if (term.Inverse || term.AliasForm || term.Text.Length == 0)
                    continue;
                positives++;
                if (term.Kind is FzfTermKind.Exact or FzfTermKind.Fuzzy)
                {
                    if (index.TryAdd(term.Text, words.Count))
                        words.Add(term);
                    plainCount++;
                    firstPlain ??= term;
                }
            }
        }

        // The whole-name bonuses belong to a query of exactly ONE plain word; a second term of any
        // kind (another word, an operator, an inverse) leaves them off.
        var lone = positives == 1 && plainCount == 1 ? firstPlain : null;
        var joinedTerm = TryBuildJoinedTerm(pattern);

        var list = words.ToArray();
        var tables = new List<ProviderTable>();
        foreach (var provider in providers)
        {
            var full = new IAliasTermMatcher?[list.Length];
            var common = new IAliasTermMatcher?[list.Length];
            var any = false;
            for (var i = 0; i < list.Length; i++)
            {
                try
                {
                    full[i] = provider.CreateMatcher(list[i].Text, false);
                    if (full[i] != null)
                        common[i] = provider.CreateMatcher(list[i].Text, true);
                }
                catch
                {
                    full[i] = null;
                    common[i] = null;
                }
                any |= full[i] != null;
            }
            IAliasTermMatcher? joinedFull = null;
            IAliasTermMatcher? joinedCommon = null;
            if (joinedTerm != null && full.All(matcher => matcher != null))
            {
                try
                {
                    joinedFull = provider.CreateMatcher(joinedTerm, false);
                    if (joinedFull != null)
                        joinedCommon = provider.CreateMatcher(joinedTerm, true);
                }
                catch
                {
                    joinedFull = null;
                    joinedCommon = null;
                }
            }

            if (any || joinedFull != null)
                tables.Add(new ProviderTable
                {
                    ProviderId = AliasProviderRegistry.GetProviderId(provider),
                    ProviderName = provider.GetType().Name,
                    Full = full,
                    Common = common,
                    JoinedFull = joinedFull,
                    JoinedCommon = joinedCommon,
                });
        }

        var eligible = new bool[list.Length];
        foreach (var table in tables)
            for (var i = 0; i < list.Length; i++)
                eligible[i] |= table.Full[i] != null;

        return new PinyinNameEvaluator(pattern, index, tables.ToArray(), lone, eligible, joinedTerm);
    }

    private static string? TryBuildJoinedTerm(FzfPattern pattern)
    {
        if (pattern.TargetDrive != null
            || pattern.OrGroups != null
            || pattern.HasExclusions
            || pattern.TermSets.Length < 2)
        {
            return null;
        }

        var plainKind = SearchContext.FuzzyMatchEnabled ? FzfTermKind.Fuzzy : FzfTermKind.Exact;
        var builder = new System.Text.StringBuilder(16);
        foreach (var set in pattern.TermSets)
        {
            FzfTerm? typed = null;
            foreach (var term in set.Terms)
            {
                if (term.AliasForm)
                    continue;
                if (typed != null
                    || term.Inverse
                    || term.Kind != plainKind
                    || term.Text.Length == 0)
                {
                    return null;
                }
                typed = term;
            }

            if (typed is not { } word)
                return null;
            foreach (var c in word.Text)
            {
                if (c is not (>= 'a' and <= 'z') and not (>= 'A' and <= 'Z'))
                    return null;
                builder.Append(char.ToLowerInvariant(c));
                if (builder.Length > 63)
                    return null;
            }
        }

        return builder.Length >= 2 ? builder.ToString() : null;
    }

    /// <summary>
    /// Decides the whole query against <paramref name="name"/>. On true the match carries the score and
    /// the span, ready for <c>FzfResultRank.ForDefaultScheme</c>.
    /// </summary>
    public bool TryScore(ReadOnlySpan<char> name, FzfSlab? slab, out FzfPatternResult match) =>
        TryScore(name, slab, out match, out _);

    internal bool TryScore(
        ReadOnlySpan<char> name,
        FzfSlab? slab,
        out FzfPatternResult match,
        out PinyinMatchWitness witness)
    {
        match = default;
        witness = default;
        if (name.IsEmpty || !HasProviders)
            return false;

        if (name.IndexOf('|') < 0)
            return ScoreWhole(name, slab, out match, out witness);

        // A '|' inside the text is a reading separator to every alias tier, so a candidate that carries
        // one for real -- a browser tab title like "example.com | 代理" -- is decided one segment at a
        // time, exactly as FzfPattern.TryMatch does, and never across the boundary.
        var best = default(FzfPatternResult);
        var bestWitness = default(PinyinMatchWitness);
        var matched = false;
        var start = 0;
        while (start < name.Length)
        {
            var len = name.Slice(start).IndexOf('|');
            if (len < 0)
                len = name.Length - start;
            if (ScoreWhole(name.Slice(start, len), slab, out var segment, out var segmentWitness))
            {
                if (segment.ValidOffsetFound)
                    segment = new FzfPatternResult(segment.Score, segment.MinBegin + start,
                        segment.MinEnd + start, segment.MaxEnd + start, true);
                if (segmentWitness.IsJoined)
                    segmentWitness = segmentWitness with
                    {
                        Start = segmentWitness.Start + start,
                        End = segmentWitness.End + start,
                    };
                if (!matched || segment.Score > best.Score)
                {
                    best = segment;
                    bestWitness = segmentWitness;
                    matched = true;
                }
            }
            start += len + 1;
        }

        match = best;
        witness = bestWitness;
        return matched;
    }

    internal bool TryPaintJoined(ReadOnlySpan<char> name, Span<bool> painted, out PinyinMatchWitness witness)
    {
        witness = default;
        if (!TryScore(name, null, out _, out var accepted) || !accepted.IsJoined)
            return false;
        for (var i = accepted.Start; i < accepted.End && i < painted.Length; i++)
            painted[i] = true;
        witness = accepted;
        return true;
    }

    private bool ScoreWhole(
        ReadOnlySpan<char> name,
        FzfSlab? slab,
        out FzfPatternResult match,
        out PinyinMatchWitness witness)
    {
        match = default;
        witness = default;
        var disabled = SearchContext.DisabledAliasIds;
        var originalMatched = false;
        var originalStats = default(BranchStats);
        if (_pattern.OrGroups != null)
        {
            foreach (var group in _pattern.OrGroups)
            {
                if (!ScoreSets(group.Sets, name, slab, disabled, out match, out originalStats))
                    continue;
                originalMatched = true;
                break;
            }
        }
        else
        {
            originalMatched = ScoreSets(
                _pattern.TermSets, name, slab, disabled, out match, out originalStats);
        }

        if (TryScoreJoined(name, disabled, out var joined, out var joinedWitness)
            && JoinedWins(name, originalMatched, match, originalStats, joined))
        {
            match = joined;
            witness = joinedWitness;
            return true;
        }
        return originalMatched;
    }

    private static bool JoinedWins(
        ReadOnlySpan<char> name,
        bool originalMatched,
        in FzfPatternResult original,
        in BranchStats originalStats,
        in FzfPatternResult joined)
    {
        if (!originalMatched || joined.Score > original.Score)
            return true;
        if (!originalStats.ReadingOnly)
            return false;

        // Per-term reading bonuses are additive, so merely inserting more spaces can make the original
        // branch's raw score larger even when both branches describe exactly the same source span. Those
        // numbers are not comparable across tokenizations. Canonicalize to the joined witness only when
        // it starts no later and explains at least as much of the stem; otherwise the ordinary multi-word
        // evidence remains stronger and keeps its existing result.
        var stem = StemLength(name);
        var joinedCoverage = Math.Min(joined.MaxEnd - joined.MinBegin, stem);
        var originalCoverage = Math.Min(originalStats.Covered, stem);
        return joined.MinBegin <= original.MinBegin && joinedCoverage >= originalCoverage;
    }

    private bool TryScoreJoined(
        ReadOnlySpan<char> name,
        HashSet<byte>? disabled,
        out FzfPatternResult match,
        out PinyinMatchWitness witness)
    {
        match = default;
        witness = default;
        if (_joinedTerm == null)
            return false;

        var found = false;
        foreach (var table in _providers)
        {
            if (disabled != null && disabled.Contains(table.ProviderId))
                continue;
            if (table.JoinedFull == null
                || !table.JoinedFull.TryFind(name, default, out var start, out var end))
            {
                continue;
            }

            var common = table.JoinedCommon != null
                && table.JoinedCommon.TryFind(name, default, out _, out _);
            var stem = StemLength(name);
            var score = PinyinBonus(start, name, common)
                + 25 * Math.Min(end - start, stem) / stem;
            if (start == 0 && end - start >= stem)
                score += 40;
            var tier = !common
                ? MatchRank.TierFullRare
                : end - start == _joinedTerm.Length
                    ? MatchRank.TierInitials
                    : MatchRank.TierFull;
            if (found && (score < match.Score
                || score == match.Score && CompareJoinedTie(
                    table.ProviderId, table.ProviderName, tier, start, end, witness) >= 0))
            {
                continue;
            }

            match = new FzfPatternResult(score, start, end, end, true);
            witness = new PinyinMatchWitness(true, table.ProviderId, table.ProviderName, start, end, tier);
            found = true;
        }
        return found;
    }

    private static int CompareJoinedTie(
        byte providerId,
        string providerName,
        int tier,
        int start,
        int end,
        in PinyinMatchWitness accepted)
    {
        var compare = start.CompareTo(accepted.Start);
        if (compare != 0)
            return compare;
        compare = (accepted.End - accepted.Start).CompareTo(end - start);
        if (compare != 0)
            return compare;
        compare = tier.CompareTo(accepted.Tier);
        if (compare != 0)
            return compare;
        compare = string.CompareOrdinal(providerName, accepted.ProviderName);
        return compare != 0 ? compare : providerId.CompareTo(accepted.ProviderId);
    }

    /// <summary>
    /// The pinyin half of HighlightMask's alias tier for ONE term: paints the matched span and reports
    /// the tier the match belongs to (initials / full / rare reading), or false when this term has no
    /// live match. Literal painting stays with the caller's own literal pass.
    /// </summary>
    public bool TryPaintTerm(ReadOnlySpan<char> name, string term, FzfTermKind kind, Span<bool> painted, out int tier)
    {
        tier = MatchRank.TierFull;
        if (name.IsEmpty || !HasProviders || kind is not (FzfTermKind.Exact or FzfTermKind.Fuzzy))
            return false;
        if (!_wordIndex.TryGetValue(term, out var wi))
            return false;

        var disabled = SearchContext.DisabledAliasIds;
        foreach (var table in _providers)
        {
            if (disabled != null && disabled.Contains(table.ProviderId))
                continue;
            var matcher = table.Full[wi];
            if (matcher == null || !matcher.TryFind(name, painted, out var start, out var end))
                continue;

            var common = table.Common[wi] is { } commonMatcher && commonMatcher.TryFind(name, default, out _, out _);
            tier = !common ? MatchRank.TierFullRare
                : end - start == term.Length ? MatchRank.TierInitials
                : MatchRank.TierFull;
            return true;
        }
        return false;
    }

    private bool ScoreSets(
        FzfTermSet[] sets,
        ReadOnlySpan<char> name,
        FzfSlab? slab,
        HashSet<byte>? disabled,
        out FzfPatternResult result,
        out BranchStats stats)
    {
        result = default;
        stats = default;
        var total = 0;
        var covered = 0;
        var minBegin = int.MaxValue;
        var minEnd = int.MaxValue;
        var maxEnd = 0;
        var valid = false;
        var literalTerms = 0;
        var readTerms = 0;
        var loneReadingStart = -1;
        var loneReadingEnd = -1;

        foreach (var set in sets)
        {
            var satisfied = false;
            foreach (var term in set.Terms)
            {
                if (term.Text.Length == 0)
                    continue;

                // Whether this word is one the readings can decide -- and so whether its literal side has
                // to ask for a run even with fuzzy matching on. Letting a readable word scatter across the
                // name's own letters is what made one pinyin query answer differently in the two modes.
                var wordIndex = -1;
                var readable = !term.Inverse &&
                    term.Kind is FzfTermKind.Exact or FzfTermKind.Fuzzy &&
                    _wordIndex.TryGetValue(term.Text, out wordIndex) && _eligible[wordIndex];
                var literal = FzfAlgorithm.Match(
                    readable && term.Kind == FzfTermKind.Fuzzy ? FzfTermKind.Exact : term.Kind,
                    name, term.Text, term.CaseSensitive, FzfScoringScheme.Default, slab);
                if (literal.IsMatch)
                {
                    if (term.Inverse)
                        return false; // the excluded text is present; the whole query fails
                    total += literal.Score;
                    covered += term.Text.Length;
                    Accumulate(literal.Start, literal.End, ref minBegin, ref minEnd, ref maxEnd, ref valid);
                    literalTerms++;
                    satisfied = true;
                    break;
                }

                if (term.Inverse)
                {
                    // Absence satisfies the set and contributes nothing else (mirrors TryMatchSet).
                    satisfied = true;
                    break;
                }

                if (!readable)
                    continue;

                foreach (var table in _providers)
                {
                    if (disabled != null && disabled.Contains(table.ProviderId))
                        continue;
                    var matcher = table.Full[wordIndex];
                    if (matcher == null || !matcher.TryFind(name, default, out var start, out var end))
                        continue;

                    var common = table.Common[wordIndex] is { } commonMatcher &&
                        commonMatcher.TryFind(name, default, out _, out _);
                    total += PinyinBonus(start, name, common);
                    covered += end - start;
                    Accumulate(start, end, ref minBegin, ref minEnd, ref maxEnd, ref valid);
                    if (IsLone(term))
                    {
                        loneReadingStart = start;
                        loneReadingEnd = end;
                    }
                    readTerms++;
                    satisfied = true;
                    break;
                }
                if (satisfied)
                    break;
            }

            if (!satisfied)
                return false;
        }

        // A word the name literally says is scored by fzf, term by term, so a query nothing had to read
        // totals exactly what the old name tier returned -- which is the whole point of keeping the two
        // tables apart: ordinary searches keep the ordering the windows were tuned on, and the reference's
        // additive table only decides what is only reachable through its readings.
        if (literalTerms == 0)
        {
            if (readTerms > 0)
            {
                // Coverage is measured over the stem, as the reference's own whole-name rule does: with
                // the extension in the denominator, 报告.txt explains itself exactly as well as the folder
                // 报告 and still scores 6 to its 25, which is how a file loses to its own folder for
                // naming itself in full (the reference's pitfall 2360, curl vs curl.exe).
                var stem = StemLength(name);
                total += 25 * Math.Min(covered, stem) / stem;
            }

            if (_loneWord != null && loneReadingStart == 0)
            {
                var stemLength = StemLength(name);
                if (loneReadingEnd - loneReadingStart >= stemLength)
                    total += 40; // one word that spelled the whole stem, from the first character
            }
        }

        result = new FzfPatternResult(total, minBegin, minEnd, maxEnd, valid);
        stats = new BranchStats(literalTerms == 0 && readTerms > 0, covered);
        return true;
    }

    private bool IsLone(FzfTerm term) => _loneWord is { } lone && term.Text == lone.Text && term.Kind == lone.Kind;

    // Pinyin: -12 when the term only matches through a less common reading (the common-only matcher
    // misses it), 0 when it does not; then the same position ladder scaled for a spelling.
    private static int PinyinBonus(int start, ReadOnlySpan<char> name, bool common)
        => (common ? 0 : -12) + (start == 0 ? 26 : TextBoundary.IsBoundary(name, start) ? 16 : 4);

    // Length of the name without its extension ("报告" for "报告.txt"); the whole name when there is
    // no extension to strip.
    private static int StemLength(ReadOnlySpan<char> name)
    {
        var dot = name.LastIndexOf('.');
        return dot > 0 && dot < name.Length - 1 ? dot : name.Length;
    }

    private static void Accumulate(int start, int end, ref int minBegin, ref int minEnd, ref int maxEnd, ref bool valid)
    {
        if (start >= end)
            return;
        if (start < minBegin)
            minBegin = start;
        if (end < minEnd)
            minEnd = end;
        if (end > maxEnd)
            maxEnd = end;
        valid = true;
    }
}
