using System.Text;
using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;
using Lertaro.Core.IndexV2.Persistence;

namespace Lertaro.Core.IndexV2.Search;

// Alias-fallback matching tiers for SearchMatcher.MatchOne, split out (composition, not a partial class)
// to keep SearchMatcher.cs under the project's line limit. SearchMatcher itself keeps the primary
// unique-name scan orchestration and dispatch (MatchOne); this file holds the two fallback tiers that
// only run once a candidate's literal name has already failed to match: the general per-alias fallback
// (TryMatchAliases -- also called directly by SearchMatcherPath, via the thin SearchMatcher.TryMatchAliases
// forwarder that preserves that external call site) and the mixed-alphabet last resort (TryMatchMixed,
// only ever reached from SearchMatcher.MatchOne).
internal static class SearchMatcherAliasExtensions
{
    // Last-resort tier for a query mixing an alias provider's own two alphabets: only
    // the baked aliases belonging to that exact provider are worth trying, since MapAliasToSourceIndices
    // (needed to align the alias-syntax run back onto `name`) is only meaningful for the provider that
    // produced the alias. Materializes each candidate alias as text -- acceptable here since this only runs
    // for the rare candidates that already failed both the literal-name and whole-query-alias tiers.
    internal static bool TryMatchMixed(Snapshot snapshot, SearchMatcher.QueryContext ctx, int uid, ReadOnlySpan<char> name, out FzfPatternResult best)
    {
        best = default;
        var mixedTerm = ctx.MixedTerm!; // TrySegmentPattern already excluded a disabled provider from consideration
        var matched = false;
        var (start, end) = snapshot.AliasEntryRange(uid);
        string? nameStr = null;
        // Segments of one entry: its '|'-split spellings. This lane matches an alias as TEXT (it maps the
        // positions it took back onto the source), so the entry has to be decoded here -- unlike the
        // tiers above, which decide against the alias's raw bytes.
        var segments = MixedSegments;

        // A live provider's readings are not in the snapshot at all, so the spelling this lane matches
        // is materialized from the provider (AppendSpellings); the entry loop below then adds whatever
        // string entries the same provider still bakes (the Simplified spelling).
        if (mixedTerm.Provider is PluginSdk.Abstractions.Plugins.IAliasLiveMatcherProvider liveProvider)
        {
            nameStr = name.ToString();
            segments.Clear();
            liveProvider.AppendSpellings(nameStr, segments);
            foreach (var segment in segments)
            {
                if (!MixedQueryMatcher.TryMatch(mixedTerm, name, nameStr, segment, out var liveMm))
                    continue;
                var liveCandidate = new FzfPatternResult(liveMm.Score, liveMm.MinBegin, liveMm.MaxEnd, liveMm.MaxEnd, liveMm.ValidOffsetFound);
                if (!matched || liveCandidate.Score > best.Score)
                {
                    matched = true;
                    best = liveCandidate;
                }
            }
        }

        for (var e = start; e < end; e++)
        {
            if (snapshot.AliasProviderId(e) != mixedTerm.ProviderId)
                continue;

            segments.Clear();
            foreach (var segment in Encoding.UTF8.GetString(snapshot.AliasUtf8(e)).Split('|'))
            {
                if (segment.Length > 0)
                    segments.Add(segment);
            }
            foreach (var segment in segments)
            {
                nameStr ??= name.ToString();
                if (!MixedQueryMatcher.TryMatch(mixedTerm, name, nameStr, segment, out var mm))
                    continue;

                var candidate = new FzfPatternResult(mm.Score, mm.MinBegin, mm.MaxEnd, mm.MaxEnd, mm.ValidOffsetFound);
                if (!matched || candidate.Score > best.Score)
                {
                    matched = true;
                    best = candidate;
                }
            }
        }
        return matched;
    }

    // One scratch list per thread for the lane above: reached per candidate, but only for the rare
    // candidates that already failed every earlier tier.
    [ThreadStatic] private static List<string>? _mixedSegments;
    private static List<string> MixedSegments => _mixedSegments ??= new List<string>(4);

