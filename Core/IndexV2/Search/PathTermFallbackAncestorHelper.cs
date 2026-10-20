using System.Text;
using Lertaro.Core.IndexV2.Delta;
using Lertaro.Core.IndexV2.Persistence;
using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.IndexV2.Search;

// Split out to keep PathTermFallback under the repository's per-file line limit. This helper owns only
// ancestor-chain traversal and segment matching; PathTermFallback remains responsible for candidate
// collection, filtering, ranking, and branch orchestration.
internal static class PathTermFallbackAncestorHelper
{
    internal static int AncestorMask(Snapshot snapshot, DeltaOverlay delta, int parentRow,
        FzfPattern[] termPatterns, FzfBytePattern[] termBytePatterns,
        FzfPattern[] termAliasPatterns, FzfBytePattern[] termAliasBytePatterns, SearchMatcher.Worker worker,
        Dictionary<int, int> memo, int fullMask, int rootMask, List<int> chain)
    {
        if (memo.TryGetValue(parentRow, out var cached))
            return cached;

        chain.Clear();
        var mask = 0;
        var current = parentRow;
        var composable = false;
        for (var depth = 0; depth < snapshot.Count && current >= 0; depth++)
        {
            if (memo.TryGetValue(current, out var known))
            {
                mask = known;
                composable = true;
                break;
            }

            if (delta.IsSuperseded(current))
            {
                var fallback = MaskFromPath(delta.GetFullPath(parentRow), termPatterns, worker);
                if (fallback != fullMask)
                    fallback |= rootMask;
                memo[parentRow] = fallback;
                return fallback;
            }

            chain.Add(current);
            var parent = snapshot.ParentIndexes[current];
            if (parent == current)
            {
                composable = true;
                break;
            }
            current = parent;
        }

        for (var i = chain.Count - 1; i >= 0; i--)
        {
            var node = chain[i];
            if (mask != fullMask)
            {
                var uid = (int)snapshot.NameIds[node];
                var nameUtf8 = snapshot.UniqueNameUtf8(uid);
                if (nameUtf8.Length > 0)
                    mask |= MaskForSegment(snapshot, uid, nameUtf8, termPatterns, termBytePatterns, termAliasPatterns, termAliasBytePatterns, worker, mask, fullMask);
            }

            if (composable)
                memo[node] = mask | rootMask;
        }

        if (mask != fullMask)
            mask |= rootMask;

        memo[parentRow] = mask;
        return mask;
    }

    private static int MaskForSegment(Snapshot snapshot, int uid, ReadOnlySpan<byte> nameUtf8,
        FzfPattern[] termPatterns, FzfBytePattern[] termBytePatterns,
        FzfPattern[] termAliasPatterns, FzfBytePattern[] termAliasBytePatterns, SearchMatcher.Worker worker, int already, int fullMask)
    {
        var mask = 0;
        var ascii = snapshot.IsUniqueAscii(uid);
        var written = 0;
        if (!ascii)
        {
            if (worker.Scratch.Length < nameUtf8.Length)
                worker.Scratch = new char[Math.Max(nameUtf8.Length, worker.Scratch.Length * 2)];
            written = Encoding.UTF8.GetChars(nameUtf8, worker.Scratch);
        }

        ReadOnlySpan<char> segment = ascii ? default : worker.Scratch.AsSpan(0, written);
        if (!ascii)
        {
            var normalizedBuffer = worker.NormalizedScratch;
            var normalizedLength = SearchTextNormalizer.NormalizeInto(segment, ref normalizedBuffer, out _);
            if (normalizedBuffer != null)
                worker.NormalizedScratch = normalizedBuffer;
            if (normalizedLength > 0 && normalizedBuffer is { } normalized)
                segment = normalized.AsSpan(0, normalizedLength);
        }

        for (var i = 0; i < termPatterns.Length; i++)
        {
            var bit = 1 << i;
            if ((already & bit) != 0)
                continue;
            var hit = ascii
                ? termBytePatterns[i].TryMatch(nameUtf8, out _, FzfScoringScheme.Default, worker.Slab, worker.ByteBuffers)
                : termPatterns[i].TryMatch(segment, out _, FzfScoringScheme.Default, worker.Slab);
            if (!hit && !ascii)
            {
                // The live reading tier decides the same question for a pinyin word the literal pass
                // cannot reach (see PinyinNameEvaluator).
                var live = PinyinNameEvaluator.ForPattern(termPatterns[i]);
                hit = live.HasProviders && live.TryScore(segment, worker.Slab, out _);
            }
            if (hit)
                mask |= bit;
        }

        var unresolved = fullMask & ~(already | mask);
        if (unresolved != 0)
            mask |= MaskFromAliases(snapshot, uid, termPatterns, termBytePatterns, termAliasPatterns, termAliasBytePatterns, worker, unresolved);
        return mask;
    }

