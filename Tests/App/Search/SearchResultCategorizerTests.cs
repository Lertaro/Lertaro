using Lertaro.App.ViewModels.Search;
using Lertaro.PluginSdk.Helpers;
using Lertaro.Plugins.SystemSettings;

namespace Lertaro.App.Tests.Search;

// The quick window's tab filter. Only the row-level questions live here -- which provider contributed a row,
// whether it reports itself launchable, where a shortcut points -- because the extension answer is pinned
// once and for all in SearchCategoryClassifierTests.
[TestClass]
public sealed class SearchResultCategorizerTests
{
    private static AppSearchResult Row(string path, bool isDir = false, bool isApplication = false, object? provider = null) =>
        new()
        {
            FullPath = path,
            Name = System.IO.Path.GetFileName(path),
            IsDir = isDir,
            ResultKind = isApplication ? "Application" : "File",
            SourceProvider = provider,
        };

    [TestMethod]
    [DataRow(@"D:\libs\readme.md", SearchCategoryFilter.Document)]
    [DataRow(@"D:\pics\shot.png", SearchCategoryFilter.Image)]
    [DataRow(@"D:\media\track.flac", SearchCategoryFilter.Audio)]
    [DataRow(@"D:\setup\installer.msi", SearchCategoryFilter.Application)]
    public void Categorize_AFileRow_TakesTheCategoryItsExtensionSays(string path, SearchCategoryFilter expected) =>
        Assert.AreEqual(expected, SearchResultCategorizer.Categorize(Row(path)));

    [TestMethod]
    public void Categorize_ADirectoryRow_IsAFolder() =>
        Assert.AreEqual(SearchCategoryFilter.Folder, SearchResultCategorizer.Categorize(Row(@"D:\Downloads", isDir: true)));

    [TestMethod]
    public void Categorize_ALaunchableRowTheProviderReported_IsAnApplicationWithoutAnExtension() =>
        Assert.AreEqual(SearchCategoryFilter.Application,
            SearchResultCategorizer.Categorize(Row("shell:::{AppsFolder}\\something", isApplication: true)));

    [TestMethod]
    public void Categorize_AnUnresolvableShortcut_KeepsWhicheverKindItsRowReports()
    {
        // Nothing on this machine to point at, so the target question has no answer and the row's own claim
        // is all there is. An application row stays an application; a plain file row belongs to no tab.
        var shortcut = Row(@"Start Menu\Missing.lnk", isApplication: true);
        Assert.AreEqual(SearchCategoryFilter.Application, SearchResultCategorizer.Categorize(shortcut));

        Assert.IsNull(SearchResultCategorizer.Categorize(Row(@"Desktop\Broken.lnk")));
    }

    [TestMethod]
    public void Categorize_TheSystemSettingsProvider_WinsOverTheApplicationItReports()
    {
        // Every GodMode task row also says it is an application, and its path is a shell namespace rather
        // than a file, so only the provider that contributed it tells the two kinds apart.
        var row = Row("shell:::{ED7BA470-8E54-465E-825C-99712043E01C}\\Display", isApplication: true,
            provider: new SystemSettingsItemProvider());

        Assert.AreEqual(SearchCategoryFilter.Settings, SearchResultCategorizer.Categorize(row));
    }

    [TestMethod]
    [DataRow("ms-settings:network")]
    [DataRow(@"C:\Windows\ImmersiveControlPanel\SystemSettings.exe")]
    public void Categorize_ASettingsPathByName_IsSettingsEvenWithoutItsProvider(string path) =>
        Assert.AreEqual(SearchCategoryFilter.Settings, SearchResultCategorizer.Categorize(Row(path)));

    [TestMethod]
    public void Categorize_APluginInstantAnswer_BelongsToNoTab()
    {
        var row = Row("__INSTANT__");
        row.ResultKind = "InstantResult";

        Assert.IsNull(SearchResultCategorizer.Categorize(row));
    }

    [TestMethod]
    public void Matches_All_KeepsEveryRowIncludingTheUncategorized()
    {
        Assert.IsTrue(SearchResultCategorizer.Matches(SearchCategoryFilter.All, Row("notes.xyz")));
        Assert.IsTrue(SearchResultCategorizer.Matches(SearchCategoryFilter.All, Row("a.md")));
    }

    [TestMethod]
    public void Matches_AFilterDropsTheRowsThatBelongElsewhere()
    {
        Assert.IsTrue(SearchResultCategorizer.Matches(SearchCategoryFilter.Document, Row("a.md")));
        Assert.IsFalse(SearchResultCategorizer.Matches(SearchCategoryFilter.Document, Row("b.png")));
        Assert.IsFalse(SearchResultCategorizer.Matches(SearchCategoryFilter.Audio, Row("notes.xyz")));
    }

    [TestMethod]
    public void BuildQuickResults_AFilterIsAppliedBeforeTheDisplayCap()
    {
        // The cap is what a filtered list must escape: the engine's own order puts documents ahead of audio
        // for a short query, so capping first and filtering after would hand the 音频 tab an empty list while
        // the index held seventy matches.
        var candidates = new List<Core.SearchResult>();
        for (var i = 0; i < 120; i++)
            candidates.Add(Record($@"D:\mix\doc{i}.md"));
        for (var i = 0; i < 70; i++)
            candidates.Add(Record($@"D:\mix\song{i}.mp3"));

        var results = Lertaro.App.ViewModels.Search.Mapping.SearchResultMapper.BuildQuickResults(
            candidates, "song", scope: null, contextDirectory: null, isInlineWindow: false,
            category: SearchCategoryFilter.Audio);

        Assert.AreEqual(70, results.Count, "every audio match should survive, not just the ones inside the cap");
        Assert.IsTrue(results.All(row => SearchResultCategorizer.Matches(SearchCategoryFilter.Audio, row)));
        Assert.IsFalse(results.Any(row => row.FullPath.Contains("__SHOW_MORE__")),
            "a filtered list has no overflow left to offer");
    }

    private static Core.SearchResult Record(string path) => new()
    {
        Path = path,
        Name = System.IO.Path.GetFileName(path),
        IsDir = false,
        Drive = "D",
    };
}
