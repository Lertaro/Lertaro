using Lertaro.Core.SearchIndex.Fzf;

using Lertaro.Core.IndexV2.Delta;

using Lertaro.Core.IndexV2.Persistence;
using Lertaro.Core.SearchIndex.Query;
using Lertaro.Core.Services.Plugin.DirectoryIndex;
using Lertaro.PluginSdk.Helpers;
namespace Lertaro.Core.IndexV2.Search.PathMode;

// Path-mode search entry point + exact-path navigation, mirroring PathExtensions.SearchPath /
// TrySearchDirectoryChildren. Fuzzy dir+file matching lives in PathSearchFuzzy (split to stay under
// the file-length convention).
internal static class PathSearch
{
    public static void SearchStreaming(Snapshot snapshot, DeltaOverlay delta, ParsedSearchQuery parsed, int limit,
        Action<SearchResult> onResult, CancellationToken token, string? directoryFilterLower,
        string[]? fileNamePatterns = null, SearchCategoryFilter category = SearchCategoryFilter.All)
    {
        if (TryDirectoryChildren(snapshot, delta, parsed, limit, onResult, token, directoryFilterLower, fileNamePatterns, category))
            return;

        if (parsed.TargetDrive != null && !parsed.TargetDrive.Equals(snapshot.SourceKey, StringComparison.OrdinalIgnoreCase))
            return;

        PathSearchFuzzy.SearchStreaming(snapshot, delta, parsed.PathPatternLower ?? string.Empty, limit, onResult, token, directoryFilterLower, fileNamePatterns, category);
    }

    // "t:\a\b\" lists children; "t:\a\b\pre" filters them by the last segment as a name prefix query.
    private static bool TryDirectoryChildren(Snapshot snapshot, DeltaOverlay delta, ParsedSearchQuery parsed, int limit,
        Action<SearchResult> onResult, CancellationToken token, string? directoryFilterLower, string[]? fileNamePatterns, SearchCategoryFilter category)
    {
        if (parsed.ExactPathLower == null || parsed.TargetDrive == null)
            return false;
        if (!DirectoryFilterResolver.TryResolve(snapshot, delta, parsed.ExactPathLower, forceLastSegmentAsQuery: !parsed.PathEndsWithSeparator, out var current, out var childPrefix))
            return false;

        // See NameSearch: bounded by the index, and widened so a large limit cannot overflow.
        var keep = (int)Math.Min((long)Math.Max(limit, 8) * 8, snapshot.Count + delta.Added.Count);
        var matches = new FzfTopN(keep);

        if (childPrefix.Length == 0 && !DirectoryFilterResolver.IsVisiblyDeleted(snapshot, delta, current) && Admits(current))
        {
            matches.Add(FzfResultRank.ForDefaultScheme(current, DirectoryFilterResolver.GetName(snapshot, delta, current), new FzfPatternResult(0, 0, 0, 0, false)));
        }

        var pattern = childPrefix.Length == 0 ? null : FzfPattern.ParseText(childPrefix);
        var queryLen = pattern?.GetTotalTermLength() ?? 0;
        var slab = new FzfSlab();
        var aliasScratch = new List<(string Alias, byte ProviderId)>();

        if (current < snapshot.Count)
        {
            foreach (var child in snapshot.ChildrenOf(current))
            {
                token.ThrowIfCancellationRequested();
                if (snapshot.IsDeleted(child) || delta.IsSuperseded(child))
                    continue;
                if (!Admits(child)) continue;
                AddBaseMatch(child);
            }
        }

        var deltaChildren = DeltaChildLookup.Build(snapshot, delta);
        if (deltaChildren != null)
        {
            var children = current < snapshot.Count
                ? deltaChildren.ChildrenOfRow(current)
                : deltaChildren.ChildrenOfFrn(delta.Added[current - snapshot.Count].Id);
            foreach (var child in children)
            {
                token.ThrowIfCancellationRequested();
                if (DirectoryFilterResolver.IsSuperseded(snapshot, delta, child))
                    continue;
                if (!Admits(child)) continue;
                AddDeltaMatch(child);
            }
        }

        bool Admits(int child)
        {
            if (fileNamePatterns == null && category == SearchCategoryFilter.All && directoryFilterLower == null) return true;
            var flags = child >= snapshot.Count ? delta.Added[child - snapshot.Count].Flags
                : delta.BaseOverrides.TryGetValue(child, out var record) ? record.Flags : snapshot.Flags[child];
            if (!FilterPatternHelper.MatchesSearchResult(DirectoryFilterResolver.GetName(snapshot, delta, child),
                (flags & (ushort)FileRecordFlags.Directory) != 0, fileNamePatterns, category)) return false;
            if (directoryFilterLower == null) return true;
            var path = child >= snapshot.Count ? delta.GetFullPath(delta.Added[child - snapshot.Count]) : delta.GetFullPath(child);
            return path.StartsWith(directoryFilterLower, StringComparison.OrdinalIgnoreCase);
        }

        void AddBaseMatch(int child)
        {
            if (pattern == null)
            {
                matches.Add(FzfResultRank.ForDefaultScheme(child, snapshot.GetName(child), new FzfPatternResult(0, 0, 0, 0, false)));
                return;
            }
            if (SearchMatcherRow.MatchRow(snapshot, child, pattern, queryLen, slab, aliasScratch, out var name, out var match))
                matches.Add(FzfResultRank.ForDefaultScheme(child, name, match));
        }

        void AddDeltaMatch(int child)
        {
            var name = DirectoryFilterResolver.GetName(snapshot, delta, child);
            if (name.Length == 0)
                return;
            if (pattern != null)
            {
                var record = child >= snapshot.Count ? delta.Added[child - snapshot.Count] : delta.BaseOverrides[child];
                if (!SearchMatcherRow.TryMatchNameOrAliases(pattern, name, record.Aliases, record.ProviderIds, queryLen, slab, out var match))
                    return;
                matches.Add(FzfResultRank.ForDefaultScheme(child, name, match));
                return;
            }
            matches.Add(FzfResultRank.ForDefaultScheme(child, name, new FzfPatternResult(0, 0, 0, 0, false)));
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
