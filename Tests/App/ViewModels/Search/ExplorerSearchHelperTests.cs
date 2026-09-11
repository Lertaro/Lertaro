using Lertaro.App.ViewModels.Search;
using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core.SearchIndex;

namespace Lertaro.App.Tests.ViewModels.Search;

[TestClass]
public sealed class ExplorerSearchHelperTests
{
    // ResultKind stays "File" even for a folder, which is what production does (a folder is a "File" row
    // with IsDir set) -- IsDir is the only field that distinguishes them.
    private static AppSearchResult Item(string path, bool isDir = false) =>
        new() { FullPath = path, Name = System.IO.Path.GetFileName(path), ResultKind = "File", IsDir = isDir };

    private static ExplorerSearchHelper.RankedRow Row(string path, int tier = MatchRank.TierName, int start = 0, double weight = 1.0, bool isDir = false) =>
        new(Item(path, isDir), new MatchRank(tier, start, weight));

    [TestMethod]
    public void OrderByDirectoryTier_DirectChildrenPrecedeDescendantMatches()
    {
        var result = ExplorerSearchHelper.OrderByDirectoryTier(
            [Row(@"C:\Root\Child\nested.txt"), Row(@"C:\Root\direct.txt")],
            @"C:\Root");

        Assert.AreEqual(@"C:\Root\direct.txt", result[0].FullPath);
        Assert.AreEqual(@"C:\Root\Child\nested.txt", result[1].FullPath);
    }

    // Direct children read in Explorer's own file-name order. This is the reported case: Explorer lists
    // these three as Lertaro, LRC maker, lx-music, and match-quality ordering used to invert it because
    // its coverage term favours the shortest name.
    [TestMethod]
    public void OrderByDirectoryTier_DirectChildren_UseExplorerNameOrder()
    {
        var result = ExplorerSearchHelper.OrderByDirectoryTier(
            [Row(@"C:\Root\lx-music"), Row(@"C:\Root\LRC maker"), Row(@"C:\Root\Lertaro")],
            @"C:\Root");

        CollectionAssert.AreEqual(
            new[] { @"C:\Root\Lertaro", @"C:\Root\LRC maker", @"C:\Root\lx-music" },
            result.Select(item => item.FullPath).ToArray());
    }

    [TestMethod]
    public void OrderByDirectoryTier_DirectChildren_NumericNamesSortByValue()
    {
        var result = ExplorerSearchHelper.OrderByDirectoryTier(
            [Row(@"C:\Root\file10.txt"), Row(@"C:\Root\file9.txt"), Row(@"C:\Root\file2.txt")],
            @"C:\Root");

        CollectionAssert.AreEqual(
            new[] { @"C:\Root\file2.txt", @"C:\Root\file9.txt", @"C:\Root\file10.txt" },
            result.Select(item => item.FullPath).ToArray());
    }

    // Folders group ahead of files rather than interleaving with them, matching Explorer -- so a folder
    // whose name sorts after a file's still leads, and one sorting before a file's still trails it.
    [TestMethod]
    public void OrderByDirectoryTier_DirectChildren_FoldersGroupBeforeFiles()
    {
        var result = ExplorerSearchHelper.OrderByDirectoryTier(
            [
                Row(@"C:\Root\apple.txt"),
                Row(@"C:\Root\Berry", isDir: true),
                Row(@"C:\Root\banana.txt"),
                Row(@"C:\Root\Zebra", isDir: true),
            ],
            @"C:\Root");

        CollectionAssert.AreEqual(
            new[] { @"C:\Root\Berry", @"C:\Root\Zebra", @"C:\Root\apple.txt", @"C:\Root\banana.txt" },
            result.Select(item => item.FullPath).ToArray());
    }

    // Each group keeps the name ordering inside it.
    [TestMethod]
    public void OrderByDirectoryTier_DirectChildren_NameOrderAppliesWithinEachGroup()
    {
        var result = ExplorerSearchHelper.OrderByDirectoryTier(
            [
                Row(@"C:\Root\zzz-folder", isDir: true),
                Row(@"C:\Root\aaa-folder", isDir: true),
                Row(@"C:\Root\zzz-file.txt"),
                Row(@"C:\Root\aaa-file.txt"),
            ],
            @"C:\Root");

        CollectionAssert.AreEqual(
            new[] { @"C:\Root\aaa-folder", @"C:\Root\zzz-folder", @"C:\Root\aaa-file.txt", @"C:\Root\zzz-file.txt" },
            result.Select(item => item.FullPath).ToArray());
    }

    // Folder-first is the OUTER key, so it holds even when a file matches better -- but tier and start
    // still order items WITHIN a group.
    [TestMethod]
    public void OrderByDirectoryTier_DirectChildren_FolderGroupLeadsRegardlessOfMatchTier()
    {
        var result = ExplorerSearchHelper.OrderByDirectoryTier(
            [
                Row(@"C:\Root\perfect.txt", tier: MatchRank.TierName),
                Row(@"C:\Root\weak-folder", isDir: true, tier: MatchRank.TierFull),
                Row(@"C:\Root\better-folder", isDir: true, tier: MatchRank.TierName),
            ],
            @"C:\Root");

        CollectionAssert.AreEqual(
            new[] { @"C:\Root\better-folder", @"C:\Root\weak-folder", @"C:\Root\perfect.txt" },
            result.Select(item => item.FullPath).ToArray());
    }

    // A folder in a SUBfolder is a descendant, not a direct child, so the grouping does not apply to it.
    [TestMethod]
    public void OrderByDirectoryTier_DescendantFolderIsNotGroupedWithDirectChildren()
    {
        var result = ExplorerSearchHelper.OrderByDirectoryTier(
            [Row(@"C:\Root\Child\deep-folder", isDir: true), Row(@"C:\Root\direct.txt")],
            @"C:\Root");

        Assert.AreEqual(@"C:\Root\direct.txt", result[0].FullPath);
        Assert.AreEqual(@"C:\Root\Child\deep-folder", result[1].FullPath);
    }

