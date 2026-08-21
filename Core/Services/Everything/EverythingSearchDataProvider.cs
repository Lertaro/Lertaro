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

        if (!string.IsNullOrEmpty(criteria.ParentDirectoryFilter))
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

        SortResults(items, request.SortType);
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

        SortResults(items, request.SortType);

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
            return new EverythingQueryResult(Array.Empty<EverythingResultItem>(), 0, 0, 0);
        }

        var limit = request.MaxResults == EverythingIpcConstants.AllResults
            ? int.MaxValue
            : (int)Math.Min(request.MaxResults + request.Offset, 100000);

        var results = new List<SearchResult>();
        await _searchService.SearchStreamingAsync(
            query: keyword,
            maxResults: limit,
            maxAppResults: 0,
            directoryFilter: null,
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

        var items = new List<EverythingResultItem>(results.Count);
        foreach (var res in results)
        {
            var dir = Path.GetDirectoryName(res.Path) ?? string.Empty;
            var fileName = Path.GetFileName(res.Path);
            if (string.IsNullOrEmpty(fileName))
                fileName = !string.IsNullOrEmpty(res.Name) ? res.Name : res.Path;

            var modified = res.Metadata.Modified != DateTime.MinValue ? res.Metadata.Modified : (DateTime?)null;
            var created = res.Metadata.Created != DateTime.MinValue ? res.Metadata.Created : (DateTime?)null;
            var accessed = res.Metadata.Accessed != DateTime.MinValue ? res.Metadata.Accessed : (DateTime?)null;

            items.Add(new EverythingResultItem(
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

        SortResults(items, request.SortType);

        var totalItems = (uint)items.Count;
        var totalFolders = (uint)items.Count(i => i.IsDirectory);
        var totalFiles = totalItems - totalFolders;

        var pagedItems = ApplyPagination(items, request.Offset, request.MaxResults);
        return new EverythingQueryResult(pagedItems, totalItems, totalFolders, totalFiles);
    }

    private static void SortResults(List<EverythingResultItem> items, uint sortType)
    {
        switch (sortType)
        {
            case EverythingIpcConstants.SortNameAscending:
                items.Sort((a, b) => string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase));
                break;
            case EverythingIpcConstants.SortNameDescending:
                items.Sort((a, b) => string.Compare(b.FileName, a.FileName, StringComparison.OrdinalIgnoreCase));
                break;
            case EverythingIpcConstants.SortPathAscending:
                items.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
                break;
            case EverythingIpcConstants.SortPathDescending:
                items.Sort((a, b) => string.Compare(b.Path, a.Path, StringComparison.OrdinalIgnoreCase));
                break;
            case EverythingIpcConstants.SortSizeAscending:
                items.Sort((a, b) => a.Size.CompareTo(b.Size));
                break;
            case EverythingIpcConstants.SortSizeDescending:
                items.Sort((a, b) => b.Size.CompareTo(a.Size));
                break;
            case EverythingIpcConstants.SortDateModifiedAscending:
                items.Sort((a, b) => Nullable.Compare(a.DateModified, b.DateModified));
                break;
            case EverythingIpcConstants.SortDateModifiedDescending:
                items.Sort((a, b) => Nullable.Compare(b.DateModified, a.DateModified));
                break;
        }
    }

    private static IReadOnlyList<EverythingResultItem> ApplyPagination(List<EverythingResultItem> items, uint offset, uint maxResults)
    {
        if (offset >= items.Count) return Array.Empty<EverythingResultItem>();
        var count = maxResults == EverythingIpcConstants.AllResults
            ? items.Count - (int)offset
            : Math.Min((int)maxResults, items.Count - (int)offset);
        return count <= 0 ? Array.Empty<EverythingResultItem>() : items.GetRange((int)offset, count);
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
