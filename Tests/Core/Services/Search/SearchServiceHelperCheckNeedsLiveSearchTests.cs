using Lertaro.Core.Services.Search;

namespace Lertaro.Core.Tests.Services.Search;

[TestClass]
public sealed class SearchServiceHelperCheckNeedsLiveSearchTests
{
    [TestMethod]
    public void CheckNeedsLiveSearch_WslPath_IsAlwaysIndexOnly()
    {
        var result = SearchServiceHelper.CheckNeedsLiveSearch(
            @"\\wsl$\Ubuntu\home\testuser",
            ExclusionRuleSet.From(new UserSettings()));

        Assert.IsFalse(result);
    }

    // A local drive enabled for indexing is walked by one of two build paths, and only one of them can
    // leave a hole in the index:
    //   - a journal-capable volume ($MFT parse / ReFS scan) is read whole and never consults
    //     ExcludedPaths, so it is fully indexed -- an exclusion rule is never a reason to live-scan it,
    //     no matter what kind of rule matches (the four CheckNeedsLiveSearch_* tests below, which run
    //     against the real C: and therefore only hold while C: is journal-capable; the predicate tests
    //     here pin the rule itself regardless of what this machine has mounted).
    //   - a non-journal volume (FAT32/exFAT) is walked by LocalDriveWalkBuilder, which DOES apply the
    //     rules -- so an excluded subtree is genuinely absent from its index and searching inside it
    //     (notably via the documented `*` bypass prefix) has nothing to fall back on but a live scan.
    // The boundary is that predicate, pure and testable without a real volume; the production caller
    // feeds it VolumeHelper.SupportsUsnJournal(driveLetter).
    [TestMethod]
    public void IsPartiallyIndexedLocalDrive_EnabledNonJournalDrive_NeedsExclusionAwareLiveSearch() =>
        Assert.IsTrue(SearchServiceHelper.IsPartiallyIndexedLocalDrive(isJournalCapable: false));

    [TestMethod]
    public void IsPartiallyIndexedLocalDrive_EnabledJournalCapableDrive_KeepsTodaysBehaviour() =>
        Assert.IsFalse(SearchServiceHelper.IsPartiallyIndexedLocalDrive(isJournalCapable: true));

    // The two callers' answers differ in exactly one case, and this pins it: a path on an enabled,
    // non-journal local drive that an exclusion rule matches. The search path live-scans it (that is how
    // content kept out of the index -- including via the `*` bypass prefix -- stays findable); the
    // directory enumeration API does not, because its "yes" branch routes to the in-process sources and
    // would mean dropping a directory its own index genuinely holds. Upstream requires that API's
    // previous behavior, so this is the assertion that would fail if the two were ever remerged.
    [TestMethod]
    public void NeedsLiveSearchForExcludedPath_SearchPath_LiveScansAnExcludedPath()
    {
        Assert.IsTrue(SearchServiceHelper.NeedsLiveSearchForExcludedPath(
            SearchServiceHelper.LiveSearchIntent.Search, isExcluded: true));
    }

    [TestMethod]
    public void NeedsLiveSearchForExcludedPath_DirectoryEnumeration_KeepsTheIndexAnswer()
    {
        Assert.IsFalse(SearchServiceHelper.NeedsLiveSearchForExcludedPath(
            SearchServiceHelper.LiveSearchIntent.DirectoryEnumeration, isExcluded: true));
    }

    // Neither caller live-scans a path no rule matches, so the divergence above is confined to excluded
    // paths -- the enumeration API must not have lost its index answer for ordinary directories either.
    [TestMethod]
    public void NeedsLiveSearchForExcludedPath_NotExcluded_NeitherCallerLiveScans()
    {
        Assert.IsFalse(SearchServiceHelper.NeedsLiveSearchForExcludedPath(
            SearchServiceHelper.LiveSearchIntent.Search, isExcluded: false));
        Assert.IsFalse(SearchServiceHelper.NeedsLiveSearchForExcludedPath(
            SearchServiceHelper.LiveSearchIntent.DirectoryEnumeration, isExcluded: false));
    }

