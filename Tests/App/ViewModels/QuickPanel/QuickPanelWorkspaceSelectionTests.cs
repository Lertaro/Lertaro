using Lertaro.App.ViewModels.QuickPanel;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.QuickPanel;

// Which workspace the panel comes up on, and what switching between them does. Split from
// QuickPanelViewModelTests, which covers what one workspace turns into once chosen, both to keep either
// file under the repo's per-file line limit and because the two ask genuinely different questions.
[TestClass]
public sealed class QuickPanelWorkspaceSelectionTests
{
    private static SearchResult Entry(string folder, string name) => new()
    {
        Name = name,
        Path = System.IO.Path.Combine(folder, name),
        Metadata = new PluginSdk.Abstractions.FileMetadata(
            0, new DateTime(2026, 1, 1), new DateTime(2026, 1, 1), new DateTime(2026, 1, 1)),
    };

    private static QuickPanelFolderSource Folder(string path, string id)
        => new() { Id = id, Path = path };

    private static Task<List<SearchResult>> OneEach(QuickPanelFolderSource source, CancellationToken token)
        => Task.FromResult(new List<SearchResult> { Entry(source.Path, "file.txt") });

    private static QuickPanelViewModel Build(QuickPanelSettings settings)
        => new(() => settings, OneEach, saveSettings: () => { });

    private static QuickPanelSettings OneWorkspace()
    {
        var tab = new QuickPanelTab { Id = "w1", Name = "Work" };
        tab.Folders.Add(Folder(@"C:\a", "s1"));
        return new QuickPanelSettings { Tabs = new List<QuickPanelTab> { tab }, ActiveTabId = tab.Id };
    }

