using System.Collections.Concurrent;

using Lertaro.Core.Services.Network;

namespace Lertaro.Core.Services.Search;

// Answers "would a scoped search of this folder be answered from an index?" -- the same routing
// IndexedDirectoryEnumerator.EnumerateAsync uses to pick a source, but as a plain predicate the App's
// FileFilter scope resolution consults BEFORE dispatching. A folder that fails it would come back
// empty from every scoped search (neither the service index nor the in-process indexes know it, and
// the enumerate path has no live-walk fallback), so the scope layer drops it from the fan-out instead
// of letting it silently swallow the user's searches -- with a logged warning naming the folder, so
// "my scope finds nothing" has a visible one-line explanation in the log.
public static class SearchScopeCoverage
{
    // Scoped searches re-resolve on every keystroke, but the underlying facts (drive enablement,
    // configured roots) are stable between status/settings changes -- a one-minute cache keeps the
    // repeated checks free, while the explicit invalidation hooks pick up changes immediately.
    private const int TtlMs = 60_000;

    private sealed record Verdict(bool Covered, long CheckedAtMs);
    private static readonly ConcurrentDictionary<string, Verdict> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> _warned = new(StringComparer.OrdinalIgnoreCase);

    static SearchScopeCoverage() => UserNetworkDriveSearch.StatusesChanged += _ => Invalidate();

    public static void Invalidate()
    {
        _cache.Clear();
        _warned.Clear();
    }

    public static bool IsIndexed(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            return false;

        var path = IndexedDirectoryEnumerator.NormalizeDirectoryPath(directoryPath);
        var now = Environment.TickCount64;
        if (_cache.TryGetValue(path, out var verdict) && now - verdict.CheckedAtMs < TtlMs)
            return verdict.Covered;

        var covered = ComputeCovered(path);
        _cache[path] = new Verdict(covered, now);
        if (!covered && _warned.TryAdd(path, 0))
            Logger.Log($"[SearchScopeCoverage] '{directoryPath}' is not covered by any index (no folder index, its local drive is not enabled, or the network/WSL root is not configured); that scope folder is skipped until it is indexed.", LogLevel.Warn);
        return covered;
    }

    // The routing decision over precomputed facts, kept pure for tests: WSL counts as covered on its
    // own; an in-process index root covers any path below it, including a fixed-drive folder index;
    // anything else is a local drive that must be enabled for indexing.
    internal static bool DecideCovered(bool isWsl, bool isNetworkSource, bool hasInProcessRoot, bool isLocalDriveEnabled)
        => isWsl || hasInProcessRoot || !isNetworkSource && isLocalDriveEnabled;

    private static bool ComputeCovered(string path)
    {
        try
        {
            // WSL's in-memory index is the sole source for distro paths (CheckNeedsLiveSearch refuses
            // to live-scan them either), so a distro path counts as covered on its own.
            if (WslPath.IsPath(path))
                return true;

            var isNetworkSource = path.StartsWith(@"\\", StringComparison.Ordinal);
            if (!isNetworkSource)
            {
                try
                {
                    var root = Path.GetPathRoot(path);
                    isNetworkSource = !string.IsNullOrEmpty(root) && new DriveInfo(root).DriveType == DriveType.Network;
                }
                catch
                {
                    // An unresolvable root (disconnected letter, malformed path) falls through to the
                    // local-drive check below, whose volume lookup fails the same way it does for the
                    // real search routing.
                }
            }

            // Network, WSL and explicitly configured folder indexes are all represented by opaque
            // in-process roots. This check must also run for fixed-drive paths: a folder index such as
            // D:\Projects is valid even when the whole D: local drive is disabled.
            var hasInProcessRoot = UserNetworkDriveSearch.GetStatuses()
                .Any(item => DirectoryIndexReadiness.IsInProcessReady(item)
                    && IndexedDirectoryEnumerator.IsUnderRoot(path, IndexedDirectoryEnumerator.NormalizeIndexRoot(item.Drive)));
            var driveLetter = path.Length > 0 ? path.Substring(0, 1) : string.Empty;
            return DecideCovered(isWsl: false, isNetworkSource, hasInProcessRoot, MachineSettings.Load().IsLocalDriveEnabled(VolumeHelper.GetVolumeId(driveLetter)));
        }
        catch
        {
            // Same stance as the routing it mirrors: an inconclusive probe must not send a scoped
            // search at a folder whose coverage is unknown -- the fan-out can do without it.
            return false;
        }
    }
}
