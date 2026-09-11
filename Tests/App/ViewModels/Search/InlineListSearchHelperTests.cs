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

    // The tiers. This is the whole of "current folder first": the list is built from the same global
    // results, re-ordered so the window's folder leads -- its own files, then its subfolders, then the
    // rest of the drive.
    //
    // Assert.IsLessThan(upperBound, value) asserts value < upperBound, so the SECOND argument is the one
    // expected to come first in the list.

    [TestMethod]
    public void MergeLocalMatches_GlobalDescendantsOfTheFolder_PrecedeRowsOutsideIt()
    {
        // A global row inside the folder's subtree must come before one elsewhere, even though the global
        // search ranked them the other way round.
        var uiResults = new List<AppSearchResult>
        {
            Header("[Search_SectionHeader]"),
            Item(@"D:\elsewhere\far.txt"),
            Item(@"C:\work\sub\deep.txt"),
        };

        var result = InlineListSearchHelper.MergeLocalMatches(uiResults, new List<AppSearchResult>(), "q", @"C:\work");

        var deepIndex = result.FindIndex(r => r.FullPath == @"C:\work\sub\deep.txt");
        var farIndex = result.FindIndex(r => r.FullPath == @"D:\elsewhere\far.txt");
        Assert.IsGreaterThanOrEqualTo(0, deepIndex);
        Assert.IsLessThan(farIndex, deepIndex, "the folder's descendant must lead the outside row");
    }

    [TestMethod]
    public void MergeLocalMatches_ShallowerDescendants_PrecedeDeeperOnes()
    {
        var uiResults = new List<AppSearchResult>
        {
            Header("[Search_SectionHeader]"),
            Item(@"C:\work\a\b\c\deep.txt"),
            Item(@"C:\work\a\shallow.txt"),
        };

        var result = InlineListSearchHelper.MergeLocalMatches(uiResults, new List<AppSearchResult>(), "q", @"C:\work");

        var shallow = result.FindIndex(r => r.FullPath == @"C:\work\a\shallow.txt");
        var deep = result.FindIndex(r => r.FullPath == @"C:\work\a\b\c\deep.txt");
        Assert.IsLessThan(deep, shallow, "the nearer descendant must lead the deeper one");
    }

    [TestMethod]
    public void MergeLocalMatches_DirectListingRows_LeadTheFoldersOwnDescendants()
    {
        var uiResults = new List<AppSearchResult>
        {
            Header("[Search_SectionHeader]"),
            Item(@"C:\work\sub\deep.txt"),
        };
        var local = new List<AppSearchResult> { Item(@"C:\work\here.txt") };

        var result = InlineListSearchHelper.MergeLocalMatches(uiResults, local, "q", @"C:\work");

        var here = result.FindIndex(r => r.FullPath == @"C:\work\here.txt");
        var deep = result.FindIndex(r => r.FullPath == @"C:\work\sub\deep.txt");
        Assert.IsLessThan(deep, here, "the folder's own file must lead its subfolder's");
    }

    [TestMethod]
    public void MergeLocalMatches_FolderHasOnlyDescendants_TheyStillLeadTheOutsideRows()
    {
        // The window's folder can contribute no DIRECT hit while its subfolders do -- and those subfolder
        // rows are still "this folder" to the user, so they lead the rest of the drive.
        var uiResults = new List<AppSearchResult>
        {
            Header("[Search_SectionHeader]"),
            Item(@"D:\unrelated\r1.txt"),
            Item(@"C:\work\sub\r2.txt"),
        };

        var result = InlineListSearchHelper.MergeLocalMatches(uiResults, new List<AppSearchResult>(), "q", @"C:\work");

        var subIndex = result.FindIndex(r => r.FullPath == @"C:\work\sub\r2.txt");
        var outsideIndex = result.FindIndex(r => r.FullPath == @"D:\unrelated\r1.txt");
        Assert.IsLessThan(outsideIndex, subIndex, "the folder's descendant must lead the outside row");
    }

    [TestMethod]
    public void MergeLocalMatches_NothingFromTheFolder_OmitsTheCurrentFolderHeader()
    {
        // With no rows at all under the folder there is no section to head, so the header must not appear.
        var uiResults = new List<AppSearchResult>
        {
            Header("[Search_SectionHeader]"),
            Item(@"D:\unrelated\r1.txt"),
        };

        var result = InlineListSearchHelper.MergeLocalMatches(uiResults, new List<AppSearchResult>(), "q", @"C:\work");

        Assert.IsFalse(result.Any(r => r.ResultKind == "SectionHeader" && r.Name == "[Search_LocalFolderHeader]"),
            "an empty Current Folder section would be misleading");
        // The outside row is still there, under its own Global Search header.
        var globalHeader = result.FindIndex(r => r.ResultKind == "SectionHeader" && r.Name == "[Search_GlobalSearchHeader]");
        var rowIndex = result.FindIndex(r => r.FullPath == @"D:\unrelated\r1.txt");
        Assert.IsGreaterThanOrEqualTo(0, rowIndex);
        Assert.IsLessThan(rowIndex, globalHeader, "the outside row sits under the global header");
    }

    [TestMethod]
    public void MergeLocalMatches_FolderRowsAndOutsideRows_GetTheirOwnHeaders()
    {
        var uiResults = new List<AppSearchResult>
        {
            Header("[Search_SectionHeader]"),
            Item(@"C:\work\sub\in.txt"),
            Item(@"D:\out.txt"),
        };

        var result = InlineListSearchHelper.MergeLocalMatches(uiResults, new List<AppSearchResult>(), "q", @"C:\work");

        var localHeader = result.FindIndex(r => r.ResultKind == "SectionHeader" && r.Name == "[Search_LocalFolderHeader]");
        var globalHeader = result.FindIndex(r => r.ResultKind == "SectionHeader" && r.Name == "[Search_GlobalSearchHeader]");
        var inIndex = result.FindIndex(r => r.FullPath == @"C:\work\sub\in.txt");
        var outIndex = result.FindIndex(r => r.FullPath == @"D:\out.txt");
        Assert.IsGreaterThanOrEqualTo(0, localHeader);
        Assert.IsGreaterThanOrEqualTo(0, globalHeader);
        Assert.IsLessThan(globalHeader, localHeader, "the folder section comes first");
        Assert.IsLessThan(inIndex, localHeader, "the folder row sits under the folder header");
        Assert.IsLessThan(outIndex, globalHeader, "the outside row sits under the global header");
    }

    [TestMethod]
    public void MergeLocalMatches_EqualTierRows_KeepTheGlobalRankOrder()
    {
        // Stability matters: within one tier the engine's ranking is still what decides, so the re-order
        // must not shuffle rows that are equally near.
        var uiResults = new List<AppSearchResult>
        {
            Header("[Search_SectionHeader]"),
            Item(@"C:\work\better.txt"),
            Item(@"C:\work\worse.txt"),
        };

        var result = InlineListSearchHelper.MergeLocalMatches(uiResults, new List<AppSearchResult>(), "q", @"C:\work");

        Assert.IsLessThan(
            result.FindIndex(r => r.FullPath == @"C:\work\worse.txt"),
            result.FindIndex(r => r.FullPath == @"C:\work\better.txt"));
    }

    [TestMethod]
    public void MergeLocalMatches_SiblingFolderWithTheSamePrefix_IsNotTreatedAsInside()
    {
        // "C:\workshop" is not under "C:\work"; the folder boundary is a separator, not a prefix.
        var uiResults = new List<AppSearchResult>
        {
            Header("[Search_SectionHeader]"),
            Item(@"C:\workshop\x.txt"),
        };

        var result = InlineListSearchHelper.MergeLocalMatches(uiResults, new List<AppSearchResult>(), "q", @"C:\work");

        Assert.IsFalse(result.Any(r => r.ResultKind == "SectionHeader" && r.Name == "[Search_LocalFolderHeader]"));
    }
}
