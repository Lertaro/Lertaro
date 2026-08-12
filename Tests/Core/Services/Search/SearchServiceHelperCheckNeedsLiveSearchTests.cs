using Lertaro.Core.Services.Search;

namespace Lertaro.Core.Tests.Services.Search;

[TestClass]
public sealed class SearchServiceHelperCheckNeedsLiveSearchTests
{
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

    // A local drive enabled for indexing is walked unconditionally (MftIndexScanner/ReFsScanner/
    // LocalDriveWalkBuilder never consult ExcludedPaths/globs/regexes) -- so it's always fully indexed,
    // and exclusion settings can never be a reason to fall back to a live scan for it, no matter what kind
    // of rule matches. (There's no network drive mapped in a test environment, so the network branch's
    // "partially indexed -- only live-scan what's excluded" behavior isn't covered here.)
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
