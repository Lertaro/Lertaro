using Lertaro.Core.Indexer.Usn;

using Lertaro.Core.Services.Network;

using Lertaro.Core.Wire;
using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Query;
namespace Lertaro.Core.Services.Search;

public class SearchService : IDisposable
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<List<SearchResult>>> _sessionDirectoryCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ScopeLiveSearchCache _scopeLiveSearchCache = new();
    private readonly CancellationTokenSource _cacheFillCts = new();
    private readonly SearchPipeClient _pipeClient = new();
    private int _disposed;

    public Task<UsnIndexer.IndexerStatus> GetStatusAsync(CancellationToken token = default) => _pipeClient.GetStatusAsync(token);

    public Task<bool> PingAsync(CancellationToken token = default) => _pipeClient.PingAsync(token);

    // Asks the already-running --service instance to spawn the hook process directly into this caller's
    // own session (see HookProcessBroker) -- the App itself never launches the hook process anymore, so
    // it never has a "runas" UAC prompt of its own to show. requestElevation is only honored server-side
    // when that session's user is genuinely an administrator; otherwise it just launches non-elevated.
    public Task<(bool Ok, int Pid, string? Error)> RequestHookLaunchAsync(bool requestElevation, CancellationToken token = default)
        => _pipeClient.RequestHookLaunchAsync(requestElevation, token);

    // Fire-and-forget, called whenever a search window closes/hides (mirrors ShellIconHelper.ClearCache()'s
    // existing trigger points) -- gives back the local drives' per-row full-path memo, which otherwise
    // only self-clears once it crosses its own high backstop threshold (see PathQueryExtensions).
    public Task ClearPathCachesAsync(CancellationToken token = default) => _pipeClient.ClearPathCachesAsync(token);

    // bypassExclusions: opts this one search out of ExcludedPaths/IgnoredPathGlobs/IgnoredPathRegexes
    // filtering. The caller is responsible for stripping whatever query-string marker triggers this
    // (see SearchQuerySortParser.StripExclusionBypass) BEFORE calling here -- `query` itself is always
    // matched/highlighted verbatim, so a caller must never pass the marker through as part of it. Also
    // forced on automatically for a path-mode query (see effectiveBypassExclusions below) -- typing an
    // exact path is the same "I want to see what's actually here" intent regardless of the marker. Only
    // covers results that are already indexed -- content that was never indexed in the first place (an
    // excluded/unconfigured network or WSL root, or a local drive not enabled for indexing) has nothing
    // to unfilter here; recovering that is CheckNeedsLiveSearch's live-scan fallback's job instead.
    public async Task<bool> SearchStreamingAsync(string query, int maxResults, int maxAppResults, string? directoryFilter, Action<SearchResult> onResult, CancellationToken token = default, Action? onLocalSearchFailed = null, bool bypassExclusions = false)
    {
        var settings = UserSettings.Load();
        var exclusionRules = ExclusionRuleSet.From(settings);
        // No longer clamped to 2000. The service returns everything that matches and the caller decides
        // what to do with it; asking for a multiple of maxResults existed only to leave headroom for the
        // exclusion filtering below, which is pointless once maxResults is itself unbounded.
        var fileCandidateLimit = maxResults >= int.MaxValue / 4 ? int.MaxValue : Math.Max(maxResults * 4, maxResults);

        var isSearchDir = !string.IsNullOrEmpty(directoryFilter);
        var msg = new SearchRequestMessage
        {
            Id = isSearchDir ? SearchRequestId.SearchDir : SearchRequestId.Search,
            Limit = fileCandidateLimit,
            AppLimit = maxAppResults,
            DirectoryFilter = isSearchDir ? directoryFilter : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Query = query,
            ExactMatch = !settings.EnableFuzzyMatch,
            DisabledAliasComponents = settings.DisabledPluginComponents
                .Where(c => c.Contains("::AliasProvider::", StringComparison.OrdinalIgnoreCase))
                .ToList()
        };

        HashSet<byte>? disabledIds = null;
        if (msg.DisabledAliasComponents != null && msg.DisabledAliasComponents.Count > 0)
        {
            disabledIds = new HashSet<byte>();
            foreach (var comp in msg.DisabledAliasComponents)
            {
                var id = AliasProviderRegistry.GetProviderIdByComponentId(comp);
                if (id != 255)
                    disabledIds.Add(id);
            }
        }
        SearchContext.DisabledAliasIds = disabledIds;
        // Applies to the sources this process searches itself (network drives, live directory scans);
        // the local-drive path carries the same flag over the pipe instead -- see SearchStreamPump.
        SearchContext.FuzzyMatchEnabled = !msg.ExactMatch;

        var parsed = SearchQueryParser.Parse(query);
        // A path-mode query ("C:\Windows\...") is the user typing an exact location they want to look at,
        // same as ExplorerSearchHelper's "current folder" search always passing bypassExclusions: true for
        // the directory the user is actually standing in -- global exclusion settings have no business
        // hiding results from a location the user explicitly pointed at either way.
        var effectiveBypassExclusions = bypassExclusions || parsed.IsPathMode;

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var uniqueOnResult = new Action<SearchResult>(result =>
        {
            // Unconditional, even in bypass mode: "*" only opts out of the user's own
            // ExcludedPaths/Globs/Regexes configuration, not hidden/system attributes -- those are a
            // separate, always-on filter.
            if (FileSystemItemFilter.IsHiddenOrSystem(result))
                return;

            lock (seenPaths)
            {
                if (!seenPaths.Add(result.Path))
                    return;
            }
            onResult(result);
        });

        var localTask = Task.Run(async () =>
        {
            try
            {
                await SearchPipeClient.SendSearchPipeCommandAsync(msg, result =>
                {
                    if (effectiveBypassExclusions || !exclusionRules.IsExcluded(result, directoryFilter))
                        uniqueOnResult(result);
                }, token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Log($"[SearchService] Streaming local search failed: {ex.Message}", LogLevel.Error);
                onLocalSearchFailed?.Invoke();
                return false;
            }
        }, token);

        var networkTask = Task.Run(() =>
        {
            try
            {
                return SearchServiceHelper.SearchNetworkDrives(query, fileCandidateLimit, directoryFilter, exclusionRules, effectiveBypassExclusions, uniqueOnResult, token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Log($"[SearchService] Network drive search failed: {ex.Message}", LogLevel.Error);
                return false;
            }
        }, token);

        var needsLiveSearch = false;
        var liveScanDir = string.Empty;
        var liveScanFilter = string.Empty;

        if (parsed.IsPathMode && !string.IsNullOrEmpty(parsed.ExactPathLower))
        {
            var resolved = LiveDirectorySearcher.ResolvePathModeSearch(parsed.ExactPathLower);
            if (!string.IsNullOrEmpty(resolved.DirectoryToScan) && SearchServiceHelper.CheckNeedsLiveSearch(resolved.DirectoryToScan, exclusionRules))
            {
                needsLiveSearch = true;
                liveScanDir = resolved.DirectoryToScan;
                liveScanFilter = resolved.FilterQuery;
            }
        }
        else if (!string.IsNullOrEmpty(directoryFilter) && _scopeLiveSearchCache.GetOrAdd(directoryFilter,
            dir => SearchServiceHelper.CheckNeedsLiveSearch(dir, exclusionRules) && Directory.Exists(dir)))
        {
            needsLiveSearch = true;
            liveScanDir = directoryFilter;
            liveScanFilter = query;
        }

        Task<bool>? liveTask = null;
        if (needsLiveSearch && !string.IsNullOrEmpty(liveScanDir))
        {
            var cacheFillToken = _cacheFillCts.Token;
            liveTask = Task.Run(async () =>
            {
                try
                {
                    // GetOrAdd shares ONE in-flight scan per directory across every caller currently
                    // waiting on it, instead of the old lock(this) around the whole SearchService
                    // instance -- that lock serialized every live scan regardless of which directory it
                    // targeted, so typing into a directory that needs one (excluded from the index, an
                    // unconfigured network drive, ...) queued every subsequent keystroke's own scan
                    // attempt behind whichever one happened to go first, none of which could even check
                    // their own cancellation token until they finally got the lock. The shared scan keeps
                    // running when this one query is superseded so the next query in this window can reuse
                    // it, but the SearchService owns its lifetime and cancels it when the window closes.
                    if (_sessionDirectoryCache.Count > 32)
                        _sessionDirectoryCache.Clear();
                    var onlyDirectChildren = parsed.IsPathMode && string.IsNullOrEmpty(liveScanFilter);
                    // liveQuery/onLiveMatch only actually fire for whichever caller wins the GetOrAdd race
                    // (i.e. triggers the scan for real) -- a directory this large/uncached is exactly the
                    // "current folder" case that used to sit with zero results until the entire subtree
                    // walk finished; streaming matches out as each directory is walked keeps the first,
                    // cold keystroke from looking frozen even though the underlying walk cost is unchanged.
                    var scanTask = _sessionDirectoryCache.GetOrAdd(liveScanDir,
                        dir => Task.Run(() => LiveDirectorySearcher.ScanDirectory(dir, 10000, cacheFillToken,
                            liveQuery: liveScanFilter, onLiveMatch: uniqueOnResult,
                            onlyDirectChildren: onlyDirectChildren, parentPath: liveScanDir)));
                    List<SearchResult> entries;
                    try
                    {
                        entries = await scanTask.WaitAsync(token).ConfigureAwait(false);
                    }
                    catch (Exception) when (scanTask.IsFaulted)
                    {
                        // Do not let a faulted live-scan task poison the cache for every later keystroke.
                        _sessionDirectoryCache.TryRemove(new KeyValuePair<string, Task<List<SearchResult>>>(liveScanDir, scanTask));
                        throw;
                    }

                    return LiveDirectorySearcher.MatchAndStream(entries, liveScanFilter, uniqueOnResult, token, onlyDirectChildren, liveScanDir);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Logger.Log($"[SearchService] Live directory search failed: {ex.Message}", LogLevel.Error);
                    return false;
                }
            }, token);
        }

        var tasks = new List<Task<bool>> { localTask, networkTask };
        if (liveTask != null) tasks.Add(liveTask);

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.Any(r => r);
    }

    // In-memory index lookup only (no disk I/O) -- the most recently modified entries across all of the
    // given directories' subtrees, most recent first. The elevated service only tracks local drive
    // letters, so network/WSL directories are queried in-process here (same split as SearchStreamingAsync's
    // localTask/networkTask) and merged by actual modification time rather than just concatenated.
    public async Task<List<SearchResult>> GetRecentFilesAsync(IReadOnlyList<string> directories, int limit, int maxAgeMinutes, CancellationToken token = default)
    {
        var networkTask = Task.Run(() =>
        {
            try
            {
                return UserNetworkDriveSearch.GetRecentFiles(directories, limit, maxAgeMinutes);
            }
            catch (Exception ex)
            {
                Logger.Log($"[SearchService] Network drive GetRecentFiles failed: {ex.Message}", LogLevel.Error);
                return new List<SearchResult>();
            }
        }, token);

        var resp = await SendPipeCommandAsync(new SearchRequestMessage { Id = SearchRequestId.GetRecentFiles, Directories = directories.ToList(), Limit = limit, MaxAgeMinutes = maxAgeMinutes }, token).ConfigureAwait(false);
        if (resp.Kind == PipeResponseKind.Error) Logger.Log($"[SearchService] GetRecentFiles failed: {resp.Message}", LogLevel.Error);
        var localResults = resp.Kind == PipeResponseKind.RecentFiles && resp.RecentFiles != null ? resp.RecentFiles : new List<SearchResult>();

        var networkResults = await networkTask.ConfigureAwait(false);
        var merged = localResults.Concat(networkResults).OrderByDescending(r => r.Metadata.Modified);
        return (limit > 0 ? merged.Take(limit) : merged).ToList();
    }

    // Forwards to the pipe client -- kept on SearchService itself since SearchServiceManagementExtensions
    // and other callers already reach this as an instance method (`service.SendPipeCommandAsync(...)`).
    internal Task<PipeResponse> SendPipeCommandAsync(SearchRequestMessage msg, CancellationToken token)
        => _pipeClient.SendPipeCommandAsync(msg, token);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _cacheFillCts.Cancel();
        _cacheFillCts.Dispose();
        GC.SuppressFinalize(this);
    }
}
