using System.Collections.Concurrent;
using System.Text;
using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;

using Lertaro.Core.IndexV2.Delta;

using Lertaro.Core.IndexV2.Persistence;
namespace Lertaro.Core.IndexV2.Search.PathMode;

// Directory-segment verification for path mode. Replaces the old per-candidate
// "build parent path string -> Split -> re-ParseText each query segment -> live pinyin per segment"
// with: segment patterns parsed ONCE per query; ancestor ROWS walked directly (right-to-left ==
// child-to-root, the same consumption semantics as splitting the built path); each ancestor's name
// and BAKED aliases consumed zero-copy from the snapshot; and the verdict memoized per parent row,
// since every file in a directory shares it. The row walk mirrors DeltaOverlay.GetFullPath: stop at
// parent < 0 or self-parent (no orphan hop -- the delta path walk has none), skip empty names, then
// offer the SourceRoot's own segments (the built path always carried the root prefix, whose tokens
// are legitimately matchable). A parent whose chain touches live delta state (rename/override) falls
// back to verifying the delta-built path string -- rare, and exactly what the old code always did.
internal sealed class PathGate
{
    private readonly Snapshot _snapshot;
    private readonly DeltaOverlay _delta;
    private readonly string[] _querySegments;
    private readonly FzfPattern[] _segmentPatterns;
    private readonly FzfBytePattern[] _segmentBytePatterns;
    private readonly string[] _rootSegments;
    // Score > 0 = verified (<= 0 rejects, matching the old dirScore contract); Depth 0 only for a
    // bare-root parent (parent path == SourceRoot, whose trailing separator swallows the child's own).
    private readonly ConcurrentDictionary<int, (int Score, byte Depth)> _memo = new();
    private readonly PathGateWeighting _weighting;

    public PathGate(Snapshot snapshot, DeltaOverlay delta, string dirQuery)
    {
        _snapshot = snapshot;
        _delta = delta;
        _querySegments = dirQuery.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        _segmentPatterns = new FzfPattern[_querySegments.Length];
        _segmentBytePatterns = new FzfBytePattern[_querySegments.Length];
        for (var i = 0; i < _querySegments.Length; i++)
        {
            _segmentPatterns[i] = FzfPattern.ParseText(_querySegments[i]);
            _segmentBytePatterns[i] = FzfBytePattern.From(_segmentPatterns[i]);
        }
        _rootSegments = snapshot.SourceRoot.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        _weighting = new PathGateWeighting(_snapshot, _delta, _querySegments, _segmentPatterns, _segmentBytePatterns, _rootSegments);
    }

    public (int Score, byte Depth) Verify(int parentRow, SearchMatcher.Worker worker)
    {
        if (_memo.TryGetValue(parentRow, out var cached))
            return cached;

        var q = _querySegments.Length - 1;
        var score = 0;
        var sawSegment = false;
        var current = parentRow;
        for (var depth = 0; depth < 512 && current >= 0; depth++)
        {
            if (_delta.IsSuperseded(current))
            {
                // Renamed/overridden ancestor: this chain's live names come from delta state -- verify
                // the delta-built path string instead, like the old per-candidate path always did.
                var parentPath = _delta.GetFullPath(parentRow);
                var result = (VerifyPath(parentPath, worker), parentPath.EndsWith('\\') ? (byte)0 : (byte)1);
                _memo[parentRow] = result;
                return result;
            }

            var uid = (int)_snapshot.NameIds[current];
            var nameUtf8 = _snapshot.UniqueNameUtf8(uid);
            if (nameUtf8.Length > 0)
            {
                sawSegment = true;
                if (q >= 0 && TryMatchSegmentRow(uid, nameUtf8, q, worker, out var segScore))
                {
                    score += segScore;
                    q--;
                }
            }

            var parent = _snapshot.ParentIndexes[current];
            if (parent == current)
                break;
            current = parent;
        }

        for (var i = _rootSegments.Length - 1; i >= 0 && q >= 0; i--)
        {
            if (TryMatchSegmentText(_rootSegments[i], q, worker, out var segScore))
            {
                score += segScore;
                q--;
            }
        }

        var verdict = (q < 0 ? score : 0, sawSegment ? (byte)1 : (byte)0);
        _memo[parentRow] = verdict;
        return verdict;
    }

