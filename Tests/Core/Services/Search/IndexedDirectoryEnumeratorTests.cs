using Lertaro.Core.Services.Search;

namespace Lertaro.Core.Tests.Services.Search;

[TestClass]
public sealed class IndexedDirectoryEnumeratorTests
{
    [TestMethod]
    public void NormalizeDirectoryPath_WslPathUsesLexicalNormalization()
    {
        var path = @"\\wsl$\Ubuntu/home/testuser/~cache/";

        Assert.AreEqual(@"\\wsl$\Ubuntu\home\testuser\~cache\", IndexedDirectoryEnumerator.NormalizeDirectoryPath(path));
    }

    [TestMethod]
    public void NormalizeDirectoryPath_LocalRelativePathStillBecomesFullyQualified()
    {
        var normalized = IndexedDirectoryEnumerator.NormalizeDirectoryPath("relative");

        Assert.IsTrue(Path.IsPathFullyQualified(normalized));
    }

    [TestMethod]
    public void NormalizeIndexRoot_BareDriveLetter_GainsColonAndSeparator()
    {
        Assert.AreEqual(@"C:\", IndexedDirectoryEnumerator.NormalizeIndexRoot("C"));
        // Case is preserved here; the case-insensitive matching happens in IsUnderRoot downstream.
        Assert.AreEqual(@"c:\", IndexedDirectoryEnumerator.NormalizeIndexRoot("c"));
    }

    [TestMethod]
    public void NormalizeIndexRoot_TrailingSeparator_IsGuaranteed()
    {
        Assert.AreEqual(@"\\server\share\", IndexedDirectoryEnumerator.NormalizeIndexRoot(@"\\server\share"));
        Assert.AreEqual(@"D:\folder\", IndexedDirectoryEnumerator.NormalizeIndexRoot(@"D:/folder"));
    }

    [TestMethod]
    public void IsUnderRoot_RootItself_AndNestedPaths_Match_CaseInsensitive()
    {
        Assert.IsTrue(IndexedDirectoryEnumerator.IsUnderRoot(@"\\Server\Share", @"\\server\share\"));
        Assert.IsTrue(IndexedDirectoryEnumerator.IsUnderRoot(@"\\server\share\sub\deeper", @"\\server\share\"));
    }

    [TestMethod]
    public void IsUnderRoot_SiblingPrefix_DoesNotMatch()
    {
        Assert.IsFalse(IndexedDirectoryEnumerator.IsUnderRoot(@"\\server\share2\file", @"\\server\share\"));
        Assert.IsFalse(IndexedDirectoryEnumerator.IsUnderRoot(@"C:\Users\other", @"C:\Users\testuser\"));
    }

    // This API's routing predicate must keep answering "no live search" for a local directory its index
    // holds, even when an exclusion rule matches it. Its "yes" branch below routes to the in-process
    // sources, which cannot answer for a local drive, so switching this to the search path's
    // exclusion-aware answer would have dropped a directory the index genuinely has. Upstream requires
    // the enumeration API's behavior to be unchanged, which is what this pins.
    [TestMethod]
    public void EnumerateRouting_ExcludedDirectoryOnAnEnabledLocalDrive_StaysWithTheIndex()
    {
        var settings = new UserSettings
        {
            ExcludedPaths = [@"c:\windows"],
            IgnoredPathGlobs = new List<string>(),
            IgnoredPathRegexes = new List<string>()
        };
        var rules = ExclusionRuleSet.From(settings, @"c:\");
        var machineSettings = new MachineSettings
        {
            LocalDriveSelectionConfigured = true,
            LocalDrives = [VolumeHelper.GetVolumeId("C") ?? throw new AssertInconclusiveException("Drive C has no volume ID.")]
        };

        var enumerationSays = SearchServiceHelper.CheckNeedsLiveSearch(@"c:\windows", rules, machineSettings,
            SearchServiceHelper.LiveSearchIntent.DirectoryEnumeration);
        var searchSays = SearchServiceHelper.CheckNeedsLiveSearch(@"c:\windows", rules, machineSettings,
            SearchServiceHelper.LiveSearchIntent.Search);

        // The enumeration API takes the index path; the search path is the one that gained the live scan.
        // On C: -- journal-capable, so fully indexed -- both agree "no live search", which is the
        // pre-existing answer this API must keep. The divergence itself is pinned in
        // SearchServiceHelperCheckNeedsLiveSearchTests, where it can be reached without a real drive.
        Assert.IsFalse(enumerationSays);
        Assert.IsFalse(searchSays);
    }
}
