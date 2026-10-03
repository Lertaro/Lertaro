using Lertaro.Core.Services.Network;
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
        CancellationToken token,
        string? fileNameFilter = null)
    {
        try
        {
            var found = 0;
            UserNetworkDriveSearch.SearchStreaming(query, maxResults, result =>
            {
                token.ThrowIfCancellationRequested();
                if (bypassExclusions || !exclusionRules.IsExcluded(result, directoryFilter))
                {
                    Interlocked.Increment(ref found);
                    onResult(result);
                }
            }, token, directoryFilter, fileNameFilter);

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
    // whether MATCHED results get filtered by ExcludedPaths/globs/regexes once found). The distinction
    // that matters is not the filesystem or the source kind but whether THAT source's build path applies
    // the exclusion rules at all, because only a build that skips excluded content leaves a hole an
    // excluded path could fall into:
    //   1. Fully indexed (a local drive enabled for indexing whose volume IS journal-capable -- an $MFT
    //      parse or a ReFS scan reads the whole volume and never consults ExcludedPaths) -- the index has
    //      everything, so exclusion settings are never a reason to live-scan.
    //   2. Partially indexed (a configured network drive, and a local drive whose volume is NOT
    //      journal-capable: their build paths -- WalkFilter / LocalDriveWalkBuilder -- skip excluded
    //      roots/globs/regexes at build time) -- only live-scan the part the index doesn't have: content
    //      that's excluded. Live-scanning is what keeps an excluded subtree searchable despite being
    //      deliberately absent from the index (notably for the `*` prefix that bypasses result filtering).
    //   3. Not indexed at all (network drive not configured, or a local drive not enabled for indexing)
    //      -- always live-scan, there's no index data to fall back on.
    // Tiers and rules are deliberately separate questions: if the journal-capable builds ever start
    // honouring exclusions too, they move to tier 2 by the rule below -- not by a filesystem name list.
    public static bool CheckNeedsLiveSearch(
        string dir,
        ExclusionRuleSet exclusionRules,
        MachineSettings? machineSettings = null)
    {
        // WSL can take seconds to wake after an idle period. Its configured in-memory index is the
        // sole automatic-search source; only explicit user actions may touch the distro filesystem.
        if (WslPath.IsPath(dir))
            return false;

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

            // Any local drive currently enabled for indexing is built by one of the local build paths, and
            // which one decides whether its index can have a hole: a journal-capable volume is read whole
            // (tier 1 above), a non-journal one is walked with the exclusion rules applied (tier 2). The
            // explicit local-drive selection is authoritative; an empty selection means no local drive is
            // indexed at all. Filesystem type alone is not the "is this indexed" signal -- only the
            // "does its build honour exclusions" one.
            var driveLetter = dir.Substring(0, 1);
            var isIndexed = (machineSettings ?? MachineSettings.Load()).IsLocalDriveEnabled(VolumeHelper.GetVolumeId(driveLetter));
            if (!isIndexed)
                return true;

            // A wholly-indexed local drive has nothing an exclusion rule could hide from it.
            if (!IsPartiallyIndexedLocalDrive(VolumeHelper.SupportsUsnJournal(driveLetter)))
                return false;

            // Excluded exactly as the network branch does, including the dummy-file probe: a rule that
            // names a DIRECTORY (an ExcludedPaths entry, or a glob like `node_modules`) only matches the
            // directory spelling, so a path that is itself a directory has to be asked about as one.
            return exclusionRules.IsExcludedPath(dir, true)
                || exclusionRules.IsExcludedPath(Path.Combine(dir, "_live_search_dummy.txt"), false);
        }
        catch { return true; }
    }

    // Whether an enabled local drive's index can be missing content its own build path skipped. Only a
    // non-journal volume is walked by LocalDriveWalkBuilder (see UsnIndexerBuildExtensions -> IndexBuilder),
    // which is the only local build that applies the user's exclusion rules; a journal-capable volume is
    // read whole. Split out as a pure decision so the tier boundary is testable without a real volume.
    internal static bool IsPartiallyIndexedLocalDrive(bool isJournalCapable) => !isJournalCapable;
}
