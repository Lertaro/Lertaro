using Lertaro.Core.Services.Network;

using Lertaro.Core.SearchIndex.Query;
namespace Lertaro.Core.Services.Search;

internal static class SearchServiceHelper
{
    public static bool SearchNetworkDrives(
        string query,
        int maxResults,
        string? directoryFilter,
        ExclusionRuleSet exclusionRules,
        bool bypassExclusions,
        Action<SearchResult> onResult,
        CancellationToken token)
    {
        try
        {
            var parsed = SearchQueryParser.Parse(query);
            var queryExemptRoot = parsed.IsPathMode ? parsed.ExactPathLower : null;
            var found = 0;
            UserNetworkDriveSearch.SearchStreaming(query, maxResults, result =>
            {
                token.ThrowIfCancellationRequested();
                if (bypassExclusions || !exclusionRules.IsExcluded(result, directoryFilter) || !exclusionRules.IsExcluded(result, queryExemptRoot))
                {
                    Interlocked.Increment(ref found);
                    onResult(result);
                }
            }, token, directoryFilter);

            return found > 0;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Logger.Log($"[SearchServiceHelper] Network drive search failed: {ex.Message}", LogLevel.Error);
            return false;
        }
    }

    // Three-tier rule, based purely on whether `dir`'s content actually made it into an index -- never on
    // caller intent (see SearchService.SearchStreamingAsync for the separate, orthogonal question of
    // whether MATCHED results get filtered by ExcludedPaths/globs/regexes once found):
    //   1. Fully indexed (a local drive enabled for indexing -- MftIndexScanner/ReFsScanner/
    //      LocalDriveWalkBuilder all walk the WHOLE volume unconditionally, ExcludedPaths never enters
    //      into it) -- the index has everything, so exclusion settings are never a reason to live-scan.
    //   2. Partially indexed (a configured network drive -- WalkFilter skips excluded roots/globs/regexes
    //      at build time) -- only live-scan the part the index doesn't have: content that's excluded.
    //   3. Not indexed at all (network drive not configured, or a local drive not enabled for indexing)
    //      -- always live-scan, there's no index data to fall back on.
    public static bool CheckNeedsLiveSearch(string dir, ExclusionRuleSet exclusionRules)
    {
        try
        {
            var driveInfo = new DriveInfo(dir);
            if (driveInfo.DriveType == DriveType.Network)
            {
                var letter = dir.Substring(0, 1);
                var id = NetworkDriveResolver.GetNetworkId(letter);
                var isConfigured = !string.IsNullOrWhiteSpace(id) && UserSettings.Load().NetworkDrives.Any(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
                if (!isConfigured)
                    return true;

                return exclusionRules.IsExcludedPath(dir, true)
                    || exclusionRules.IsExcludedPath(Path.Combine(dir, "_live_search_dummy.txt"), false);
            }

            // Any local drive currently enabled for indexing gets a full, exhaustive walk regardless of
            // its own filesystem -- NTFS/ReFS via the USN journal/MFT, everything else (FAT32, exFAT, ...)
            // via the same walk pipeline network drives use -- so filesystem type alone is no longer a
            // reliable "is this indexed" signal. An empty LocalDrives list means no drive has been
            // explicitly restricted, matching every other LocalDrives consumer's own "empty = everything
            // enabled" convention (see SearchEngineDriveMaintenance.RebuildDriveIndex/DeleteDriveIndex).
            var driveLetter = dir.Substring(0, 1);
            var enabledLocalDriveIds = MachineSettings.Load().LocalDrives;
            var isIndexed = enabledLocalDriveIds.Count == 0
                || enabledLocalDriveIds.Contains(VolumeHelper.GetVolumeId(driveLetter) ?? string.Empty, StringComparer.OrdinalIgnoreCase);

            return !isIndexed;
        }
        catch { return true; }
    }
}
