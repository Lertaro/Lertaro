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
            "Opened folders");

        Assert.AreSame(recent, result[0]);
        Assert.AreEqual("SectionHeader", result[1].ResultKind);
        Assert.AreEqual("Opened folders", result[1].Name);
        Assert.AreEqual(@"C:\opened", result[2].FullPath);
        Assert.AreEqual("OpenedFolder", result[2].ResultKind);
    }

    [TestMethod]
    public void Build_ExcludesCurrentScopeRecentPathAndDuplicates()
    {
        var recent = new AppSearchResult { FullPath = @"C:\recent\", ResultKind = "JumpToExplorerPath" };

        var result = InlineEmptyStateResultHelper.Build(
            recent,
            @"C:\current\",
            new[] { @"C:\recent", @"C:\CURRENT\", @"C:\other", @"C:\other\", "" },
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
            "Opened folders");

        for (var index = 0; index < result.Count; index++)
            Assert.AreEqual(index, result[index].Index);
    }
}
