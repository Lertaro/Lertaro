using Lertaro.App.ViewModels.QuickPanel;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.QuickPanel;

// Covers the assembly the panel does between the settings and what is on screen: which workspace it
// opens on, which of that workspace's sources become groups, in what order, and what each one is
// called. The loading itself goes through the index and is handed in, so none of this touches one.
[TestClass]
public sealed class QuickPanelViewModelTests
{
    private static SearchResult Entry(string folder, string name, DateTime modified) => new()
    {
        Name = name,
        Path = System.IO.Path.Combine(folder, name),
        Metadata = new PluginSdk.Abstractions.FileMetadata(0, modified, modified, modified),
    };

    private static QuickPanelFolderSource Folder(string path, string id, QuickPanelSourceKind kind = QuickPanelSourceKind.RecentFiles)
        => new() { Id = id, Path = path, Kind = kind };

    // Every source answers with one entry named after its own folder, unless a case says otherwise --
    // enough to tell the groups apart without any of them being empty and dropped.
    private static Task<List<SearchResult>> OneEach(QuickPanelFolderSource source, CancellationToken token)
        => Task.FromResult(new List<SearchResult> { Entry(source.Path, "file.txt", new DateTime(2026, 1, 1)) });

    private static QuickPanelViewModel Build(
        QuickPanelSettings settings,
        Func<QuickPanelFolderSource, CancellationToken, Task<List<SearchResult>>>? load = null)
        => new(() => settings, load ?? OneEach, saveSettings: () => { });

    private static QuickPanelSettings OneWorkspace(params QuickPanelFolderSource[] folders)
    {
        var tab = new QuickPanelTab { Id = "w1", Name = "Work" };
        tab.Folders.AddRange(folders);
        return new QuickPanelSettings { Tabs = new List<QuickPanelTab> { tab }, ActiveTabId = tab.Id };
    }

