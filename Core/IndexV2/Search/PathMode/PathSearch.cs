using Lertaro.Core.SearchIndex.Fzf;

using Lertaro.Core.IndexV2.Delta;

using Lertaro.Core.IndexV2.Persistence;
using Lertaro.Core.SearchIndex.Query;
namespace Lertaro.Core.IndexV2.Search.PathMode;

// Path-mode search entry point + exact-path navigation, mirroring PathExtensions.SearchPath /
// TrySearchDirectoryChildren. Fuzzy dir+file matching lives in PathSearchFuzzy (split to stay under
// the file-length convention).
internal static class PathSearch
{
    public static void SearchStreaming(Snapshot snapshot, DeltaOverlay delta, ParsedSearchQuery parsed, int limit,
        Action<SearchResult> onResult, CancellationToken token, string? directoryFilterLower)
    {
        if (TryDirectoryChildren(snapshot, delta, parsed, limit, onResult, token))
            return;

        if (parsed.TargetDrive != null && !parsed.TargetDrive.Equals(snapshot.SourceKey, StringComparison.OrdinalIgnoreCase))
            return;

        PathSearchFuzzy.SearchStreaming(snapshot, delta, parsed.PathPatternLower ?? string.Empty, limit, onResult, token, directoryFilterLower);
    }

    // "t:\a\b\" lists children; "t:\a\b\pre" filters them by the last segment as a name prefix query.
    private static bool TryDirectoryChildren(Snapshot snapshot, DeltaOverlay delta, ParsedSearchQuery parsed, int limit, Action<SearchResult> onResult, CancellationToken token)
    {
        if (parsed.ExactPathLower == null || parsed.TargetDrive == null)
            return false;
        if (!DirectoryFilterResolver.TryResolve(snapshot, delta, parsed.ExactPathLower, forceLastSegmentAsQuery: !parsed.PathEndsWithSeparator, out var current, out var childPrefix))
            return false;

        // See NameSearch: bounded by the index, and widened so a large limit cannot overflow.
        var keep = (int)Math.Min((long)Math.Max(limit, 8) * 8, snapshot.Count + delta.Added.Count);
        var matches = new FzfTopN(keep);

        if (childPrefix.Length == 0 && !delta.IsSuperseded(current))
        {
            matches.Add(FzfResultRank.ForDefaultScheme(current, delta.NameOf(current), new FzfPatternResult(0, 0, 0, 0, false)));
        }

        var pattern = childPrefix.Length == 0 ? null : FzfPattern.ParseText(childPrefix);
        var queryLen = pattern?.GetTotalTermLength() ?? 0;
        var slab = new FzfSlab();
        var aliasScratch = new List<(string Alias, byte ProviderId)>();

        foreach (var child in snapshot.ChildrenOf(current))
        {
            if (snapshot.IsDeleted(child) || delta.IsSuperseded(child))
                continue;
            if (pattern == null)
            {
                matches.Add(FzfResultRank.ForDefaultScheme(child, snapshot.GetName(child), new FzfPatternResult(0, 0, 0, 0, false)));
                continue;
            }
            if (!SearchMatcherRow.MatchRow(snapshot, child, pattern, queryLen, slab, aliasScratch, out var name, out var match))
                continue;
            matches.Add(FzfResultRank.ForDefaultScheme(child, name, match));
        }

        // Rows the base CSR wouldn't show yet: renamed-in/moved-in overrides and freshly added children.
        foreach (var (row, record) in delta.BaseOverrides)
        {
            if (record.ParentBaseRow != current || record.Name.Length == 0)
                continue;
            if (pattern != null && !SearchMatcherRow.TryMatchNameOrAliases(pattern, record.Name, record.Aliases, record.ProviderIds, queryLen, slab, out var match))
                continue;
            matches.Add(FzfResultRank.ForDefaultScheme(row, record.Name, new FzfPatternResult(0, 0, 0, 0, false)));
        }
        var currentFrn = snapshot.Ids[current];
        for (var i = 0; i < delta.Added.Count; i++)
        {
            var record = delta.Added[i];
            if (record.Removed || record.ParentFrn != currentFrn || record.Name.Length == 0)
                continue;
            if (pattern != null && !SearchMatcherRow.TryMatchNameOrAliases(pattern, record.Name, record.Aliases, record.ProviderIds, queryLen, slab, out _))
                continue;
            matches.Add(FzfResultRank.ForDefaultScheme(snapshot.Count + i, record.Name, new FzfPatternResult(0, 0, 0, 0, false)));
        }

        var seen = new HashSet<int>();
        var emitted = 0;
        foreach (var rank in matches.Finish(keep))
        {
            token.ThrowIfCancellationRequested();
            if (!seen.Add(rank.EntryIndex))
                continue;
            onResult(ResultBuilder.ToResult(snapshot, delta, rank));
            if (++emitted >= limit)
                break;
        }
        return true;
    }
}
