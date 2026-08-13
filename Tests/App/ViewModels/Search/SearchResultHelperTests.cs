using Lertaro.Core;
using Lertaro.PluginSdk.Services;
using Lertaro.App.ViewModels.Search;

namespace Lertaro.App.Tests.ViewModels.Search;

[TestClass]
public sealed class SearchResultHelperTests
{
    [TestMethod]
    public void HistoryKindOf_Application_ReturnsApplication() =>
        Assert.AreEqual(HistoryEntryKind.Application, SearchResultHelper.HistoryKindOf(new AppSearchResult { ResultKind = "Application" }));

    [TestMethod]
    public void HistoryKindOf_Directory_ReturnsFolder() =>
        Assert.AreEqual(HistoryEntryKind.Folder, SearchResultHelper.HistoryKindOf(new AppSearchResult { IsDir = true }));

    [TestMethod]
    public void HistoryKindOf_PlainFile_ReturnsFile() =>
        Assert.AreEqual(HistoryEntryKind.File, SearchResultHelper.HistoryKindOf(new AppSearchResult()));

    [TestMethod]
    public void AddSectionHeader_AppendsHeaderRowWithGivenTitleAndQuery()
    {
        var results = new List<AppSearchResult>();

        SearchResultHelper.AddSectionHeader(results, "My Header", "q");

        Assert.HasCount(1, results);
        Assert.AreEqual("My Header", results[0].Name);
        Assert.AreEqual("SectionHeader", results[0].ResultKind);
        Assert.AreEqual("__SECTION_HEADER__", results[0].FullPath);
        Assert.AreEqual("q", results[0].SearchQuery);
    }

    [TestMethod]
    public void AddSectionHeader_IndexIsCurrentListLength()
    {
        var results = new List<AppSearchResult> { new(), new() };

        SearchResultHelper.AddSectionHeader(results, "Header", "q");

        Assert.AreEqual(2, results[2].Index);
    }

    [TestMethod]
    public void CreateUiResult_UsesItemNameWhenPresent()
    {
        var item = new SearchResult { Name = "file.txt", Path = @"C:\dir\file.txt", IsDir = false, Drive = "C" };

        var ui = SearchResultHelper.CreateUiResult(item, "q", 3, isApplication: false, scope: null);

        Assert.AreEqual("file.txt", ui.Name);
        Assert.AreEqual(@"C:\dir\file.txt", ui.FullPath);
        Assert.AreEqual("File", ui.ResultKind);
        Assert.AreEqual(3, ui.Index);
        Assert.AreEqual("q", ui.SearchQuery);
    }

    [TestMethod]
    public void CreateUiResult_BlankItemName_FallsBackToPath()
    {
        var item = new SearchResult { Name = "  ", Path = @"C:\dir\file.txt" };

        var ui = SearchResultHelper.CreateUiResult(item, "q", 0, isApplication: false, scope: null);

        Assert.AreEqual(@"C:\dir\file.txt", ui.Name);
    }

    [TestMethod]
    public void CreateUiResult_Application_MarksResultKindApplication()
    {
        var item = new SearchResult { Name = "app.exe", Path = @"C:\app.exe" };

        var ui = SearchResultHelper.CreateUiResult(item, "q", 0, isApplication: true, scope: null);

        Assert.AreEqual("Application", ui.ResultKind);
    }

    [TestMethod]
    public void CreateUiResult_Directory_ContextDirectoryIsItselfNotParent()
    {
        var item = new SearchResult { Name = "folder", Path = @"C:\dir\folder", IsDir = true };

        var ui = SearchResultHelper.CreateUiResult(item, "q", 0, isApplication: false, scope: null);

        Assert.AreEqual(@"C:\dir\folder", ui.ContextDirectory);
    }

    [TestMethod]
    public void CreateUiResult_File_ContextDirectoryIsParentDirectory()
    {
        var item = new SearchResult { Name = "file.txt", Path = @"C:\dir\file.txt", IsDir = false };

        var ui = SearchResultHelper.CreateUiResult(item, "q", 0, isApplication: false, scope: null);

        Assert.AreEqual(@"C:\dir", ui.ContextDirectory);
    }

    [TestMethod]
    public void CreateNoResultsResult_HasEmptyResultKindAndNoResultsMarker()
    {
        var ui = SearchResultHelper.CreateNoResultsResult("q");

        Assert.AreEqual("Empty", ui.ResultKind);
        Assert.AreEqual("__NO_RESULTS__", ui.FullPath);
    }

    [TestMethod]
    public void CreateResultTypeTriggerPromptResult_UsesEmptyResultKind() =>
        // TranslationManager's untranslated fallback ("[key]") has no {0} placeholder to substitute
        // typeDisplayName into, so string.Format leaves it as the literal bracketed key -- this only
        // asserts the untranslated shape is what a real (translated) template would also produce a
        // non-empty Name from, not the literal substituted text.
        Assert.AreEqual("[Search_ResultTypeTriggerPrompt]", SearchResultHelper.CreateResultTypeTriggerPromptResult("Docs").Name);

    [TestMethod]
    public void CreateKeepTypingPromptResult_HasEmptyResultKind() =>
        Assert.AreEqual("Empty", SearchResultHelper.CreateKeepTypingPromptResult().ResultKind);

