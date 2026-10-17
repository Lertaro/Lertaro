using System.Runtime.CompilerServices;
using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Core.IndexV2.Search;

// Decides one query against one candidate NAME with the WinShun-style live matcher: every plain
// positive term is tried literally first (the user's own precision) and, when that fails, against a
// live provider's reading NFA (each character consumes any non-empty prefix of one of its readings;
// see IAliasLiveMatcherProvider). The score is the reference's additive table -- literal 30/18/4,
// pinyin -12-if-rare + 26/16/4, whole-name 60/45/40, coverage 25*n/len -- so the frequency signal and
// the literal-over-pinyin preference both live in one number the scan can rank by.
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
    private readonly FzfTerm[] _words; // distinct plain positive terms, in first-appearance order
    private readonly Dictionary<string, int> _wordIndex;
    private readonly ProviderTable[] _providers;
    private readonly FzfTerm? _loneWord; // the single plain positive term, when there is exactly one

    private sealed class ProviderTable
    {
        public required byte ProviderId { get; init; }
        public required IAliasTermMatcher?[] Full { get; init; }
        public required IAliasTermMatcher?[] Common { get; init; }
    }

    private PinyinNameEvaluator(FzfPattern pattern, FzfTerm[] words, Dictionary<string, int> wordIndex,
        ProviderTable[] providers, FzfTerm? loneWord)
    {
        _pattern = pattern;
        _words = words;
        _wordIndex = wordIndex;
        _providers = providers;
        _loneWord = loneWord;
    }

    public bool HasProviders => _providers.Length != 0;

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
            if (any)
                tables.Add(new ProviderTable
                {
                    ProviderId = AliasProviderRegistry.GetProviderId(provider),
                    Full = full,
                    Common = common,
                });
        }

        return new PinyinNameEvaluator(pattern, list, index, tables.ToArray(), lone);
    }

    /// <summary>
    /// Decides the whole query against <paramref name="name"/>. On true the match carries the additive
    /// score and the span, ready for <c>FzfResultRank.ForDefaultScheme</c>.
    /// </summary>
    public bool TryScore(ReadOnlySpan<char> name, FzfSlab? slab, out FzfPatternResult match)
    {
        match = default;
        if (name.IsEmpty || !HasProviders)
            return false;

        var disabled = SearchContext.DisabledAliasIds;
        if (_pattern.OrGroups != null)
        {
            foreach (var group in _pattern.OrGroups)
            {
                if (ScoreSets(group.Sets, name, slab, disabled, out match))
                    return true;
            }
            return false;
        }
        return ScoreSets(_pattern.TermSets, name, slab, disabled, out match);
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

    private bool ScoreSets(FzfTermSet[] sets, ReadOnlySpan<char> name, FzfSlab? slab, HashSet<byte>? disabled,
        out FzfPatternResult result)
    {
        result = default;
        var total = 0;
        var covered = 0;
        var minBegin = int.MaxValue;
        var minEnd = int.MaxValue;
        var maxEnd = 0;
        var valid = false;
        var loneLiteral = false;
        var lonePinyinStart = -1;
        var lonePinyinEnd = -1;

        foreach (var set in sets)
        {
            var satisfied = false;
            foreach (var term in set.Terms)
            {
                if (term.Text.Length == 0)
                    continue;

                var literal = FzfAlgorithm.Match(term.Kind, name, term.Text, term.CaseSensitive,
                    FzfScoringScheme.Default, slab);
                if (literal.IsMatch)
                {
                    if (term.Inverse)
                        return false; // the excluded text is present; the whole query fails
                    total += LiteralBonus(literal.Start, name);
                    covered += term.Text.Length;
                    Accumulate(literal.Start, literal.End, ref minBegin, ref minEnd, ref maxEnd, ref valid);
                    loneLiteral = IsLone(term);
                    satisfied = true;
                    break;
                }

                if (term.Inverse)
                {
                    // Absence satisfies the set and contributes nothing else (mirrors TryMatchSet).
                    satisfied = true;
                    break;
                }

                if (term.Kind is not (FzfTermKind.Exact or FzfTermKind.Fuzzy) ||
                    !_wordIndex.TryGetValue(term.Text, out var wi))
                    continue;

                foreach (var table in _providers)
                {
                    if (disabled != null && disabled.Contains(table.ProviderId))
                        continue;
                    var matcher = table.Full[wi];
                    if (matcher == null || !matcher.TryFind(name, default, out var start, out var end))
                        continue;

                    var common = table.Common[wi] is { } commonMatcher && commonMatcher.TryFind(name, default, out _, out _);
                    total += PinyinBonus(start, name, common);
                    covered += end - start;
                    Accumulate(start, end, ref minBegin, ref minEnd, ref maxEnd, ref valid);
                    lonePinyinStart = IsLone(term) ? start : lonePinyinStart;
                    lonePinyinEnd = IsLone(term) ? end : lonePinyinEnd;
                    satisfied = true;
                    break;
                }
                if (satisfied)
                    break;
            }

            if (!satisfied)
                return false;
        }

        // Whole-name bonuses for a single-word query, in the reference's branch order.
        if (_loneWord is { } lone)
        {
            if (lonePinyinStart >= 0)
            {
                var stem = StemLength(name);
                if (lonePinyinStart == 0 && lonePinyinEnd - lonePinyinStart >= stem)
                    total += 40; // the whole name (without extension), spelled as pinyin
            }
            else if (loneLiteral)
            {
                if (name.Equals(lone.Text, StringComparison.OrdinalIgnoreCase))
                    total += 60;
                else
                {
                    var stem = StemLength(name);
                    if (stem < name.Length && name[..stem].Equals(lone.Text, StringComparison.OrdinalIgnoreCase))
                        total += 45;
                }
            }
        }

        total += 25 * Math.Min(covered, name.Length) / name.Length;
        result = new FzfPatternResult(total, minBegin, minEnd, maxEnd, valid);
        return true;
    }

    private bool IsLone(FzfTerm term) => _loneWord is { } lone && term.Text == lone.Text && term.Kind == lone.Kind;

    // The reference's literal bonuses; the position is the matched occurrence's start (its up-to-four
    // probe for a later word-start occurrence is folded into MatchShape.IsBoundary's own boundaries).
    private static int LiteralBonus(int start, ReadOnlySpan<char> name)
        => start == 0 ? 30 : MatchShape.IsBoundary(name, start) ? 18 : 4;

    // Pinyin: -12 when the term only matches through a less common reading (the common-only matcher
    // misses it), 0 when it does not; then the same position ladder scaled for a spelling.
    private static int PinyinBonus(int start, ReadOnlySpan<char> name, bool common)
        => (common ? 0 : -12) + (start == 0 ? 26 : MatchShape.IsBoundary(name, start) ? 16 : 4);

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
