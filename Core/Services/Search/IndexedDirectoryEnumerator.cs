using Lertaro.Core.Services.Network;

using Lertaro.Core.Wire;
namespace Lertaro.Core.Services.Search;

/// <summary>
/// Lists directory contents from an index only. An index that is still loading is not treated as an
/// empty index: this waits for the relevant source to become ready before returning.
/// </summary>
public static class IndexedDirectoryEnumerator
{
    private const int ReadinessPollMs = 250;
    private static readonly TimeSpan ServiceConnectionTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromMinutes(2);

    public static async Task EnumerateAsync(string directoryPath, bool recursive, string filterPattern,
        Action<SearchResult> onResult, int limit = 0, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            return;
        var path = NormalizeDirectoryPath(directoryPath);
        var exclusions = ExclusionRuleSet.From(UserSettings.Load());

        // A local drive enabled in the service is the authoritative source for that drive. Waiting on
        // its status prevents a cold-start request from falling through to a raw filesystem walk.
        if (!IsInProcessIndexedSource(path) && !SearchServiceHelper.CheckNeedsLiveSearch(path, exclusions))
        {
            if (await WaitForLocalIndexAsync(path, token).ConfigureAwait(false)
                && await TryServiceIndexAsync(path, recursive, filterPattern, onResult, limit, token).ConfigureAwait(false))
                return;

            return;
        }

        // Network, WSL and folder indexes live in this process. Their status has the same pending vs
        // ready distinction, so a request made while a cache is loading waits instead of returning a
        // false empty result.
        if (!await WaitForInProcessIndexAsync(path, token).ConfigureAwait(false))
            return;

        foreach (var spelling in IsInProcessIndexedSource(path) ? IndexedPathSpelling.IndexSpellings(path) : new[] { path })
        {
            token.ThrowIfCancellationRequested();
            if (UserNetworkDriveSearch.EnumerateDirectory(spelling, recursive, filterPattern, limit, onResult, token))
                return;
        }
    }

    private static async Task<bool> WaitForLocalIndexAsync(string path, CancellationToken token)
    {
        var deadline = DateTime.UtcNow + ReadinessTimeout;
        var serviceConnectionDeadline = DateTime.UtcNow + ServiceConnectionTimeout;
        var drive = Path.GetPathRoot(path)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrEmpty(drive))
            return false;

        while (DateTime.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            var statusResult = await new SearchPipeClient().GetStatusForReadinessAsync(token).ConfigureAwait(false);
            if (!statusResult.Connected)
            {
                if (DateTime.UtcNow >= serviceConnectionDeadline)
                    return false;
                await Task.Delay(ReadinessPollMs, token).ConfigureAwait(false);
                continue;
            }

            var status = statusResult.Status;
            if (status == null)
                return false;
            var normalizedDrive = drive.TrimEnd(':');
            if (DirectoryIndexReadiness.IsLocalReady(status, normalizedDrive))
                return true;
            if (!DirectoryIndexReadiness.ShouldWaitForLocal(status, normalizedDrive))
                return false;

            await Task.Delay(ReadinessPollMs, token).ConfigureAwait(false);
        }
        return false;
    }

    private static async Task<bool> WaitForInProcessIndexAsync(string path, CancellationToken token)
    {
        var deadline = DateTime.UtcNow + ReadinessTimeout;
        while (DateTime.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var status = UserNetworkDriveSearch.GetStatuses()
                    .Where(item => IsUnderRoot(path, NormalizeIndexRoot(item.Drive)))
                    .OrderByDescending(item => item.Drive.Length)
                    .FirstOrDefault();
                if (status == null)
                    return false;
                if (DirectoryIndexReadiness.IsInProcessReady(status))
                    return true;
                if (status.State is not ("pending" or "indexing"))
                    return false;
            }
            catch
            {
                // Configuration can be racing service startup. Keep waiting inside the bounded window;
                // a genuine configuration failure eventually becomes an empty indexed result.
            }

            await Task.Delay(ReadinessPollMs, token).ConfigureAwait(false);
        }
        return false;
    }

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
            Logger.Log($"[IndexedDirectoryEnumerator] Index enumeration of '{path}' failed: {ex.Message}", LogLevel.Warn);
            return false;
        }
        return indexed;
    }

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

    private static string NormalizeIndexRoot(string drive)
    {
        var normalized = drive.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return normalized.Length == 1 && char.IsLetter(normalized[0])
            ? normalized + @":\"
            : normalized.EndsWith(Path.DirectorySeparatorChar) ? normalized : normalized + Path.DirectorySeparatorChar;
    }

    private static bool IsUnderRoot(string path, string root)
    {
        var normalizedPath = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var normalizedRoot = root.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return normalizedPath.Equals(normalizedRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    internal static string NormalizeDirectoryPath(string path) => WslPath.IsPath(path)
        ? path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
        : Path.GetFullPath(path);
}
