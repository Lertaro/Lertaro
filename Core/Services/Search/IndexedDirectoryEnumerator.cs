using Lertaro.Core.Services.Network;

using Lertaro.Core.Services.Plugin.DirectoryIndex;

using Lertaro.Core.Wire;
namespace Lertaro.Core.Services.Search;

/// <summary>
/// Client half of "list this directory without touching the disk": streams a directory's entries out of
/// whichever index holds it, and walks the real filesystem only for a directory none of them does.
/// <para>
/// There are two places an index can live, the same split search itself works with (see
/// <c>SearchService.SearchStreamingAsync</c>'s local/network tasks): local drives are indexed by the
/// elevated service and reached over the pipe, while network, WSL and folder indexes live in THIS
/// process. A local drive enabled for indexing is indexed in full, so it is asked first and answers on
/// its own; everything else falls through to the in-process indexes, and only then to the disk. A
/// folder index is what makes that last hop worth taking for a local path the service doesn't cover.
/// </para>
/// <para>
/// The user's exclusion settings deliberately play no part here: the caller named one exact directory,
/// which is the same "show me what is actually in this place" intent that already bypasses them for a
/// path-mode query. Hidden and system entries ARE dropped though, exactly as they are for every other
/// search result -- that filter is a separate, always-on one, not part of those settings. It applies to
/// an entry's own attributes only: a hidden directory is still walked through, just never returned.
/// </para>
/// <para>
/// Every call re-decides on its own, and nothing about a fallback is remembered: a directory answered
/// by a live walk today (index still building, service restarting) is answered from the index the
/// moment the index can answer it, with no cache to invalidate and no state to reset.
/// </para>
/// </summary>
public static class IndexedDirectoryEnumerator
{
    // limit <= 0 means "everything". Note it bounds RESULTS, not work: an index-side walk still visits
    // the subtree until that many entries have passed the filters.
    public static async Task EnumerateAsync(string directoryPath, bool recursive, string filterPattern,
        Action<SearchResult> onResult, int limit = 0, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            return;
        var path = NormalizeDirectoryPath(directoryPath);

        // A local drive the service indexes: asked first and preferred over any in-process index that
        // also happens to cover the path (a folder index nested inside it), since the service's copy is
        // the USN-live, whole-volume one. Skipped outright for a network/UNC path -- the service only
        // ever holds local drives, so that would be a round trip that cannot succeed.
        if (!IsInProcessIndexedSource(path)
            && !SearchServiceHelper.CheckNeedsLiveSearch(path, ExclusionRuleSet.From(UserSettings.Load()))
            && await TryServiceIndexAsync(path, recursive, filterPattern, onResult, limit, token).ConfigureAwait(false))
            return;

        // Network drive, WSL distro or folder index -- these are built and held in this process, so the
        // pipe never knew about them. Cheap to try even when the path belongs to none of them: each
        // index rejects a path outside its own root before touching anything.
        //
        // Retried under the location's other spelling (mapped letter vs UNC, \\wsl$ vs \\wsl.localhost)
        // when there is one: index lookup is a prefix match against the configured root, so the same
        // directory written the other way matches nothing and would walk the network for no reason.
        foreach (var spelling in IsInProcessIndexedSource(path) ? IndexedPathSpelling.IndexSpellings(path) : new[] { path })
        {
            if (UserNetworkDriveSearch.EnumerateDirectory(spelling, recursive, filterPattern, limit, onResult, token))
                return;
        }

        // Automatic WSL listings are index-only. A missing/building index must look empty instead of
        // waking the distro with DirectoryInfo; explicit open/locate/preview actions do not use here.
        if (WslPath.IsPath(path))
            return;

        // Nothing has it: an unconfigured share, a drive indexing is off for, an index still building,
        // the service down, or a path that simply doesn't exist. Every one of those emitted nothing
        // above, so walking the disk here cannot duplicate what was already delivered.
        await Task.Run(() => ScanLive(path, recursive, filterPattern, onResult, limit, token), token).ConfigureAwait(false);
    }

