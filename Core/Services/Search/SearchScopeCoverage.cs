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
    // configured roots) only change when the user applies settings -- a one-minute cache keeps the
    // repeated checks free while still picking up an Apply without needing any invalidation hook.
    private const int TtlMs = 60_000;

    private sealed record Verdict(bool Covered, long CheckedAtMs);
    private static readonly ConcurrentDictionary<string, Verdict> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _warned = new(StringComparer.OrdinalIgnoreCase);

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
        if (!covered && _warned.Add(path))
            Logger.Log($"[SearchScopeCoverage] '{directoryPath}' is not covered by any index (no folder index, its local drive is not enabled, or the network/WSL root is not configured); that scope folder is skipped until it is indexed.", LogLevel.Warn);
        return covered;
    }

    // The routing decision over precomputed facts, kept pure for tests: WSL counts as covered on its
    // own; a network/UNC source needs a containing in-process root; anything else is a local drive
    // that must be enabled for indexing.
    internal static bool DecideCovered(bool isWsl, bool isNetworkSource, bool hasInProcessRoot, bool isLocalDriveEnabled)
        => isWsl || (isNetworkSource ? hasInProcessRoot : isLocalDriveEnabled);

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

            if (isNetworkSource)
            {
                // UNC and mapped network paths are answered from the in-process indexes only; some
                // configured root (network drive, folder index, WSL distro) must contain the folder.
                var hasInProcessRoot = UserNetworkDriveSearch.GetStatuses()
                    .Any(item => IndexedDirectoryEnumerator.IsUnderRoot(path, IndexedDirectoryEnumerator.NormalizeIndexRoot(item.Drive)));
                return DecideCovered(isWsl: false, isNetworkSource: true, hasInProcessRoot, isLocalDriveEnabled: false);
            }

            var driveLetter = path.Length > 0 ? path.Substring(0, 1) : string.Empty;
            return DecideCovered(isWsl: false, isNetworkSource: false, hasInProcessRoot: false, MachineSettings.Load().IsLocalDriveEnabled(VolumeHelper.GetVolumeId(driveLetter)));
        }
        catch
        {
            // Same stance as the routing it mirrors: an inconclusive probe must not send a scoped
            // search at a folder whose coverage is unknown -- the fan-out can do without it.
            return false;
        }
    }
}
