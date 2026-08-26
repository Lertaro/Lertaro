using Lertaro.Core;
using Lertaro.App.ViewModels.Search;

namespace Lertaro.App.Tests.ViewModels.Search;

[TestClass]
[DoNotParallelize]
public sealed class FavoriteSearchHelperTests
{
    [TestMethod]
    public void ComputeMatch_QueryMatchesExplicitName_ReturnsMatch()
    {
        var fav = new FavoriteItemSetting { Name = "My Docs", Path = @"C:\Documents" };

        var (isMatch, _) = FavoriteSearchHelper.ComputeMatch(fav, "docs");

        Assert.IsTrue(isMatch);
    }

    [TestMethod]
    public void ComputeMatch_NoExplicitName_MatchesAgainstFileNameDerivedFromPath()
    {
        var fav = new FavoriteItemSetting { Name = "", Path = @"C:\Projects\Lertaro" };

        var (isMatch, _) = FavoriteSearchHelper.ComputeMatch(fav, "lertaro");

        Assert.IsTrue(isMatch);
    }

    [TestMethod]
    public void ComputeMatch_QueryDoesNotMatchNameOrPath_ReturnsNoMatch()
    {
        var fav = new FavoriteItemSetting { Name = "My Docs", Path = @"C:\Documents" };

        var (isMatch, _) = FavoriteSearchHelper.ComputeMatch(fav, "zzz_completely_unrelated_zzz");

        Assert.IsFalse(isMatch);
    }

    [TestMethod]
    public void ComputeMatch_QueryOnlyMatchesUnrelatedParentFolderInPath_ReturnsNoMatch()
    {
        // "Program Files" is entirely incidental to what the favorite actually is -- a query that only
        // happens to fuzzy-match letters scattered across the path, and not the favorite's own name,
        // must not surface it (this used to match via the raw path being searched as a fallback).
        var fav = new FavoriteItemSetting { Name = "", Path = @"C:\Program Files\SomeApp\readme.txt" };

        var (isMatch, _) = FavoriteSearchHelper.ComputeMatch(fav, "program");

        Assert.IsFalse(isMatch);
    }

    [TestMethod]
    public void ComputeMatch_ExplicitNameSet_QueryMatchingOnlyPath_ReturnsNoMatch()
    {
        var fav = new FavoriteItemSetting { Name = "My Docs", Path = @"C:\Program Files\Documents" };

        var (isMatch, _) = FavoriteSearchHelper.ComputeMatch(fav, "program");

        Assert.IsFalse(isMatch);
    }

    [TestMethod]
    public void ComputeMatch_WebUrlFavoriteWithNoName_MatchesAgainstFullUrl()
    {
        var fav = new FavoriteItemSetting { Name = "", Path = "https://example.com/docs" };

        var (isMatch, _) = FavoriteSearchHelper.ComputeMatch(fav, "example.com");

        Assert.IsTrue(isMatch);
    }

    [TestMethod]
    public void CreateFavoriteUiResult_ExplicitName_UsedAsDisplayName()
    {
        var fav = new FavoriteItemSetting { Name = "My Docs", Path = @"C:\Documents" };

        var ui = FavoriteSearchHelper.CreateFavoriteUiResult(fav, "q", 0);

        Assert.AreEqual("My Docs", ui.Name);
    }

    [TestMethod]
    public void CreateFavoriteUiResult_EnvironmentVariableFolder_IsDirectoryAndExpandsFullPath()
    {
        var fav = new FavoriteItemSetting { Name = "", Path = "%TEMP%" };

        var ui = FavoriteSearchHelper.CreateFavoriteUiResult(fav, "q", 0);

        Assert.IsTrue(ui.IsDir);
        Assert.AreEqual(Environment.ExpandEnvironmentVariables("%TEMP%"), ui.FullPath);
    }

