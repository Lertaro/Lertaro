using Lertaro.App.ViewModels.Search;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.ViewModels.Search;

// The quick window resolves a candidate's display type rank for every row it maps, so the dictionary form
// has to agree with the list form it replaced -- including the two details that decide real rankings: the
// first occurrence wins, and an id that is not listed at all sorts after every listed one.
[TestClass]
public sealed class SearchResultTypePriorityTests
{
    [TestMethod]
    public void FilesComesFirst_ReadsTheTopOfTheSavedOrder()
    {
        Assert.IsTrue(SearchResultTypePriority.FilesComesFirst(
            [SearchResultTypePriority.FilesTypeId, "apps", "settings"]));
        Assert.IsFalse(SearchResultTypePriority.FilesComesFirst(
            ["apps", SearchResultTypePriority.FilesTypeId, "settings"]));
        Assert.IsFalse(SearchResultTypePriority.FilesComesFirst(
            ["apps", "settings", SearchResultTypePriority.FilesTypeId]));
        // No saved order at all is not "Files first": unlisted types stay neutral and the cap keeps applying.
        Assert.IsFalse(SearchResultTypePriority.FilesComesFirst([]));
        Assert.IsFalse(SearchResultTypePriority.FilesComesFirst(["apps", "settings"]));
    }

    [TestMethod]
    public void BuildRanks_MatchesTheListOverload()
    {
        var orders = new[]
        {
            new List<string>(),
            ["apps", "files", "apps"],
            [SearchResultTypePriority.FilesTypeId, "apps"],
            ["settings"],
        };

        foreach (var order in orders)
        {
            var ranks = SearchResultTypePriority.BuildRanks(order);
            foreach (var typeId in order.Append("unlisted").Append(SearchResultTypePriority.FilesTypeId))
            {
                Assert.AreEqual(
                    SearchResultTypePriority.Rank(typeId, order),
                    SearchResultTypePriority.Rank(typeId, ranks),
                    $"{typeId} in [{string.Join(", ", order)}]");
            }
        }
    }

    [TestMethod]
    public void BuildRanks_FirstOccurrenceWins()
    {
        var ranks = SearchResultTypePriority.BuildRanks(["apps", "files", "apps"]);

        Assert.AreEqual(0, SearchResultTypePriority.Rank("apps", ranks));
        Assert.AreEqual(1, SearchResultTypePriority.Rank("files", ranks));
    }

    [TestMethod]
    public void Rank_UnknownTypeIsMaxValue()
    {
        var ranks = SearchResultTypePriority.BuildRanks(["apps"]);

        Assert.AreEqual(int.MaxValue, SearchResultTypePriority.Rank("nope", ranks));
    }

    [TestMethod]
    public void Rank_CaseSensitiveLikeTheOldIndexOf()
    {
        var ranks = SearchResultTypePriority.BuildRanks(["Apps"]);

        Assert.AreEqual(int.MaxValue, SearchResultTypePriority.Rank("apps", ranks));
    }

    // An empty order is the inline window's shape: no type layer at all, every row tied.
    [TestMethod]
    public void Rank_EmptyOrderPutsEverythingLast() => Assert.AreEqual(
            int.MaxValue,
            SearchResultTypePriority.Rank(SearchResultTypePriority.FilesTypeId, SearchResultTypePriority.BuildRanks([])));

    // A shortcut is ranked as whichever of its two identities the user put first. Min is what makes
    // "Files" before "Applications" a no-op for shortcuts instead of a silent demotion.
    [TestMethod]
    public void ShortcutRank_KeepsWhicheverIdentityIsRankedHigher()
    {
        Assert.AreEqual(0, SearchResultTypePriority.ShortcutRank(appRank: 0, filesRank: 2));
        Assert.AreEqual(0, SearchResultTypePriority.ShortcutRank(appRank: 2, filesRank: 0));
        Assert.AreEqual(1, SearchResultTypePriority.ShortcutRank(int.MaxValue, 1));
        Assert.AreEqual(int.MaxValue, SearchResultTypePriority.ShortcutRank(int.MaxValue, int.MaxValue));
    }

    [TestMethod]
    public void IsShortcutPath_RecognisesTheShortcutExtensionsRegardlessOfCase()
    {
        Assert.IsTrue(SearchResultTypePriority.IsShortcutPath(@"D:\tools\smalltools\Auslogics Duplicate File Finder.lnk"));
        Assert.IsTrue(SearchResultTypePriority.IsShortcutPath(@"D:\tools\App.APPREF-MS"));
    }

    [TestMethod]
    public void IsShortcutPath_LeavesAnExecutableAndPlainPathsAlone()
    {
        Assert.IsFalse(SearchResultTypePriority.IsShortcutPath(@"D:\tools\smalltools\重复文件"));
        Assert.IsFalse(SearchResultTypePriority.IsShortcutPath(@"D:\tools\app.exe"));
        Assert.IsFalse(SearchResultTypePriority.IsShortcutPath(@"D:\tools\notes.lnk.txt"));
        Assert.IsFalse(SearchResultTypePriority.IsShortcutPath(@"D:\tools\Portal.URL"),
            "a web shortcut opens a page, so it is not an application identity");
    }

