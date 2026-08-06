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
    // produced the alias. Decodes each candidate alias to UTF-16 -- acceptable here since this only runs
    // for the rare candidates that already failed both the literal-name and whole-query-alias tiers.
    internal static bool TryMatchMixed(Snapshot snapshot, SearchMatcher.QueryContext ctx, int uid, ReadOnlySpan<char> name, out FzfPatternResult best)
    {
        best = default;
        var mixedTerm = ctx.MixedTerm!; // TrySegmentPattern already excluded a disabled provider from consideration
        var matched = false;
        var (start, end) = snapshot.AliasEntryRange(uid);
        string? nameStr = null;
        for (var e = start; e < end; e++)
        {
            if (snapshot.AliasProviderId(e) != mixedTerm.ProviderId)
                continue;

            var aliasUtf8 = snapshot.AliasUtf8(e);
            if (aliasUtf8.Length == 0)
                continue;

            nameStr ??= name.ToString();
            var aliasStr = Encoding.UTF8.GetString(aliasUtf8);
            foreach (var segment in aliasStr.Split('|'))
            {
                if (segment.Length == 0)
                    continue;
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

            FzfPatternResult aliasMatch;
            bool hit;
            var decodedLength = -1; // -1: not decoded to chars yet (the ASCII/byte fast path below skips it)
            if (Ascii.IsValid(aliasUtf8))
            {
                hit = ctx.BytePattern.TryMatchSegmented(aliasUtf8, out aliasMatch, FzfScoringScheme.Default, worker.Slab, worker.ByteBuffers);
            }
            else
            {
                if (worker.AliasScratch.Length < aliasUtf8.Length)
                    worker.AliasScratch = new char[Math.Max(aliasUtf8.Length, worker.AliasScratch.Length * 2)];
                decodedLength = Encoding.UTF8.GetChars(aliasUtf8, worker.AliasScratch);
                hit = ctx.Pattern.TryMatch(worker.AliasScratch.AsSpan(0, decodedLength), out aliasMatch, FzfScoringScheme.Default, worker.Slab);
            }

            if (hit)
            {
                var acceptable = ctx.Pattern.IsAcceptableAliasMatch(aliasMatch, ctx.QueryLen);
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
                    acceptable = ctx.Pattern.IsAcceptableAliasMatch(aliasMatch, ctx.QueryLen, worker.AliasScratch.AsSpan(0, decodedLength), FzfScoringScheme.Default, worker.Slab);
                }

                if (acceptable)
                {
                    var weighted = ctx.Pattern.WeightAliasMatch(aliasMatch, ctx.QueryLen);
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