    [TestMethod]
    public void CreateFavoriteUiResult_PrefixesParentDirWithFavoriteStar()
    {
        var fav = new FavoriteItemSetting { Name = "My Docs", Path = @"C:\Documents" };

        var ui = FavoriteSearchHelper.CreateFavoriteUiResult(fav, "q", 0);

        Assert.StartsWith("★ ", ui.ParentDir);
    }

    [TestMethod]
    public void CreateFavoriteUiResult_NonExistentPlainPath_IsNotMarkedAsDirectory()
    {
        var fav = new FavoriteItemSetting { Name = "Gone", Path = @"Z:\definitely-not-real-lertaro-path" };

        var ui = FavoriteSearchHelper.CreateFavoriteUiResult(fav, "q", 0);

        Assert.IsFalse(ui.IsDir);
    }

    [TestMethod]
    public void CreateFavoriteUiResult_WebUrl_GetsGlobeIconOverride()
    {
        var fav = new FavoriteItemSetting { Name = "Example", Path = "https://example.com" };

        var ui = FavoriteSearchHelper.CreateFavoriteUiResult(fav, "q", 0);

        // A web URL matches none of the isDir conditions (no "::"/"shell:" prefix, and Directory.Exists
        // is naturally false for a URL), so IsDir stays false -- only the globe icon override applies.
        Assert.IsFalse(ui.IsDir);
        Assert.IsNotNull(ui.IconOverride);
    }

    [TestMethod]
    public void CreateFavoriteUiResult_PlainFilePath_HasNoIconOverride()
    {
        var fav = new FavoriteItemSetting { Name = "Doc", Path = @"C:\Documents\file.txt" };

        var ui = FavoriteSearchHelper.CreateFavoriteUiResult(fav, "q", 0);

        Assert.IsNull(ui.IconOverride);
    }

    [TestMethod]
    public void CreateFavoriteUiResult_SetsIndexAndSearchQuery()
    {
        var fav = new FavoriteItemSetting { Name = "Doc", Path = @"C:\Documents" };

        var ui = FavoriteSearchHelper.CreateFavoriteUiResult(fav, "myquery", 7);

        Assert.AreEqual(7, ui.Index);
        Assert.AreEqual("myquery", ui.SearchQuery);
    }

    [TestMethod]
    public void ComputeMatch_EnvironmentVariableInPathNoExplicitName_MatchesExpandedFolderName()
    {
        Environment.SetEnvironmentVariable("TEST_SEARCH_FAV", @"C:\TestDir\SpecialTool");
        try
        {
            var fav = new FavoriteItemSetting { Name = "", Path = @"%TEST_SEARCH_FAV%" };
            var (isMatch, _) = FavoriteSearchHelper.ComputeMatch(fav, "special");
            Assert.IsTrue(isMatch);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_SEARCH_FAV", null);
        }
    }

    [TestMethod]
    public void CreateFavoriteUiResult_PathWithEnvironmentVariables_ExpandsFullPath()
    {
        Environment.SetEnvironmentVariable("TEST_SEARCH_FAV", @"C:\TestDir\SpecialTool");
        try
        {
            var fav = new FavoriteItemSetting { Name = "", Path = @"%TEST_SEARCH_FAV%\doc.txt" };
            var ui = FavoriteSearchHelper.CreateFavoriteUiResult(fav, "q", 0);
            Assert.AreEqual(@"C:\TestDir\SpecialTool\doc.txt", ui.FullPath);
            Assert.AreEqual("doc.txt", ui.Name);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_SEARCH_FAV", null);
        }
    }

    [TestMethod]
    public void CreateFavoriteUiResult_ShellVirtualFolder_ResolvesNameAndSetsDirectory()
    {
        var fav = new FavoriteItemSetting { Name = "", Path = "shell:downloads" };
        var ui = FavoriteSearchHelper.CreateFavoriteUiResult(fav, "q", 0);
        var expectedName = PluginSdk.Helpers.ShellPathHelper.GetVirtualFolderDisplayName("shell:downloads", "shell:downloads");
        Assert.AreEqual(expectedName, ui.Name);
        Assert.IsTrue(ui.IsDir);
    }
}
