using System.Collections.Concurrent;
using Lertaro.Core.Services.Search;

namespace Lertaro.Core.Services.Everything;

/// <summary>Implements IEverythingDataProvider by querying Lertaro's SearchService and space indexers.</summary>
public sealed class EverythingSearchDataProvider : IEverythingDataProvider
{
    private readonly SearchService _searchService;
    private readonly ConcurrentDictionary<string, uint> _runHistory = new(StringComparer.OrdinalIgnoreCase);

    public EverythingSearchDataProvider(SearchService searchService) => _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));

    public async Task<EverythingQueryResult> ExecuteQueryAsync(EverythingQueryRequest request, CancellationToken token = default)
    {
        var criteria = EverythingQueryParser.ParseSearchCriteria(request.SearchString);

        if (criteria.MatchRootsOnly)
        {
            return QueryRootDrives(request);
        }

        if (criteria.IsFolderSubtreeQuery && !string.IsNullOrEmpty(criteria.ParentDirectoryFilter))
        {
            return await QueryFolderSubtreeSizeAsync(criteria.ParentDirectoryFilter, request, token).ConfigureAwait(false);
        }

        if (!string.IsNullOrEmpty(criteria.ParentDirectoryFilter) && string.IsNullOrEmpty(criteria.KeywordQuery) && string.IsNullOrEmpty(criteria.ExtensionFilter))
        {
            return await QueryDirectoryEntriesAsync(criteria, request, token).ConfigureAwait(false);
        }

        return await QueryGeneralSearchAsync(criteria, request, token).ConfigureAwait(false);
    }

    private async Task<EverythingQueryResult> QueryFolderSubtreeSizeAsync(
        string folderPath, EverythingQueryRequest request, CancellationToken token)
    {
        var normalizedPath = NormalizeDirectory(folderPath);
        var spaceEntries = await _searchService.GetSpaceEntriesAsync(normalizedPath, token).ConfigureAwait(false);
        if (spaceEntries.Count == 0)
        {
            return new EverythingQueryResult(Array.Empty<EverythingResultItem>(), 0, 0, 0);
        }

        var totalFolderSize = 0L;
        foreach (var entry in spaceEntries)
        {
            totalFolderSize += entry.Size;
        }

        var folderName = Path.GetFileName(normalizedPath.TrimEnd('\\'));
        if (string.IsNullOrEmpty(folderName))
            folderName = normalizedPath;
        var parentDir = Path.GetDirectoryName(normalizedPath.TrimEnd('\\')) ?? normalizedPath;

        var singleItem = new EverythingResultItem(
            Path: parentDir,
            FileName: folderName,
            Size: Math.Max(0, totalFolderSize),
            IsDirectory: false,
            DateModified: DateTime.UtcNow,
            Attributes: (uint)FileAttributes.Directory);

        return new EverythingQueryResult(new[] { singleItem }, 1, 0, 1);
    }

    private static EverythingQueryResult QueryRootDrives(EverythingQueryRequest request)
    {
        var drives = DriveInfo.GetDrives();
        var items = new List<EverythingResultItem>(drives.Length);

        foreach (var drive in drives)
        {
            try
            {
                if (!drive.IsReady) continue;
                var driveLetter = drive.Name.TrimEnd('\\');
                items.Add(new EverythingResultItem(
                    Path: drive.Name,
                    FileName: driveLetter,
                    Size: drive.TotalSize,
                    IsDirectory: true,
                    IsDrive: true,
                    Attributes: (uint)FileAttributes.Directory));
            }
            catch
            {
                // Drive access error
            }
        }

        SortItems(items, request.SortType);
        var totalItems = (uint)items.Count;
        var pagedItems = ApplyPagination(items, request.Offset, request.MaxResults);
        return new EverythingQueryResult(pagedItems, totalItems, totalItems, 0);
    }

    private async Task<EverythingQueryResult> QueryDirectoryEntriesAsync(
        EverythingSearchCriteria criteria, EverythingQueryRequest request, CancellationToken token)
    {
        var parentDir = NormalizeDirectory(criteria.ParentDirectoryFilter ?? string.Empty);
        var spaceEntries = await _searchService.GetSpaceEntriesAsync(parentDir, token).ConfigureAwait(false);

        var items = new List<EverythingResultItem>(spaceEntries.Count);
        foreach (var entry in spaceEntries)
        {
            if (criteria.MatchFoldersOnly && !entry.IsDirectory) continue;
            if (criteria.MatchFilesOnly && entry.IsDirectory) continue;

            if (!string.IsNullOrEmpty(criteria.KeywordQuery) &&
                !entry.Name.Contains(criteria.KeywordQuery, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrEmpty(criteria.ExtensionFilter) && !entry.IsDirectory)
            {
                var ext = Path.GetExtension(entry.Name).TrimStart('.');
                if (!criteria.ExtensionFilter.Equals(ext, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            var dir = Path.GetDirectoryName(entry.Path);
            if (string.IsNullOrEmpty(dir))
                dir = parentDir;

            items.Add(new EverythingResultItem(
                Path: dir,
                FileName: entry.Name,
                Size: entry.Size,
                IsDirectory: entry.IsDirectory,
                Attributes: entry.IsDirectory ? (uint)FileAttributes.Directory : (uint)FileAttributes.Normal,
                RunCount: GetRunCount(entry.Path)));
        }

        SortItems(items, request.SortType);

        var totalItems = (uint)items.Count;
        var totalFolders = (uint)items.Count(i => i.IsDirectory);
        var totalFiles = totalItems - totalFolders;

        var pagedItems = ApplyPagination(items, request.Offset, request.MaxResults);
        return new EverythingQueryResult(pagedItems, totalItems, totalFolders, totalFiles);
    }

    private async Task<EverythingQueryResult> QueryGeneralSearchAsync(
        EverythingSearchCriteria criteria, EverythingQueryRequest request, CancellationToken token)
    {
        var keyword = string.IsNullOrWhiteSpace(criteria.KeywordQuery) ? criteria.RawQuery : criteria.KeywordQuery;
        if (string.IsNullOrWhiteSpace(keyword))
        {
            keyword = "*";
        }

        var directoryFilter = !string.IsNullOrEmpty(criteria.ParentDirectoryFilter) && !criteria.ParentDirectoryFilter.StartsWith("?:", StringComparison.OrdinalIgnoreCase)
            ? NormalizeDirectory(criteria.ParentDirectoryFilter)
            : null;

        var needed = request.MaxResults == EverythingIpcConstants.AllResults
            ? int.MaxValue
            : (int)Math.Min((long)request.MaxResults + request.Offset, 100000);

        var results = new List<SearchResult>();
        await _searchService.SearchStreamingAsync(
            query: keyword,
            maxResults: needed,
            maxAppResults: 0,
            directoryFilter: directoryFilter,
            onResult: res =>
            {
                if (criteria.MatchFoldersOnly && !res.IsDir) return;
                if (criteria.MatchFilesOnly && res.IsDir) return;
                if (!string.IsNullOrEmpty(criteria.ExtensionFilter) && !res.IsDir)
                {
                    var ext = Path.GetExtension(res.Path).TrimStart('.');
                    if (!criteria.ExtensionFilter.Equals(ext, StringComparison.OrdinalIgnoreCase)) return;
                }
                lock (results)
                {
                    results.Add(res);
                }
            },
            token: token,
            bypassExclusions: false).ConfigureAwait(false);

        results.Sort(SearchResultRankComparer.Instance);

        if (request.SortType is > EverythingIpcConstants.SortNameAscending)
        {
            SortSearchResults(results, request.SortType);
        }

        var totalItems = (uint)results.Count;
        var totalFolders = (uint)results.Count(i => i.IsDir);
        var totalFiles = totalItems - totalFolders;

        var pagedResults = ApplyPagination(results, request.Offset, request.MaxResults);
        var pagedItems = new List<EverythingResultItem>(pagedResults.Count);
        foreach (var res in pagedResults)
        {
            var dir = Path.GetDirectoryName(res.Path) ?? string.Empty;
            var fileName = Path.GetFileName(res.Path);
            if (string.IsNullOrEmpty(fileName))
                fileName = !string.IsNullOrEmpty(res.Name) ? res.Name : res.Path;

            var modified = res.Metadata.Modified != DateTime.MinValue ? res.Metadata.Modified : (DateTime?)null;
            var created = res.Metadata.Created != DateTime.MinValue ? res.Metadata.Created : (DateTime?)null;
            var accessed = res.Metadata.Accessed != DateTime.MinValue ? res.Metadata.Accessed : (DateTime?)null;

            pagedItems.Add(new EverythingResultItem(
                Path: dir,
                FileName: fileName,
                Size: res.Metadata.Size,
                IsDirectory: res.IsDir,
                DateCreated: created,
                DateModified: modified,
                DateAccessed: accessed,
                Attributes: res.Attributes != 0 ? (uint)res.Attributes : (res.IsDir ? (uint)FileAttributes.Directory : (uint)FileAttributes.Normal),
                RunCount: GetRunCount(res.Path)));
        }

        return new EverythingQueryResult(pagedItems, totalItems, totalFolders, totalFiles);
    }

    private static void SortSearchResults(List<SearchResult> items, uint sortType)
    {
        Comparison<SearchResult>? comparison = sortType switch
        {
            EverythingIpcConstants.SortNameDescending => (a, b) => SearchResultRankComparer.Instance.Compare(b, a),
            EverythingIpcConstants.SortPathAscending => (a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase),
            EverythingIpcConstants.SortPathDescending => (a, b) => string.Compare(b.Path, a.Path, StringComparison.OrdinalIgnoreCase),
            EverythingIpcConstants.SortSizeAscending => (a, b) => a.Metadata.Size.CompareTo(b.Metadata.Size),
            EverythingIpcConstants.SortSizeDescending => (a, b) => b.Metadata.Size.CompareTo(a.Metadata.Size),
            EverythingIpcConstants.SortDateCreatedAscending => (a, b) => a.Metadata.Created.CompareTo(b.Metadata.Created),
            EverythingIpcConstants.SortDateCreatedDescending => (a, b) => b.Metadata.Created.CompareTo(a.Metadata.Created),
            EverythingIpcConstants.SortDateModifiedAscending => (a, b) => a.Metadata.Modified.CompareTo(b.Metadata.Modified),
            EverythingIpcConstants.SortDateModifiedDescending => (a, b) => b.Metadata.Modified.CompareTo(a.Metadata.Modified),
            EverythingIpcConstants.SortAttributesAscending => (a, b) => a.Attributes.CompareTo(b.Attributes),
            EverythingIpcConstants.SortAttributesDescending => (a, b) => b.Attributes.CompareTo(a.Attributes),
            _ => null
        };

        if (comparison != null)
            items.Sort(comparison);
    }

    private static void SortItems(List<EverythingResultItem> items, uint sortType)
    {
        Comparison<EverythingResultItem>? comparison = sortType switch
        {
            EverythingIpcConstants.SortNameDescending => (a, b) => string.Compare(b.FileName, a.FileName, StringComparison.OrdinalIgnoreCase),
            EverythingIpcConstants.SortPathAscending => (a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase),
            EverythingIpcConstants.SortPathDescending => (a, b) => string.Compare(b.Path, a.Path, StringComparison.OrdinalIgnoreCase),
            EverythingIpcConstants.SortSizeAscending => (a, b) => a.Size.CompareTo(b.Size),
            EverythingIpcConstants.SortSizeDescending => (a, b) => b.Size.CompareTo(a.Size),
            EverythingIpcConstants.SortDateCreatedAscending => (a, b) => Nullable.Compare(a.DateCreated, b.DateCreated),
            EverythingIpcConstants.SortDateCreatedDescending => (a, b) => Nullable.Compare(b.DateCreated, a.DateCreated),
            EverythingIpcConstants.SortDateModifiedAscending => (a, b) => Nullable.Compare(a.DateModified, b.DateModified),
            EverythingIpcConstants.SortDateModifiedDescending => (a, b) => Nullable.Compare(b.DateModified, a.DateModified),
            EverythingIpcConstants.SortAttributesAscending => (a, b) => a.Attributes.CompareTo(b.Attributes),
            EverythingIpcConstants.SortAttributesDescending => (a, b) => b.Attributes.CompareTo(a.Attributes),
            _ => (a, b) => string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase)
        };

        if (comparison != null)
            items.Sort(comparison);
    }

    private static IReadOnlyList<T> ApplyPagination<T>(List<T> items, uint offset, uint maxResults)
    {
        if (offset >= items.Count) return Array.Empty<T>();
        var count = maxResults == EverythingIpcConstants.AllResults
            ? items.Count - (int)offset
            : Math.Min((int)maxResults, items.Count - (int)offset);
        return count <= 0 ? Array.Empty<T>() : items.GetRange((int)offset, count);
    }

    public uint GetRunCount(string fileName) =>
        _runHistory.TryGetValue(fileName, out var count) ? count : 0;

    public void SetRunCount(string fileName, uint count) =>
        _runHistory[fileName] = count;

    public uint IncrementRunCount(string fileName) =>
        _runHistory.AddOrUpdate(fileName, 1, (_, current) => current + 1);

    private static string NormalizeDirectory(string dir)
    {
        var trimmed = dir.Trim().Trim('"', '<', '>');
        if (trimmed.Length == 2 && trimmed[1] == ':')
            return trimmed + "\\";
        return trimmed;
    }
}
