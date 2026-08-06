using System.Text;
using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;

using Lertaro.Core.IndexV2.Delta;

using Lertaro.Core.IndexV2.Persistence;
namespace Lertaro.Core.IndexV2.Search.PathMode;

// Ranking-only weight (percentage*consecutiveness, product across matched segments), computed
// separately from PathGate.Verify/VerifyPath and ONLY for path-mode's bounded post-scan refinement
// (PathSearchFuzzy) -- NOT memoized, NOT called during the hot scan, since it needs the same
// relatively expensive HighlightMask computation name mode moved out of its own hot path for the
// same reason (see FzfResultRank.ApplyWeight). Re-walks the same ancestor chain as PathGate.Verify;
// safe to call only on the small headroom-bounded candidate set that survives the unweighted scan.
// Extracted out of PathGate.cs (composition, not a partial class) purely to keep PathGate.cs under
// the repo's per-file line limit.
internal sealed class PathGateWeighting
{
    private readonly Snapshot _snapshot;
    private readonly DeltaOverlay _delta;
    private readonly string[] _querySegments;
    private readonly FzfPattern[] _segmentPatterns;
    private readonly FzfBytePattern[] _segmentBytePatterns;
    private readonly string[] _rootSegments;

    public PathGateWeighting(Snapshot snapshot, DeltaOverlay delta, string[] querySegments, FzfPattern[] segmentPatterns, FzfBytePattern[] segmentBytePatterns, string[] rootSegments)
    {
        _snapshot = snapshot;
        _delta = delta;
        _querySegments = querySegments;
        _segmentPatterns = segmentPatterns;
        _segmentBytePatterns = segmentBytePatterns;
        _rootSegments = rootSegments;
    }

    public double ComputeWeight(int parentRow, SearchMatcher.Worker worker)
    {
        var q = _querySegments.Length - 1;
        var weight = 1.0;
        var current = parentRow;
        for (var depth = 0; depth < 512 && current >= 0 && q >= 0; depth++)
        {
            if (_delta.IsSuperseded(current))
            {
                var parentPath = _delta.GetFullPath(parentRow);
                return weight * ComputeWeightForPath(parentPath, worker);
            }

            var uid = (int)_snapshot.NameIds[current];
            var nameUtf8 = _snapshot.UniqueNameUtf8(uid);
            if (nameUtf8.Length > 0 && TryMatchSegmentRowWeight(uid, nameUtf8, q, worker, out var segWeight))
            {
                weight *= segWeight;
                q--;
            }

            var parent = _snapshot.ParentIndexes[current];
            if (parent == current)
                break;
            current = parent;
        }

        for (var i = _rootSegments.Length - 1; i >= 0 && q >= 0; i--)
        {
            if (TryMatchSegmentTextWeight(_rootSegments[i], q, worker, out var segWeight))
            {
                weight *= segWeight;
                q--;
            }
        }

        return weight;
    }

    public double ComputeWeightForPath(string path, SearchMatcher.Worker worker)
    {
        var pathSegments = path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        var qIdx = _querySegments.Length - 1;
        var pIdx = pathSegments.Length - 1;
        var weight = 1.0;

        while (qIdx >= 0 && pIdx >= 0)
        {
            if (TryMatchSegmentTextWeight(pathSegments[pIdx], qIdx, worker, out var segWeight))
            {
                weight *= segWeight;
                qIdx--;
            }
            pIdx--;
        }
        return weight;
    }