    [TestMethod]
    public void TypeRankForRow_OnlyMovesAShortcutFileStillOnTheFilesRank()
    {
        const int files = 2, app = 0;

        Assert.AreEqual(app, SearchResultTypePriority.TypeRankForRow(files, false, @"D:\a.lnk", files, app));
        Assert.AreEqual(files, SearchResultTypePriority.TypeRankForRow(files, true, @"D:\a.lnk", files, app),
            "a directory keeps whatever rank its type gave it");
        Assert.AreEqual(files, SearchResultTypePriority.TypeRankForRow(files, false, @"D:\a.exe", files, app),
            "an executable is a file, not an application shortcut");
        Assert.AreEqual(files, SearchResultTypePriority.TypeRankForRow(files, false, @"D:\a.url", files, app),
            "a .url is a file, not an application shortcut");
        Assert.AreEqual(1, SearchResultTypePriority.TypeRankForRow(1, false, @"D:\a.lnk", files, app),
            "a row already carrying the app rank is left alone");
    }

    // The app type is resolved from what the provider declares, not from a name or a hard-coded id: this
    // is what lets a .lnk inherit the application rank even though the app provider never indexed it.
    [TestMethod]
    public void RankForResultKind_ResolvesOnlyTheProviderDeclaringThatKind()
    {
        ISearchableItemProvider[] providers = [new FakeAppProvider(), new FakeSettingsProvider(), new FakePlainProvider()];
        var appId = SearchResultTypePriority.GetProviderTypeId(providers[0]);
        var settingsId = SearchResultTypePriority.GetProviderTypeId(providers[1]);
        var appFirst = SearchResultTypePriority.BuildRanks([appId, settingsId, SearchResultTypePriority.FilesTypeId]);
        var settingsFirst = SearchResultTypePriority.BuildRanks([settingsId, appId, SearchResultTypePriority.FilesTypeId]);

        Assert.AreEqual(0, SearchResultTypePriority.RankForResultKind(SearchResultTypePriority.ApplicationResultKind, providers, appFirst));
        Assert.AreEqual(1, SearchResultTypePriority.RankForResultKind(SearchResultTypePriority.ApplicationResultKind, providers, settingsFirst));
        Assert.AreEqual(1, SearchResultTypePriority.RankForResultKind("Settings", providers, appFirst));
        Assert.AreEqual(int.MaxValue, SearchResultTypePriority.RankForResultKind(
            SearchResultTypePriority.ApplicationResultKind, providers, SearchResultTypePriority.BuildRanks([])));
    }

    [TestMethod]
    public void RankForApplication_FallsBackToTheBestNonFilesRankWhenNoProviderIsLoaded()
    {
        var ranks = SearchResultTypePriority.BuildRanks(
            [SearchResultTypePriority.FilesTypeId, "settings", "third"]);

        // The Start Menu provider is absent, so no provider declares "Application"; a .lnk must still
        // borrow the best listed non-Files bucket instead of staying on the Files rank.
        Assert.AreEqual(1, SearchResultTypePriority.RankForApplication([], ranks));
    }

    [TestMethod]
    public void RankForApplication_PrefersTheProviderThatDeclaresApplication()
    {
        ISearchableItemProvider[] providers = [new FakeAppProvider(), new FakeSettingsProvider()];
        var appId = SearchResultTypePriority.GetProviderTypeId(providers[0]);
        var settingsId = SearchResultTypePriority.GetProviderTypeId(providers[1]);
        var ranks = SearchResultTypePriority.BuildRanks(
            [settingsId, appId, SearchResultTypePriority.FilesTypeId]);

        Assert.AreEqual(1, SearchResultTypePriority.RankForApplication(providers, ranks));
    }

    [TestMethod]
    public void IsCoreTypeRank_OnlyAcceptsTheThreeBuiltInBuckets()
    {
        Assert.IsTrue(SearchResultTypePriority.IsCoreTypeRank(0));
        Assert.IsTrue(SearchResultTypePriority.IsCoreTypeRank(2));
        Assert.IsFalse(SearchResultTypePriority.IsCoreTypeRank(3));
        Assert.IsFalse(SearchResultTypePriority.IsCoreTypeRank(int.MaxValue));
    }

    private sealed class FakeAppProvider : ISearchableItemProvider
    {
        public string? ItemResultKind => SearchResultTypePriority.ApplicationResultKind;
        public IEnumerable<SearchableItem> GetSearchableItems() => [];
        public event Action? ItemsChanged { add { } remove { } }
    }

    private sealed class FakeSettingsProvider : ISearchableItemProvider
    {
        public string? ItemResultKind => "Settings";
        public IEnumerable<SearchableItem> GetSearchableItems() => [];
        public event Action? ItemsChanged { add { } remove { } }
    }

    private sealed class FakePlainProvider : ISearchableItemProvider
    {
        public IEnumerable<SearchableItem> GetSearchableItems() => [];
        public event Action? ItemsChanged { add { } remove { } }
    }
}
