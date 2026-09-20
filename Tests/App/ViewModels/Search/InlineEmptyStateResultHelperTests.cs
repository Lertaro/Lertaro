using Lertaro.App.ViewModels.Search;

namespace Lertaro.App.Tests.ViewModels.Search;

[TestClass]
public sealed class InlineEmptyStateResultHelperTests
{
    [TestMethod]
    public void Build_PreservesRecentSuggestionBeforeOpenedFolderGroup()
    {
        var recent = new AppSearchResult { Name = "Explorer", FullPath = @"C:\recent", ResultKind = "JumpToExplorerPath" };

        var result = InlineEmptyStateResultHelper.Build(
            recent,
            new[] { @"C:\opened" },
            "Last directory",
            "Opened folders");

        Assert.AreEqual("SectionHeader", result[0].ResultKind);
        Assert.AreEqual("Last directory", result[0].Name);
        Assert.AreSame(recent, result[1]);
        Assert.AreEqual("SectionHeader", result[2].ResultKind);
        Assert.AreEqual("Opened folders", result[2].Name);
        Assert.AreEqual(@"C:\opened", result[3].FullPath);
        Assert.AreEqual("OpenedFolder", result[3].ResultKind);
    }

    // Every reported folder is listed, in the order it was reported, duplicates of the same folder across
    // two spellings collapsed. In particular the folder the user is in right now is NOT filtered out, even
    // when the recent-directory row above names it too: the file manager reports the tab the user has focus
    // on (Directory Opus's active_tab) first, and that is the entry this list is looked at for.
    [TestMethod]
    public void Build_KeepsEveryReportedFolderInTheReportedOrder()
    {
        var recent = new AppSearchResult { FullPath = @"C:\recent\", ResultKind = "JumpToExplorerPath" };

        var result = InlineEmptyStateResultHelper.Build(
            recent,
            new[] { @"C:\focused", @"C:\recent", @"C:\CURRENT\", @"C:\other", @"C:\other\", "" },
            "Last directory",
            "Opened folders");

        CollectionAssert.AreEqual(
            new[] { @"C:\focused", @"C:\recent", @"C:\CURRENT\", @"C:\other" },
            result.Where(r => r.ResultKind == "OpenedFolder").Select(r => r.FullPath).ToArray());
    }

    [TestMethod]
    public void Build_IndexesHeadersAndRowsSequentially()
    {
        var result = InlineEmptyStateResultHelper.Build(
            null,
            new[] { @"C:\one", @"C:\two" },
            "Last directory",
            "Opened folders");

        for (var index = 0; index < result.Count; index++)
            Assert.AreEqual(index, result[index].Index);
    }
}