    // Every workspace is loaded on a refresh and one that came back empty gets no tab, so a case about
    // tabs has to give each of them a source that answers.
    private static QuickPanelTab Workspace(string id, string sourceId, params string[] processes)
    {
        var tab = new QuickPanelTab { Id = id, Name = id };
        tab.Folders.Add(Folder(@"C:\" + sourceId, sourceId));
        tab.Processes.AddRange(processes);
        return tab;
    }

    [TestMethod]
    public async Task Refresh_DisabledWorkspace_GetsNoTab()
    {
        var settings = OneWorkspace();
        settings.Tabs.Add(new QuickPanelTab { Id = "w2", Name = "Off", Enabled = false });

        var vm = Build(settings);
        await vm.RefreshAsync();

        CollectionAssert.AreEqual(new[] { "w1" }, vm.Tabs.Select(t => t.Id).ToList());
        Assert.IsTrue(vm.HasTabStrip, "one workspace still gets a strip -- it carries the name and the close button");
    }

    [TestMethod]
    public async Task Refresh_TheAppInFront_PicksTheWorkspaceThatClaimsIt()
    {
        var settings = OneWorkspace();
        settings.Tabs.Add(Workspace("w2", "s9", "premiere"));

        var vm = Build(settings);
        await vm.RefreshAsync("premiere.exe");

        Assert.AreEqual("w2", vm.Tabs.Single(t => t.IsSelected).Id);
        CollectionAssert.AreEqual(new[] { "s9" }, vm.Groups.Select(g => g.SourceId).ToList());
    }

    // A workspace with no tab must not be reachable by a process rule either.
    [TestMethod]
    public async Task Refresh_DisabledWorkspace_CannotClaimTheAppInFront()
    {
        var settings = OneWorkspace();
        settings.Tabs.Add(new QuickPanelTab { Id = "w2", Enabled = false, Processes = { "premiere" } });

        var vm = Build(settings);
        await vm.RefreshAsync("premiere.exe");

        Assert.AreEqual("w1", vm.Tabs.Single(t => t.IsSelected).Id);
    }

    // Nor an empty one, for the same reason: the rule picks among the workspaces that have a tab.
    [TestMethod]
    public async Task Refresh_EmptyWorkspace_CannotClaimTheAppInFront()
    {
        var settings = OneWorkspace();
        settings.Tabs.Add(Workspace("w2", "s9", "premiere"));

        var vm = new QuickPanelViewModel(
            () => settings,
            (source, _) => Task.FromResult(source.Id == "s9"
                ? new List<SearchResult>()
                : new List<SearchResult> { Entry(source.Path, "file.txt") }),
            saveSettings: () => { });
        await vm.RefreshAsync("premiere.exe");

        Assert.AreEqual("w1", vm.Tabs.Single(t => t.IsSelected).Id);
    }

    [TestMethod]
    public async Task Refresh_NoAppClaimsIt_OpensOnTheRecordedTab()
    {
        var settings = OneWorkspace();
        settings.Tabs.Add(Workspace("w2", "s2"));
        settings.ActiveTabId = "w2";

        var vm = Build(settings);
        await vm.RefreshAsync("notepad.exe");

        Assert.AreEqual("w2", vm.Tabs.Single(t => t.IsSelected).Id);
    }

    // Where the user left the panel outranks the recorded tab, which is only the starting point.
    [TestMethod]
    public async Task Refresh_ReopensWhereTheUserLeftIt()
    {
        var settings = OneWorkspace();
        settings.Tabs.Add(Workspace("w2", "s2"));

        var vm = Build(settings);
        await vm.RefreshAsync();
        await vm.SelectTabAsync("w2");
        await vm.RefreshAsync();

        Assert.AreEqual("w2", vm.Tabs.Single(t => t.IsSelected).Id);
    }

    // ...but the app in front still outranks both: a workspace that names an app is a statement that
    // this app means these folders.
    [TestMethod]
    public async Task Refresh_TheAppInFront_OutranksWhereTheUserLeftIt()
    {
        var settings = OneWorkspace();
        settings.Tabs[0].Processes.Add("notepad");
        settings.Tabs.Add(Workspace("w2", "s2"));

        var vm = Build(settings);
        await vm.RefreshAsync();
        await vm.SelectTabAsync("w2");
        await vm.RefreshAsync("notepad.exe");

        Assert.AreEqual("w1", vm.Tabs.Single(t => t.IsSelected).Id);
    }

    [TestMethod]
    public async Task SelectTab_SwapsTheGroupsForTheOtherWorkspace()
    {
        var settings = OneWorkspace();
        settings.Tabs.Add(Workspace("w2", "s2"));

        var vm = Build(settings);
        await vm.RefreshAsync();
        var groupChanges = 0;
        vm.Groups.CollectionChanged += (_, _) => groupChanges++;
        await vm.SelectTabAsync("w2");

        CollectionAssert.AreEqual(new[] { "s2" }, vm.Groups.Select(g => g.SourceId).ToList());
        Assert.AreEqual(1, groupChanges, "a tab swap should rebuild the groups visual tree only once");
        Assert.IsTrue(vm.Tabs.Single(t => t.Id == "w2").IsSelected);
        Assert.IsFalse(vm.Tabs.Single(t => t.Id == "w1").IsSelected);
        Assert.IsTrue(vm.HasTabStrip);
    }

    [TestMethod]
    public async Task SelectTab_AWorkspaceThatIsNotThere_ChangesNothing()
    {
        var settings = OneWorkspace();
        var vm = Build(settings);
        await vm.RefreshAsync();

        await vm.SelectTabAsync("nope");

        Assert.AreEqual("w1", vm.Tabs.Single(t => t.IsSelected).Id);
        CollectionAssert.AreEqual(new[] { "s1" }, vm.Groups.Select(g => g.SourceId).ToList());
    }

    // What the number keys reach: the strip's own positions, 1-based, and nothing outside it.
    [TestMethod]
    public async Task SelectTabAt_CountsFromOneAndIgnoresAnythingPastTheEnd()
    {
        var settings = OneWorkspace();
        settings.Tabs.Add(Workspace("w2", "s2"));

        var vm = Build(settings);
        await vm.RefreshAsync();

        await vm.SelectTabAtAsync(2);
        Assert.AreEqual("w2", vm.Tabs.Single(t => t.IsSelected).Id);

        await vm.SelectTabAtAsync(3);
        Assert.AreEqual("w2", vm.Tabs.Single(t => t.IsSelected).Id);

        await vm.SelectTabAtAsync(1);
        Assert.AreEqual("w1", vm.Tabs.Single(t => t.IsSelected).Id);
    }
}