    // A second workspace with something in it. Every workspace is loaded on a refresh and one that came
    // back empty gets no tab, so a case about tabs has to give each of them a source that answers.
    private static QuickPanelTab SecondWorkspace(string id = "w2", string sourceId = "s2")
    {
        var tab = new QuickPanelTab { Id = id, Name = "Other" };
        tab.Folders.Add(Folder(@"C:\" + sourceId, sourceId));
        return tab;
    }

    [TestMethod]
    public async Task Refresh_BuildsOneGroupPerVisibleSource()
    {
        var settings = OneWorkspace(Folder(@"C:\a", "s1"), Folder(@"C:\b", "s2"));
        var vm = Build(settings);

        await vm.RefreshAsync();

        CollectionAssert.AreEqual(new[] { "s1", "s2" }, vm.Groups.Select(g => g.SourceId).ToList());
        Assert.IsTrue(vm.HasContent);
        Assert.IsFalse(vm.IsEmpty);
    }

    [TestMethod]
    public async Task Refresh_FollowsTheStoredOrderAndSkipsHiddenSources()
    {
        var settings = OneWorkspace(Folder(@"C:\a", "s1"), Folder(@"C:\b", "s2"), Folder(@"C:\c", "s3"));
        var workspace = settings.Tabs[0];
        workspace.GroupOrder = new List<string> { "s3", "s1" };
        workspace.DisabledGroupIds = new List<string> { "s1" };

        var vm = Build(settings);
        await vm.RefreshAsync();

        // s3 leads because the order says so; s1 is hidden; s2 is unlisted and keeps its position after
        // everything that is listed.
        CollectionAssert.AreEqual(new[] { "s3", "s2" }, vm.Groups.Select(g => g.SourceId).ToList());
    }

    // A configured folder that currently has nothing costs a heading and a row of a panel that is a
    // quarter of the window it docks to, and says only what the settings page already does.
    [TestMethod]
    public async Task Refresh_SourceWithNothingInIt_GetsNoGroup()
    {
        var settings = OneWorkspace(Folder(@"C:\a", "s1"), Folder(@"C:\b", "s2"));
        var vm = Build(settings, (source, _) => Task.FromResult(source.Id == "s1"
            ? new List<SearchResult>()
            : new List<SearchResult> { Entry(source.Path, "file.txt", new DateTime(2026, 1, 1)) }));

        await vm.RefreshAsync();

        CollectionAssert.AreEqual(new[] { "s2" }, vm.Groups.Select(g => g.SourceId).ToList());
    }

    // One unreachable folder -- a disconnected drive, a permission change -- must not take the rest of
    // the workspace with it.
    [TestMethod]
    public async Task Refresh_SourceThatThrows_LosesOnlyItsOwnGroup()
    {
        var settings = OneWorkspace(Folder(@"C:\a", "s1"), Folder(@"C:\b", "s2"));
        var vm = Build(settings, (source, _) => source.Id == "s1"
            ? throw new UnauthorizedAccessException("no")
            : Task.FromResult(new List<SearchResult> { Entry(source.Path, "file.txt", new DateTime(2026, 1, 1)) }));

        await vm.RefreshAsync();

        CollectionAssert.AreEqual(new[] { "s2" }, vm.Groups.Select(g => g.SourceId).ToList());
    }

    [TestMethod]
    public async Task Refresh_EmptyWorkspace_SaysSoAndIsNotWorthOpening()
    {
        var settings = OneWorkspace(Folder(@"C:\a", "s1"));
        var vm = Build(settings, (_, _) => Task.FromResult(new List<SearchResult>()));

        await vm.RefreshAsync();

        Assert.IsTrue(vm.IsEmpty);
        Assert.IsFalse(vm.HasContent, "a single empty workspace has nothing the panel could show");
    }

    // A tab is only worth a place in the strip if there is something behind it, which is why every
    // workspace is loaded on a refresh and not just the one about to be shown.
    [TestMethod]
    public async Task Refresh_WorkspaceThatLoadedNothing_GetsNoTab()
    {
        var settings = OneWorkspace(Folder(@"C:\a", "s1"));
        var empty = new QuickPanelTab { Id = "w2", Name = "Other" };
        empty.Folders.Add(Folder(@"C:\empty", "s2"));
        settings.Tabs.Add(empty);

        var vm = Build(settings, (source, _) => Task.FromResult(source.Id == "s1"
            ? new List<SearchResult> { Entry(source.Path, "file.txt", new DateTime(2026, 1, 1)) }
            : new List<SearchResult>()));
        await vm.RefreshAsync();

        CollectionAssert.AreEqual(new[] { "w1" }, vm.Tabs.Select(t => t.Id).ToList());
        Assert.IsTrue(vm.HasContent);
    }

    // A workspace whose every entry the filter rejects keeps its tab: the strip says which workspaces
    // have something in them, and having it flicker per keystroke would say something far less useful.
    [TestMethod]
    public async Task Filter_MatchingNothing_StillLeavesTheStripAlone()
    {
        var settings = OneWorkspace(Folder(@"C:\a", "s1"));
        settings.Tabs.Add(SecondWorkspace());
        var vm = Build(settings);
        await vm.RefreshAsync();

        vm.SearchQuery = "nothing-matches-this";

        Assert.IsTrue(vm.IsEmpty);
        Assert.HasCount(2, vm.Tabs);
    }

    [TestMethod]
    public async Task Refresh_GroupTitle_IsTheFolderNameUntilItIsRenamed()
    {
        var settings = OneWorkspace(Folder(@"C:\Users\me\Downloads", "s1"), Folder(@"C:\b", "s2"));
        settings.Tabs[0].GroupPreferences["s2"] = new QuickPanelGroupPreference { DisplayName = "  素材  " };

        var vm = Build(settings);
        await vm.RefreshAsync();

        Assert.AreEqual("Downloads", vm.Groups[0].Title);
        Assert.AreEqual("素材", vm.Groups[1].Title);
    }

    // The kind IS the order choice, so a launcher folder must not come up newest-first.
    [TestMethod]
    public async Task Refresh_GroupOrder_ComesFromTheSourceKind()
    {
        var settings = OneWorkspace(
            Folder(@"C:\a", "s1", QuickPanelSourceKind.All),
            Folder(@"C:\b", "s2", QuickPanelSourceKind.AllByModified));

        var vm = Build(settings, (source, _) => Task.FromResult(new List<SearchResult>
        {
            Entry(source.Path, "b.txt", new DateTime(2026, 1, 2)),
            Entry(source.Path, "a.txt", new DateTime(2026, 1, 3)),
        }));
        await vm.RefreshAsync();

        Assert.AreEqual(QuickPanelSortMode.NameAscending, vm.Groups[0].SortMode);
        Assert.AreEqual("a.txt", vm.Groups[0].Items[0].Name);
        Assert.AreEqual(QuickPanelSortMode.ModifiedDescending, vm.Groups[1].SortMode);
        Assert.AreEqual("a.txt", vm.Groups[1].Items[0].Name, "newest first, which this file also is");
    }

    [TestMethod]
    public async Task Refresh_StoredViewAndExpandedState_AreHonoured()
    {
        var settings = OneWorkspace(Folder(@"C:\a", "s1"));
        settings.Tabs[0].GroupPreferences["s1"] = new QuickPanelGroupPreference
        {
            ThumbnailView = false,
            Expanded = false,
        };

        var vm = Build(settings);
        await vm.RefreshAsync();

        Assert.IsFalse(vm.Groups[0].IsThumbnailView);
        Assert.IsFalse(vm.Groups[0].IsExpanded);
    }

    // The whole way round, because the two halves agree only if every id survives the trip: the settings
    // page edits a clone, files the new name under the source's id, and Save puts the clone back -- and
    // the panel then has to find that entry again by the same id off the live settings object.
    [TestMethod]
    public async Task RenamingASourceInSettings_ShowsUpAsTheGroupHeading()
    {
        var userSettings = new UserSettings();
        var workspace = new QuickPanelTab { Id = "w1" };
        workspace.Folders.Add(QuickPanelFolderSource.For(@"C:\Users\me\Desktop"));
        userSettings.QuickPanel.Tabs = new List<QuickPanelTab> { workspace };
        userSettings.QuickPanel.ActiveTabId = "w1";

        var page = new Lertaro.App.ViewModels.Settings.QuickPanel.QuickPanelSettingsViewModel(userSettings);
        page.Tabs.Single().Sources.Single().DisplayName = "11212";
        page.Save();

        var vm = new QuickPanelViewModel(() => userSettings.QuickPanel, OneEach, saveSettings: () => { });
        await vm.RefreshAsync();

        Assert.AreEqual("11212", vm.Groups.Single().Title);
    }

    [TestMethod]
    public async Task Refresh_NoWorkspacesAtAll_IsNotWorthOpening()
    {
        var vm = Build(new QuickPanelSettings { Tabs = new List<QuickPanelTab>() });

        await vm.RefreshAsync();

        Assert.IsEmpty(vm.Tabs);
        Assert.IsEmpty(vm.Groups);
        Assert.IsFalse(vm.HasContent);
        Assert.IsFalse(vm.HasTabStrip, "no workspaces at all is the one case with no strip");
    }
}