    // Among direct children the order is start, then tier, then Explorer's name order -- tier is the
    // WEAKEST key, so an earlier start wins over a better tier.
    [TestMethod]
    public void OrderByDirectoryTier_DirectChildren_StartComesBeforeTierAndNameOrder()
    {
        var result = ExplorerSearchHelper.OrderByDirectoryTier(
            [
                Row(@"C:\Root\zzz-literal.txt", tier: MatchRank.TierName),
                Row(@"C:\Root\aaa-full.txt", tier: MatchRank.TierFull, start: 4),
                Row(@"C:\Root\mmm-initials.txt", tier: MatchRank.TierInitials),
            ],
            @"C:\Root");

        // All three start at 0 except aaa-full (start 4), so it goes last; the two at start 0 are then
        // ordered by tier (literal before initials), regardless of their names.
        CollectionAssert.AreEqual(
            new[] { @"C:\Root\zzz-literal.txt", @"C:\Root\mmm-initials.txt", @"C:\Root\aaa-full.txt" },
            result.Select(item => item.FullPath).ToArray());
    }

    [TestMethod]
    public void OrderByDirectoryTier_PreservesExistingOrderWithinEachTier()
    {
        var result = ExplorerSearchHelper.OrderByDirectoryTier(
            [Row(@"C:\Root\Child\first-nested.txt"), Row(@"C:\Root\Child\second-nested.txt"), Row(@"C:\Root\first-direct.txt"), Row(@"C:\Root\second-direct.txt")],
            @"C:\Root");

        CollectionAssert.AreEqual(
            new[] { @"C:\Root\first-direct.txt", @"C:\Root\second-direct.txt", @"C:\Root\Child\first-nested.txt", @"C:\Root\Child\second-nested.txt" },
            result.Select(item => item.FullPath).ToArray());
    }

    // Deeper subfolders rank below shallower ones, so a page of results reads outward from the window's
    // folder rather than interleaving every depth.
    [TestMethod]
    public void OrderByDirectoryTier_ShallowerDescendantsPrecedeDeeperOnes()
    {
        var result = ExplorerSearchHelper.OrderByDirectoryTier(
            [Row(@"C:\Root\A\B\deep.txt"), Row(@"C:\Root\A\shallow.txt"), Row(@"C:\Root\direct.txt")],
            @"C:\Root");

        CollectionAssert.AreEqual(
            new[] { @"C:\Root\direct.txt", @"C:\Root\A\shallow.txt", @"C:\Root\A\B\deep.txt" },
            result.Select(item => item.FullPath).ToArray());
    }

    [TestMethod]
    public void OrderByDirectoryTier_PathOutsideTheFolder_SortsLast()
    {
        var result = ExplorerSearchHelper.OrderByDirectoryTier(
            [Row(@"D:\elsewhere.txt"), Row(@"C:\Root\Child\inside.txt"), Row(@"C:\Root\direct.txt")],
            @"C:\Root");

        Assert.AreEqual(@"D:\elsewhere.txt", result[2].FullPath);
    }

    // The cap must be applied AFTER the proximity sort: a deep history hit or an outside match must not
    // displace a direct child from the Current Folder section.
    [TestMethod]
    public void CreateLocalSnapshot_OrdersByProximityThenCap()
    {
        var result = ExplorerSearchHelper.CreateLocalSnapshot(
            [Item(@"C:\Root\Child\deep.txt"), Item(@"D:\outside.txt"), Item(@"C:\Root\direct.txt")],
            new List<SearchResultMapper.RankedCandidate>(),
            "f",
            @"C:\Root");

        CollectionAssert.AreEqual(
            new[] { @"C:\Root\direct.txt", @"C:\Root\Child\deep.txt", @"D:\outside.txt" },
            result.Select(item => item.FullPath).ToArray());
    }

    // End-to-end through CreateLocalSnapshot, which is what the inline window actually calls.
    [TestMethod]
    public void CreateLocalSnapshot_DirectChildren_UseExplorerNameOrder()
    {
        var result = ExplorerSearchHelper.CreateLocalSnapshot(
            [Item(@"C:\Root\lx-music"), Item(@"C:\Root\LRC maker"), Item(@"C:\Root\Lertaro")],
            new List<SearchResultMapper.RankedCandidate>(),
            "l",
            @"C:\Root");

        CollectionAssert.AreEqual(
            new[] { @"C:\Root\Lertaro", @"C:\Root\LRC maker", @"C:\Root\lx-music" },
            result.Select(item => item.FullPath).ToArray());
    }

    [TestMethod]
    public void CreateLocalSnapshot_ExcludesTheQueriedFolderItself()
    {
        var result = ExplorerSearchHelper.CreateLocalSnapshot(
            [Item(@"C:\Root"), Item(@"C:\Root\direct.txt")],
            new List<SearchResultMapper.RankedCandidate>(),
            "f",
            @"C:\Root");

        CollectionAssert.AreEqual(new[] { @"C:\Root\direct.txt" }, result.Select(item => item.FullPath).ToArray());
    }

    [TestMethod]
    public void CreateLocalSnapshot_ReindexesSequentially()
    {
        var result = ExplorerSearchHelper.CreateLocalSnapshot(
            [Item(@"C:\Root\Child\deep.txt"), Item(@"C:\Root\direct.txt")],
            new List<SearchResultMapper.RankedCandidate>(),
            "f",
            @"C:\Root");

        for (var i = 0; i < result.Count; i++)
            Assert.AreEqual(i, result[i].Index);
    }
}
