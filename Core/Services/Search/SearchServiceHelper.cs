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

    /// <summary>
    /// What the caller is asking this decision for. The two callers differ on exactly one point -- the
    /// partially-indexed local drive whose path is excluded -- so the whole decision stays here, in one
    /// place, with this as its only input rather than a second copy of the rules at the call site.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The difference is real and comes from what each caller does with a "yes". The search path uses it
    /// to live-scan the excluded subtree, which is how content that was deliberately kept out of the index
    /// stays findable -- including through the `*` prefix that bypasses result filtering, where the whole
    /// point is to see what the rules hid. The enumeration path has no such fallback: its "no" sends the
    /// request to the service index, and its "yes" drops it into the in-process source branch
    /// (<c>IndexedDirectoryEnumerator.EnumerateAsync</c>), which knows nothing about a local drive. So for
    /// the enumeration API the new behavior would have been a regression, not a feature: an excluded local
    /// directory would stop being listed from the index it is genuinely in, in exchange for nothing.
    /// </para>
    /// <para>
    /// Upstream's requirement is that the enumeration API keep its previous behavior, so
    /// <see cref="DirectoryEnumeration"/> preserves it exactly while <see cref="Search"/> gains the
    /// exclusion-aware scan. Everything other than the excluded-partially-indexed-drive case is shared
    /// between them.
    /// </para>
    /// </remarks>
    internal enum LiveSearchIntent
    {
        /// <summary>The search path: excluded content on a partially-indexed drive is live-scanned.</summary>
        Search,

        /// <summary>
        /// The directory enumeration API: unchanged from before exclusion-aware live search existed -- a
        /// local drive that is enabled for indexing is answered from its index, excluded or not.
        /// </summary>
        DirectoryEnumeration
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
    //
    // The one tier the two intents read differently is tier 2 for a local drive: see LiveSearchIntent.
    public static bool CheckNeedsLiveSearch(
        string dir,
        ExclusionRuleSet exclusionRules,
        MachineSettings? machineSettings = null,
        LiveSearchIntent intent = LiveSearchIntent.Search)
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

                return IsExcludedAsDirectory(dir, exclusionRules);
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

            // The one point where the two callers part company -- see NeedsLiveSearchForExcludedPath.
            return NeedsLiveSearchForExcludedPath(intent, isExcluded: IsExcludedAsDirectory(dir, exclusionRules));
        }
        catch { return true; }
    }

    /// <summary>
    /// The tier-2 boundary, as a pure decision: an enabled but only partially-indexed local drive whose
    /// path the rules match. Testable without a real non-journal volume, which is the only place the two
    /// callers' answers differ and therefore the only part worth pinning separately.
    /// </summary>
    /// <remarks>
    /// Only the search path live-scans here. Its "yes" is what keeps deliberately-excluded content
    /// findable, notably through the `*` prefix whose entire purpose is to show what the rules hid. The
    /// enumeration API has no live-scan fallback to reach -- its "yes" branch routes to the in-process
    /// sources, which cannot answer for a local drive -- so for it the same "yes" would mean dropping a
    /// directory its own index genuinely holds, with nothing in exchange. Upstream requires that API's
    /// previous behavior, hence the split here rather than a second copy of the rule at the call site.
    /// </remarks>
    internal static bool NeedsLiveSearchForExcludedPath(LiveSearchIntent intent, bool isExcluded) =>
        intent == LiveSearchIntent.Search && isExcluded;

    // A rule that names a DIRECTORY (an ExcludedPaths entry, or a glob like `node_modules`) only matches
    // the directory spelling, so a path that is itself a directory has to be asked about as one -- the
    // same dummy-file probe the network branch above uses for the same reason.
    private static bool IsExcludedAsDirectory(string dir, ExclusionRuleSet exclusionRules) =>
        exclusionRules.IsExcludedPath(dir, true)
        || exclusionRules.IsExcludedPath(Path.Combine(dir, "_live_search_dummy.txt"), false);

    // Whether an enabled local drive's index can be missing content its own build path skipped. Only a
    // non-journal volume is walked by LocalDriveWalkBuilder (see UsnIndexerBuildExtensions -> IndexBuilder),
    // which is the only local build that applies the user's exclusion rules; a journal-capable volume is
    // read whole. Split out as a pure decision so the tier boundary is testable without a real volume.
    internal static bool IsPartiallyIndexedLocalDrive(bool isJournalCapable) => !isJournalCapable;
}
