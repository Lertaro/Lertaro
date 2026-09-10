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
            null,
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

    [TestMethod]
    public void Build_ExcludesCurrentScopeRecentPathAndDuplicates()
    {
        var recent = new AppSearchResult { FullPath = @"C:\recent\", ResultKind = "JumpToExplorerPath" };

        var result = InlineEmptyStateResultHelper.Build(
            recent,
            @"C:\current\",
            new[] { @"C:\recent", @"C:\CURRENT\", @"C:\other", @"C:\other\", "" },
            "Last directory",
            "Opened folders");

        Assert.AreEqual(1, result.Count(r => r.ResultKind == "OpenedFolder"));
        Assert.AreEqual(@"C:\other", result.Single(r => r.ResultKind == "OpenedFolder").FullPath);
    }

    [TestMethod]
    public void Build_IndexesHeadersAndRowsSequentially()
    {
        var result = InlineEmptyStateResultHelper.Build(
            null,
            null,
            new[] { @"C:\one", @"C:\two" },
            "Last directory",
            "Opened folders");

        for (var index = 0; index < result.Count; index++)
            Assert.AreEqual(index, result[index].Index);
    }
}