    // False = the service answered "no loaded index of mine holds this path" (still building, mid-swap,
    // no engine yet) or could not be reached at all -- both mean the caller should keep looking.
    private static async Task<bool> TryServiceIndexAsync(string path, bool recursive, string filterPattern,
        Action<SearchResult> onResult, int limit, CancellationToken token)
    {
        var indexed = true;
        try
        {
            await SearchPipeClient.SendSearchPipeCommandAsync(new SearchRequestMessage
            {
                Id = SearchRequestId.EnumerateDir,
                DirectoryFilter = path,
                Query = filterPattern,
                Recursive = recursive,
                Limit = limit
            }, onResult, token, onNotIndexed: () => indexed = false).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Cold-start connect timeouts (the App racing the service's own initialization) hit
            // every enumerated directory at once and are pure noise at Warn: the live walk below
            // answers meanwhile and the next request goes through the pipe again.
            var level = ServicePipeReadinessGate.Instance.IsColdStart(Environment.TickCount64)
                ? LogLevel.Debug
                : LogLevel.Warn;
            Logger.Log($"[IndexedDirectoryEnumerator] Index enumeration of '{path}' failed, falling back: {ex.Message}", level);
            return false;
        }
        return indexed;
    }

    // Where the path's index would live if it has one: network drives, WSL distros and folder indexes
    // are all held in this process. Only used to skip a pipe round trip that could not succeed -- the
    // in-process lookup below is authoritative and runs either way.
    private static bool IsInProcessIndexedSource(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
            return true;
        try
        {
            var root = Path.GetPathRoot(path);
            return !string.IsNullOrEmpty(root) && new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch
        {
            return false;
        }
    }

    internal static string NormalizeDirectoryPath(string path) => WslPath.IsPath(path)
        ? path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
        : Path.GetFullPath(path);

    // Matches the index-side walk's semantics on purpose (DirectoryEnumerator): directories are listed
    // alongside files but never matched against the file pattern, and recursion is never gated by it.
    private static void ScanLive(string path, bool recursive, string filterPattern, Action<SearchResult> onResult, int limit, CancellationToken token)
    {
        if (!Directory.Exists(path))
            return;
        var patterns = FilterPatternHelper.SplitOrNullIfMatchAll(filterPattern);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            AttributesToSkip = 0
        };

        var emitted = 0;
        foreach (var info in new DirectoryInfo(path).EnumerateFileSystemInfos("*", options))
        {
            token.ThrowIfCancellationRequested();
            // AttributesToSkip stays 0 on purpose: the walk must still go THROUGH hidden directories
            // (AppData is one), it just must not return them or any other hidden/system entry -- same
            // entry-level-only rule the index-side walk applies.
            if (FileSystemItemFilter.IsHiddenOrSystem(info.Attributes))
                continue;
            if ((info.Attributes & FileAttributes.Directory) == 0 && patterns != null && !FilterPatternHelper.Matches(info.Name, patterns))
                continue;
            onResult(ToResult(info));
            if (limit > 0 && ++emitted >= limit)
                return;
        }
    }

    private static SearchResult ToResult(FileSystemInfo info)
    {
        var isDir = (info.Attributes & FileAttributes.Directory) != 0;
        var root = Path.GetPathRoot(info.FullName) ?? string.Empty;
        return new SearchResult
        {
            Name = info.Name,
            Path = info.FullName,
            IsDir = isDir,
            Drive = root.Length >= 2 && root[1] == Path.VolumeSeparatorChar ? root.Substring(0, 1) : string.Empty,
            Attributes = info.Attributes,
            Metadata = new PluginSdk.Abstractions.FileMetadata(
                info is FileInfo file ? file.Length : 0,
                info.CreationTime,
                info.LastWriteTime,
                info.LastAccessTime)
        };
    }
}