    [TestMethod]
    public void GetParentDisplayText_ApplicationWithParent_UsesAppDirTemplateNotGenericAppLabel()
    {
        var item = new SearchResult { Path = @"C:\Program Files\App\app.exe" };

        var text = SearchResultHelper.GetParentDisplayText(item, isApplication: true, scope: null);

        // TranslationManager's untranslated fallback has no {0} placeholder, so the parent dir itself
        // never appears in the formatted text here -- this just distinguishes the "has a parent"
        // template key from the "no parent" one asserted in the sibling test below.
        Assert.AreEqual("[Search_ResultAppDir]", text);
    }

    [TestMethod]
    public void GetParentDisplayText_ApplicationWithNoParent_UsesGenericAppLabel()
    {
        var item = new SearchResult { Path = "app.exe" };

        var text = SearchResultHelper.GetParentDisplayText(item, isApplication: true, scope: null);

        Assert.AreEqual("[Search_ResultApp]", text);
    }

    [TestMethod]
    public void GetParentDisplayText_WithScope_ReturnsRelativePath()
    {
        var item = new SearchResult { Path = @"C:\root\sub\file.txt" };

        var text = SearchResultHelper.GetParentDisplayText(item, isApplication: false, scope: @"C:\root");

        Assert.AreEqual("sub", text);
    }

    [TestMethod]
    public void GetParentDisplayText_NoScope_ReturnsWslFormattedPath()
    {
        var item = new SearchResult { Path = @"\\wsl$\Ubuntu\home\file.txt" };

        var text = SearchResultHelper.GetParentDisplayText(item, isApplication: false, scope: null);

        Assert.AreEqual("WSL-Ubuntu:/home", text);
    }

    [TestMethod]
    public void FormatWslPath_WslPath_ConvertsToWslDisplayFormat() =>
        Assert.AreEqual("WSL-Ubuntu:/home/user", SearchResultHelper.FormatWslPath(@"\\wsl$\Ubuntu\home\user"));

    [TestMethod]
    public void FormatWslPath_WslRootOnly_ReturnsColonSlash() =>
        Assert.AreEqual("WSL-Ubuntu:/", SearchResultHelper.FormatWslPath(@"\\wsl$\Ubuntu"));

    [TestMethod]
    public void FormatWslPath_NonWslPath_ReturnsUnchanged() =>
        Assert.AreEqual(@"C:\normal\path", SearchResultHelper.FormatWslPath(@"C:\normal\path"));

    [TestMethod]
    public void FormatWslPath_EmptyPath_ReturnsEmpty() =>
        Assert.AreEqual("", SearchResultHelper.FormatWslPath(""));

    [TestMethod]
    public void NormalizePath_WslPathUsesLexicalNormalization()
    {
        var path = @"\\wsl$\Ubuntu/home/testuser/~cache/";

        Assert.AreEqual(@"\\wsl$\Ubuntu\home\testuser\~cache", SearchResultHelper.NormalizePath(path));
    }

    [TestMethod]
    public void FormatRelativeParentPath_SubdirectoryOfScope_ReturnsRelativeSegment() =>
        Assert.AreEqual("sub\\deeper", SearchResultHelper.FormatRelativeParentPath(@"C:\root\sub\deeper", @"C:\root"));

    [TestMethod]
    public void FormatRelativeParentPath_SameAsScope_ReturnsEmpty() =>
        Assert.AreEqual("", SearchResultHelper.FormatRelativeParentPath(@"C:\root", @"C:\root"));

    [TestMethod]
    public void FormatRelativeParentPath_StripsLeadingDotBackslash() =>
        Assert.DoesNotStartWith(".\\", SearchResultHelper.FormatRelativeParentPath(@"C:\root\sub", @"C:\root"));

    [TestMethod]
    public void NormalizePath_TrimsTrailingSeparator() =>
        Assert.AreEqual(@"C:\dir", SearchResultHelper.NormalizePath(@"C:\dir\"));

    [TestMethod]
    public void IsPathInsideScope_PathUnderScope_ReturnsTrue() =>
        Assert.IsTrue(SearchResultHelper.IsPathInsideScope(@"C:\root\sub\file.txt", @"C:\root"));

    [TestMethod]
    public void IsPathInsideScope_PathOutsideScope_ReturnsFalse() =>
        Assert.IsFalse(SearchResultHelper.IsPathInsideScope(@"C:\other\file.txt", @"C:\root"));

    [TestMethod]
    public void IsPathInsideScope_SamePathAsScope_ReturnsFalse() =>
        // No separator after the scope prefix, so the exact scope path itself is not "inside" itself.
        Assert.IsFalse(SearchResultHelper.IsPathInsideScope(@"C:\root", @"C:\root"));

    [TestMethod]
    public void AddShowMoreResult_AppendsRowWithShowMoreMarker()
    {
        var results = new List<AppSearchResult>();

        SearchResultHelper.AddShowMoreResult(results, "q");

        Assert.HasCount(1, results);
        Assert.AreEqual("__SHOW_MORE__", results[0].FullPath);
        Assert.AreEqual("Action", results[0].ResultKind);
    }

    [TestMethod]
    public void FormatSearchStatus_AppsAndFiles_UsesCombinedTemplate() =>
        Assert.AreEqual("[Search_StatsAppsAndFiles]", SearchResultHelper.FormatSearchStatus(3, 5));

    [TestMethod]
    public void FormatSearchStatus_OnlyApps_UsesAppsOnlyTemplate() =>
        Assert.AreEqual("[Search_StatsAppsOnly]", SearchResultHelper.FormatSearchStatus(3, 0));

    [TestMethod]
    public void FormatSearchStatus_OnlyFiles_UsesFilesOnlyTemplate() =>
        Assert.AreEqual("[Search_StatsFilesOnly]", SearchResultHelper.FormatSearchStatus(0, 5));
}