    // The default is the search path's behavior, so every pre-existing call site -- which passes no intent
    // at all -- keeps exactly what it did before the parameter existed.
    [TestMethod]
    public void CheckNeedsLiveSearch_OmittingTheIntent_BehavesAsTheSearchPath()
    {
        var rules = ExclusionRuleSet.From(EmptySettings(), @"c:\");
        var settings = CurrentDriveEnabled("C");

        Assert.AreEqual(
            SearchServiceHelper.CheckNeedsLiveSearch(@"c:\projects", rules, settings, SearchServiceHelper.LiveSearchIntent.Search),
            SearchServiceHelper.CheckNeedsLiveSearch(@"c:\projects", rules, settings));
    }

    // The enumeration API's routing decision on a journal-capable, enabled drive: it does not live-scan,
    // so the request is answered from the service index. This is the path IndexedDirectoryEnumerator takes,
    // and it must stay identical to what it returned before exclusion-aware live search existed.
    [TestMethod]
    public void CheckNeedsLiveSearch_DirectoryEnumeration_OnAnEnabledJournalDrive_DoesNotLiveScan()
    {
        var settings = EmptySettings();
        settings.ExcludedPaths.Add(@"c:\windows");
        var rules = ExclusionRuleSet.From(settings, @"c:\");

        Assert.IsFalse(SearchServiceHelper.CheckNeedsLiveSearch(@"c:\windows", rules, CurrentDriveEnabled("C"),
            SearchServiceHelper.LiveSearchIntent.DirectoryEnumeration));
    }

    private static UserSettings EmptySettings() => new()
    {
        ExcludedPaths = new List<string>(),
        IgnoredPathGlobs = new List<string>(),
        IgnoredPathRegexes = new List<string>()
    };

    private static MachineSettings CurrentDriveEnabled(string drive) => new()
    {
        LocalDriveSelectionConfigured = true,
        LocalDrives = [VolumeHelper.GetVolumeId(drive) ?? throw new AssertInconclusiveException($"Drive {drive} has no volume ID.")]
    };

    // A journal-capable local drive enabled for indexing is read whole (MftIndexScanner/ReFsScanner never
    // consult ExcludedPaths/globs/regexes) -- so it's fully indexed, and exclusion settings can never be a
    // reason to fall back to a live scan for it, no matter what kind of rule matches. (There's no network
    // drive mapped in a test environment, so the network branch's "partially indexed -- only live-scan
    // what's excluded" behavior isn't covered here either.)
    [TestMethod]
    public void CheckNeedsLiveSearch_LocalDriveExcludedRoot_DoesNotNeedLiveSearch()
    {
        var settings = EmptySettings();
        settings.ExcludedPaths.Add(@"c:\windows");
        var rules = ExclusionRuleSet.From(settings, @"c:\");

        Assert.IsFalse(SearchServiceHelper.CheckNeedsLiveSearch(@"c:\windows", rules, CurrentDriveEnabled("C")));
    }

    [TestMethod]
    public void CheckNeedsLiveSearch_LocalDriveInsideExcludedRoot_DoesNotNeedLiveSearch()
    {
        var settings = EmptySettings();
        settings.ExcludedPaths.Add(@"c:\windows");
        var rules = ExclusionRuleSet.From(settings, @"c:\");

        Assert.IsFalse(SearchServiceHelper.CheckNeedsLiveSearch(@"c:\windows\system32", rules, CurrentDriveEnabled("C")));
    }

    [TestMethod]
    public void CheckNeedsLiveSearch_LocalDriveNoExclusionsAtAll_DoesNotNeedLiveSearch()
    {
        var rules = ExclusionRuleSet.From(EmptySettings(), @"c:\");

        Assert.IsFalse(SearchServiceHelper.CheckNeedsLiveSearch(@"c:\projects", rules, CurrentDriveEnabled("C")));
    }

    [TestMethod]
    public void CheckNeedsLiveSearch_LocalDriveDirectoryMatchesIgnoredGlob_StillDoesNotNeedLiveSearch()
    {
        var settings = EmptySettings();
        settings.IgnoredPathGlobs.Add("node_modules");
        var rules = ExclusionRuleSet.From(settings, @"c:\");

        Assert.IsFalse(SearchServiceHelper.CheckNeedsLiveSearch(@"c:\projects\app\node_modules", rules, CurrentDriveEnabled("C")));
    }

    [TestMethod]
    public void CheckNeedsLiveSearch_LocalDriveExplicitlyDisabled_NeedsLiveSearch()
    {
        var rules = ExclusionRuleSet.From(EmptySettings(), @"c:\");
        var settings = new MachineSettings { LocalDriveSelectionConfigured = true };

        Assert.IsTrue(SearchServiceHelper.CheckNeedsLiveSearch(@"c:\projects", rules, settings));
    }
}