    // String-segment verification for delta rows' parent paths (and delta-touched ancestor chains):
    // same right-to-left consumption over a split path, but with the patterns parsed once per query.
    public int VerifyPath(string path, SearchMatcher.Worker worker)
    {
        var pathSegments = path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        var qIdx = _querySegments.Length - 1;
        var pIdx = pathSegments.Length - 1;
        var totalScore = 0;

        while (qIdx >= 0 && pIdx >= 0)
        {
            if (TryMatchSegmentText(pathSegments[pIdx], qIdx, worker, out var score))
            {
                totalScore += score;
                qIdx--;
            }
            pIdx--;
        }
        return qIdx < 0 ? totalScore : 0;
    }

    private bool TryMatchSegmentRow(int uid, ReadOnlySpan<byte> nameUtf8, int q, SearchMatcher.Worker worker, out int score)
    {
        score = 0;
        if (_snapshot.IsUniqueAscii(uid))
        {
            if (_segmentBytePatterns[q].TryMatch(nameUtf8, out var match, FzfScoringScheme.Default, worker.Slab, worker.ByteBuffers))
            {
                score = match.Score;
                return true;
            }
        }
        else
        {
            if (worker.Scratch.Length < nameUtf8.Length)
                worker.Scratch = new char[Math.Max(nameUtf8.Length, worker.Scratch.Length * 2)];
            var written = Encoding.UTF8.GetChars(nameUtf8, worker.Scratch);
            if (_segmentPatterns[q].TryMatch(worker.Scratch.AsSpan(0, written), out var match, FzfScoringScheme.Default, worker.Slab))
            {
                score = match.Score;
                return true;
            }
        }

        // Baked-alias fallback: deliberately UNGATED (no IsAcceptableAliasMatch) and first-match-wins,
        // preserving the old TryMatchSegmentWithAlias semantics -- which regenerated pinyin LIVE per
        // candidate; the same aliases now come zero-copy from the snapshot.
        var disabledIds = SearchContext.DisabledAliasIds;
        var (start, end) = _snapshot.AliasEntryRange(uid);
        for (var e = start; e < end; e++)
        {
            if (disabledIds != null && disabledIds.Contains(_snapshot.AliasProviderId(e)))
                continue;
            var aliasUtf8 = _snapshot.AliasUtf8(e);
            if (aliasUtf8.Length == 0)
                continue;
            if (Ascii.IsValid(aliasUtf8))
            {
                if (_segmentBytePatterns[q].TryMatchSegmented(aliasUtf8, out var aliasMatch, FzfScoringScheme.Default, worker.Slab, worker.ByteBuffers))
                {
                    score = aliasMatch.Score;
                    return true;
                }
            }
            else
            {
                if (worker.AliasScratch.Length < aliasUtf8.Length)
                    worker.AliasScratch = new char[Math.Max(aliasUtf8.Length, worker.AliasScratch.Length * 2)];
                var written = Encoding.UTF8.GetChars(aliasUtf8, worker.AliasScratch);
                if (_segmentPatterns[q].TryMatch(worker.AliasScratch.AsSpan(0, written), out var aliasMatch, FzfScoringScheme.Default, worker.Slab))
                {
                    score = aliasMatch.Score;
                    return true;
                }
            }
        }
        return false;
    }

    // Segments that only exist as text (SourceRoot tokens, delta-path segments) -- no baked aliases
    // to consult, so non-ASCII segments fall back to live alias generation, as the old code did.
    private bool TryMatchSegmentText(string segment, int q, SearchMatcher.Worker worker, out int score)
    {
        score = 0;
        if (_segmentPatterns[q].TryMatch(segment, out var match, FzfScoringScheme.Default, worker.Slab))
        {
            score = match.Score;
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
                    if (_segmentPatterns[q].TryMatch(alias, out var aliasMatch, FzfScoringScheme.Default, worker.Slab))
                    {
                        score = aliasMatch.Score;
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

    // Ranking-only weight computation lives in PathGateWeighting (composition, not a partial class,
    // purely to keep this file under the repo's per-file line limit); these two just forward to it.
    public double ComputeWeight(int parentRow, SearchMatcher.Worker worker) => _weighting.ComputeWeight(parentRow, worker);

    public double ComputeWeightForPath(string path, SearchMatcher.Worker worker) => _weighting.ComputeWeightForPath(path, worker);
}