    // Zero-copy alias fallback: each baked alias is matched from its raw UTF-8 (byte path for ASCII
    // aliases -- the common case, pinyin -- else decoded into the alias scratch), honoring
    // SearchContext.DisabledAliasIds and the IsAcceptableAliasMatch quality gate.
    internal static bool TryMatchAliases(Snapshot snapshot, SearchMatcher.QueryContext ctx, int uid, SearchMatcher.Worker worker, out FzfPatternResult best)
    {
        best = default;
        var matched = false;
        var disabledIds = SearchContext.DisabledAliasIds;
        var (start, end) = snapshot.AliasEntryRange(uid);
        for (var e = start; e < end; e++)
        {
            if (disabledIds != null && disabledIds.Contains(snapshot.AliasProviderId(e)))
                continue;

            var aliasUtf8 = snapshot.AliasUtf8(e);
            if (aliasUtf8.Length == 0)
                continue;

            // Aliases are read the alias side's way -- each term as a run if it can be, as a subsequence
            // otherwise (see FzfPatternAliasReading) -- while the boundary rule below stays keyed on what
            // the user actually typed. A provider that declares no structure has no boundary to align to,
            // so it keeps the strict reading.
            var separator = AliasProviderRegistry.GetSyllableSeparator(snapshot.AliasProviderId(e));
            var aliasPattern = ctx.Pattern.ReadingFor(separator);
            FzfPatternResult aliasMatch;
            bool hit;
            var decodedLength = -1; // -1: not decoded to chars yet (the ASCII/byte fast path below skips it)
            var isAsciiAlias = Ascii.IsValid(aliasUtf8);
            if (isAsciiAlias)
            {
                var bytePattern = separator == '\0' ? ctx.BytePattern : ctx.AliasBytePattern;
                hit = bytePattern.TryMatchSegmented(aliasUtf8, out aliasMatch, FzfScoringScheme.Default, worker.Slab, worker.ByteBuffers);
            }
            else
            {
                if (worker.AliasScratch.Length < aliasUtf8.Length)
                    worker.AliasScratch = new char[Math.Max(aliasUtf8.Length, worker.AliasScratch.Length * 2)];
                decodedLength = Encoding.UTF8.GetChars(aliasUtf8, worker.AliasScratch);
                hit = aliasPattern.TryMatch(worker.AliasScratch.AsSpan(0, decodedLength), out aliasMatch, FzfScoringScheme.Default, worker.Slab);
            }

            if (hit)
            {
                // A precise query must not match a full transliteration mid-syllable (see AliasMatchRules).
                // The alias's own provider is the only one that knows where its boundaries fall, and for a
                // baked alias that provider is reachable only by its id.
                var aligned = isAsciiAlias
                    ? AliasMatchRules.AllowsMatchUtf8(ctx.Pattern, separator, aliasUtf8, aliasMatch.MinBegin)
                    : AliasMatchRules.AllowsMatch(ctx.Pattern, separator, worker.AliasScratch.AsSpan(0, decodedLength), aliasMatch.MinBegin);
                if (!aligned)
                    continue;

                var acceptable = aliasPattern.IsAcceptableAliasMatch(aliasMatch, ctx.QueryLen);
                if (!acceptable)
                {
                    // The multi-term "every term individually tight" fallback (see FzfPattern's own
                    // comment on IsAcceptableAliasMatch) needs the alias as chars -- the ASCII/byte fast
                    // path above deliberately never decodes it, since the common case doesn't need to.
                    // Only pay that decode cost here, in this already-rare tail (existing check failed).
                    if (decodedLength < 0)
                    {
                        if (worker.AliasScratch.Length < aliasUtf8.Length)
                            worker.AliasScratch = new char[Math.Max(aliasUtf8.Length, worker.AliasScratch.Length * 2)];
                        decodedLength = Encoding.UTF8.GetChars(aliasUtf8, worker.AliasScratch);
                    }
                    acceptable = aliasPattern.IsAcceptableAliasMatch(aliasMatch, ctx.QueryLen, worker.AliasScratch.AsSpan(0, decodedLength), FzfScoringScheme.Default, worker.Slab);
                }

                if (acceptable)
                {
                    var weighted = aliasPattern.WeightAliasMatch(aliasMatch, ctx.QueryLen);
                    if (!matched || weighted.Score > best.Score)
                    {
                        matched = true;
                        best = weighted;
                    }
                }
            }
        }
        return matched;
    }
}