    // Only ever asked about a candidate's ALIASES, so it works off the alias-side readings of the terms
    // (FzfPatternAliasReading): each term as a contiguous run if it can be, as a subsequence otherwise --
    // for a provider that declares syllable structure, and only for one.
    private static int MaskFromAliases(Snapshot snapshot, int uid, FzfPattern[] termPatterns,
        FzfBytePattern[] termBytePatterns, FzfPattern[] termAliasPatterns,
        FzfBytePattern[] termAliasBytePatterns, SearchMatcher.Worker worker, int unresolved)
    {
        var mask = 0;
        var disabledIds = SearchContext.DisabledAliasIds;
        var (start, end) = snapshot.AliasEntryRange(uid);
        for (var e = start; e < end && mask != unresolved; e++)
        {
            if (disabledIds != null && disabledIds.Contains(snapshot.AliasProviderId(e)))
                continue;
            var aliasUtf8 = snapshot.AliasUtf8(e);
            if (aliasUtf8.Length == 0)
                continue;

            var ascii = Ascii.IsValid(aliasUtf8);
            var written = 0;
            if (!ascii)
            {
                if (worker.AliasScratch.Length < aliasUtf8.Length)
                    worker.AliasScratch = new char[Math.Max(aliasUtf8.Length, worker.AliasScratch.Length * 2)];
                written = Encoding.UTF8.GetChars(aliasUtf8, worker.AliasScratch);
            }

            // Same reading choice the other alias tiers make: both readings for a provider that declares
            // syllable structure, the strict one for a provider that declares none.
            var separator = AliasProviderRegistry.GetSyllableSeparator(snapshot.AliasProviderId(e));
            for (var i = 0; i < termPatterns.Length; i++)
            {
                var bit = 1 << i;
                if ((unresolved & bit) == 0 || (mask & bit) != 0)
                    continue;
                var hit = ascii
                    ? (separator == '\0' ? termBytePatterns[i] : termAliasBytePatterns[i])
                        .TryMatchSegmented(aliasUtf8, out _, FzfScoringScheme.Default, worker.Slab, worker.ByteBuffers)
                    : (separator == '\0' ? termPatterns[i] : termAliasPatterns[i])
                        .TryMatch(worker.AliasScratch.AsSpan(0, written), out _, FzfScoringScheme.Default, worker.Slab);
                if (hit)
                    mask |= bit;
            }
        }
        return mask;
    }

    private static int MaskFromPath(string path, FzfPattern[] termPatterns, SearchMatcher.Worker worker)
        => MaskFromSegments(path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries), termPatterns, worker, 0);

    internal static int MaskFromSegments(string[] segments, FzfPattern[] termPatterns, SearchMatcher.Worker worker, int already)
    {
        var mask = 0;
        foreach (var segment in segments)
        {
            var matchSegment = SearchTextNormalizer.Normalize(segment);
            for (var i = 0; i < termPatterns.Length; i++)
            {
                var bit = 1 << i;
                if (((already | mask) & bit) != 0)
                    continue;
                var hit = termPatterns[i].TryMatch(matchSegment, out _, FzfScoringScheme.Default, worker.Slab);
                if (!hit)
                {
                    // Same live reading tier as the per-name mask: a pinyin word the literal pass
                    // cannot reach still counts for this segment (see PinyinNameEvaluator).
                    var live = PinyinNameEvaluator.ForPattern(termPatterns[i]);
                    hit = live.HasProviders && live.TryScore(matchSegment, worker.Slab, out _);
                }
                if (hit)
                    mask |= bit;
            }
        }
        return mask;
    }
}