    // Mirrors PathGate.TryMatchSegmentRow's match-finding exactly, but only needs the winning branch's
    // weight -- re-running TryMatch here (rather than threading weight through the score-only method)
    // keeps the hot Verify/TryMatchSegmentRow path free of any HighlightMask reference at all.
    private bool TryMatchSegmentRowWeight(int uid, ReadOnlySpan<byte> nameUtf8, int q, SearchMatcher.Worker worker, out double weight)
    {
        weight = 1.0;
        var pattern = _segmentPatterns[q];
        if (_snapshot.IsUniqueAscii(uid))
        {
            if (_segmentBytePatterns[q].TryMatch(nameUtf8, out _, FzfScoringScheme.Default, worker.Slab, worker.ByteBuffers))
            {
                weight = FzfBytePattern.ComputeWeight(nameUtf8, pattern);
                return true;
            }
        }
        else
        {
            if (worker.Scratch.Length < nameUtf8.Length)
                worker.Scratch = new char[Math.Max(nameUtf8.Length, worker.Scratch.Length * 2)];
            var written = Encoding.UTF8.GetChars(nameUtf8, worker.Scratch);
            var name = worker.Scratch.AsSpan(0, written);
            if (pattern.TryMatch(name, out _, FzfScoringScheme.Default, worker.Slab))
            {
                weight = HighlightMask.ComputeWeight(name, pattern);
                return true;
            }
        }

        var disabledIds = SearchContext.DisabledAliasIds;
        var (start, end) = _snapshot.AliasEntryRange(uid);
        for (var e = start; e < end; e++)
        {
            if (disabledIds != null && disabledIds.Contains(_snapshot.AliasProviderId(e)))
                continue;
            var aliasUtf8 = _snapshot.AliasUtf8(e);
            if (aliasUtf8.Length == 0)
                continue;
            var isMatch = Ascii.IsValid(aliasUtf8)
                ? _segmentBytePatterns[q].TryMatchSegmented(aliasUtf8, out _, FzfScoringScheme.Default, worker.Slab, worker.ByteBuffers)
                : MatchesAliasChars(pattern, aliasUtf8, worker);
            if (isMatch)
            {
                // Weight is measured against the segment's own display name, not the alias string --
                // mirrors HighlightMask, which maps alias-matched positions back onto the source name.
                weight = ComputeSegmentNameWeight(uid, nameUtf8, worker, pattern);
                return true;
            }
        }
        return false;
    }

    private static bool MatchesAliasChars(FzfPattern pattern, ReadOnlySpan<byte> aliasUtf8, SearchMatcher.Worker worker)
    {
        if (worker.AliasScratch.Length < aliasUtf8.Length)
            worker.AliasScratch = new char[Math.Max(aliasUtf8.Length, worker.AliasScratch.Length * 2)];
        var written = Encoding.UTF8.GetChars(aliasUtf8, worker.AliasScratch);
        return pattern.TryMatch(worker.AliasScratch.AsSpan(0, written), out _, FzfScoringScheme.Default, worker.Slab);
    }

    private double ComputeSegmentNameWeight(int uid, ReadOnlySpan<byte> nameUtf8, SearchMatcher.Worker worker, FzfPattern pattern)
    {
        if (_snapshot.IsUniqueAscii(uid))
            return FzfBytePattern.ComputeWeight(nameUtf8, pattern);

        if (worker.Scratch.Length < nameUtf8.Length)
            worker.Scratch = new char[Math.Max(nameUtf8.Length, worker.Scratch.Length * 2)];
        var written = Encoding.UTF8.GetChars(nameUtf8, worker.Scratch);
        return HighlightMask.ComputeWeight(worker.Scratch.AsSpan(0, written), pattern);
    }

    private bool TryMatchSegmentTextWeight(string segment, int q, SearchMatcher.Worker worker, out double weight)
    {
        weight = 1.0;
        var pattern = _segmentPatterns[q];
        if (pattern.TryMatch(segment, out _, FzfScoringScheme.Default, worker.Slab))
        {
            weight = pattern.IsEmpty ? 1.0 : HighlightMask.ComputeWeight(segment, pattern);
            return true;
        }
        if (!AliasProviderRegistry.HasNonAscii(segment))
            return false;
        foreach (var provider in AliasProviderRegistry.GetActiveProviders())
        {
            try
            {
                if (!provider.CanHandle(segment))
                    continue;
                foreach (var alias in provider.GetAliases(segment))
                {
                    if (pattern.TryMatch(alias, out _, FzfScoringScheme.Default, worker.Slab))
                    {
                        weight = HighlightMask.ComputeWeight(segment, pattern);
                        return true;
                    }
                }
            }
            catch
            {
            }
        }
        return false;
    }
}
