using Lertaro.App.ViewModels.Search;

namespace Lertaro.App.Tests.ViewModels.Search;

[TestClass]
public sealed class InlineListSearchHelperTests
{
    private static AppSearchResult Item(string path, string kind = "File") => new() { FullPath = path, Name = path, ResultKind = kind };
    private static AppSearchResult Header(string title) => new() { Name = title, FullPath = "__SECTION_HEADER__", ResultKind = "SectionHeader" };
    private static AppSearchResult Instant(string name) => new() { Name = name, FullPath = name, ResultKind = "InstantResult" };

    [TestMethod]
    public void MergeLocalMatches_NoLocalMatches_OmitsLocalFolderHeader()
    {
        var uiResults = new List<AppSearchResult> { Header("[Search_SectionHeader]"), Item(@"C:\global") };

        var result = InlineListSearchHelper.MergeLocalMatches(uiResults, new List<AppSearchResult>(), "q");

        Assert.IsFalse(result.Any(r => r.ResultKind == "SectionHeader" && r.Name == "[Search_LocalFolderHeader]"));
    }

    [TestMethod]
    public void MergeLocalMatches_HasLocalMatches_InsertsLocalFolderHeaderBeforeThem()
    {
        var uiResults = new List<AppSearchResult> { Header("[Search_SectionHeader]"), Item(@"C:\global") };
        var local = new List<AppSearchResult> { Item(@"C:\local") };

        var result = InlineListSearchHelper.MergeLocalMatches(uiResults, local, "q");

        var headerIndex = result.FindIndex(r => r.ResultKind == "SectionHeader" && r.Name == "[Search_LocalFolderHeader]");
        Assert.IsGreaterThanOrEqualTo(0, headerIndex);
        Assert.AreEqual(@"C:\local", result[headerIndex + 1].FullPath);
    }

    [TestMethod]
    public void MergeLocalMatches_InstantItemsBeforeSearchHeader_ArePreservedFirst()
    {
        var uiResults = new List<AppSearchResult> { Instant("calc"), Header("[Search_SectionHeader]"), Item(@"C:\global") };

        var result = InlineListSearchHelper.MergeLocalMatches(uiResults, new List<AppSearchResult>(), "q");

        Assert.AreEqual("calc", result[0].Name);
    }

    [TestMethod]
    public void MergeLocalMatches_GlobalItemAlreadyInLocalMatches_IsDeduped()
    {
        var uiResults = new List<AppSearchResult> { Header("[Search_SectionHeader]"), Item(@"C:\dup"), Item(@"C:\unique") };
        var local = new List<AppSearchResult> { Item(@"C:\dup") };

        var result = InlineListSearchHelper.MergeLocalMatches(uiResults, local, "q");

        Assert.AreEqual(1, result.Count(r => r.FullPath == @"C:\dup"));
        Assert.IsTrue(result.Any(r => r.FullPath == @"C:\unique"));
    }

    [TestMethod]
    public void MergeLocalMatches_DedupeIsCaseInsensitiveOnNormalizedPath()
    {
        var uiResults = new List<AppSearchResult> { Header("[Search_SectionHeader]"), Item(@"C:\DUP") };
        var local = new List<AppSearchResult> { Item(@"C:\dup") };

        var result = InlineListSearchHelper.MergeLocalMatches(uiResults, local, "q");

        Assert.AreEqual(0, result.Count(r => r.FullPath == @"C:\DUP"));
    }

    [TestMethod]
    public void MergeLocalMatches_NoGlobalItemsRemainAfterDedupe_OmitsGlobalSearchHeader()
    {
        var uiResults = new List<AppSearchResult> { Header("[Search_SectionHeader]"), Item(@"C:\dup") };
        var local = new List<AppSearchResult> { Item(@"C:\dup") };

        var result = InlineListSearchHelper.MergeLocalMatches(uiResults, local, "q");

        Assert.IsFalse(result.Any(r => r.ResultKind == "SectionHeader" && r.Name == "[Search_GlobalSearchHeader]"));
    }

    [TestMethod]
    public void MergeLocalMatches_ReindexesAllResultsSequentially()
    {
        var uiResults = new List<AppSearchResult> { Header("[Search_SectionHeader]"), Item(@"C:\a"), Item(@"C:\b") };

        var result = InlineListSearchHelper.MergeLocalMatches(uiResults, new List<AppSearchResult>(), "q");

        for (var i = 0; i < result.Count; i++)
            Assert.AreEqual(i, result[i].Index);
    }
}
