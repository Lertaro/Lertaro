using Lertaro.App.ViewModels.QuickPanel;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.QuickPanel;

// The box at the top of the panel: what it narrows, what it hides, and what it deliberately leaves
// alone. A filter over what the workspace already loaded, never a search past it.
[TestClass]
public sealed class QuickPanelFilterTests
{
    private static SearchResult Entry(string folder, string name) => new()
    {
        Name = name,
        Path = System.IO.Path.Combine(folder, name),
        Metadata = new PluginSdk.Abstractions.FileMetadata(
            0, new DateTime(2026, 1, 1), new DateTime(2026, 1, 1), new DateTime(2026, 1, 1)),
    };

    // Two folders: one holding a report and a photo, the other only invoices.
    private static readonly Dictionary<string, string[]> Contents = new()
    {
        ["s1"] = new[] { "report.docx", "holiday.jpg" },
        ["s2"] = new[] { "invoice-2026.pdf" },
    };

    private static QuickPanelViewModel Build()
    {
        var workspace = new QuickPanelTab { Id = "w1", Name = "Work" };
        workspace.Folders.Add(new QuickPanelFolderSource { Id = "s1", Path = @"C:\docs" });
        workspace.Folders.Add(new QuickPanelFolderSource { Id = "s2", Path = @"C:\bills" });
        var settings = new QuickPanelSettings
        {
            Tabs = new List<QuickPanelTab> { workspace },
            ActiveTabId = "w1",
        };

        return new QuickPanelViewModel(
            () => settings,
            (source, _) => Task.FromResult(
                Contents[source.Id].Select(name => Entry(source.Path, name)).ToList()),
            saveSettings: () => { });
    }

    private static async Task<QuickPanelViewModel> Loaded()
    {
        var vm = Build();
        await vm.RefreshAsync();
        return vm;
    }

    [TestMethod]
    public async Task Filter_KeepsOnlyTheEntriesWhoseNameContainsIt()
    {
        var vm = await Loaded();

        vm.SearchQuery = "report";

        CollectionAssert.AreEqual(new[] { "report.docx" }, vm.Groups[0].Items.Select(i => i.Name).ToList());
        Assert.IsTrue(vm.Groups[0].HasMatches);
        Assert.IsFalse(vm.Groups[1].HasMatches, "a group the filter emptied is hidden, not shown at zero");
    }

    // The heading counts what is shown, which under a filter is what matched -- a heading still reading
    // the full total beside a shortened list would be contradicting the list under it.
    [TestMethod]
    public async Task Filter_GroupCount_FollowsWhatIsShown()
    {
        var vm = await Loaded();
        Assert.AreEqual(2, vm.Groups[0].Count);

        vm.SearchQuery = "holiday";

        Assert.AreEqual(1, vm.Groups[0].Count);
    }

    // The matcher splits a query on spaces into terms that must all match, so the box hands it a
    // trimmed one rather than a stray trailing space that reads as an extra empty term.
    [TestMethod]
    public async Task Filter_IgnoresSurroundingSpace()
    {
        var vm = await Loaded();

        vm.SearchQuery = "  report  ";

        CollectionAssert.AreEqual(new[] { "report.docx" }, vm.Groups[0].Items.Select(i => i.Name).ToList());
    }

    // fzf's own smart case, which is what every other box in this app does: a lower-case query ignores
    // case, and typing a capital is how you ask for one.
    [TestMethod]
    public async Task Filter_LowerCaseQuery_IgnoresCase()
    {
        var vm = await Loaded();

        vm.SearchQuery = "REPORT";
        Assert.IsFalse(vm.Groups[0].HasMatches, "a capital in the query makes it case-sensitive");

        vm.SearchQuery = "report";
        CollectionAssert.AreEqual(new[] { "report.docx" }, vm.Groups[0].Items.Select(i => i.Name).ToList());
    }

    // The point of borrowing the index's own matcher rather than a substring test: a subsequence hits.
    [TestMethod]
    public async Task Filter_MatchesASubsequence()
    {
        var vm = await Loaded();

        vm.SearchQuery = "rpt";

        CollectionAssert.AreEqual(new[] { "report.docx" }, vm.Groups[0].Items.Select(i => i.Name).ToList());
    }

    [TestMethod]
    public async Task Filter_MatchingNothingAnywhere_SaysTheTabIsEmpty()
    {
        var vm = await Loaded();

        vm.SearchQuery = "zzzz";

        Assert.IsTrue(vm.IsEmpty);
        Assert.IsFalse(vm.Groups.Any(g => g.HasMatches));
    }

    [TestMethod]
    public async Task Filter_Cleared_BringsEverythingBack()
    {
        var vm = await Loaded();
        vm.SearchQuery = "report";

        vm.ClearSearchCommand.Execute(null);

        Assert.AreEqual(string.Empty, vm.SearchQuery);
        Assert.AreEqual(2, vm.Groups[0].Count);
        Assert.IsTrue(vm.Groups[1].HasMatches);
        Assert.IsFalse(vm.IsEmpty);
    }

    // A filter narrows what is on screen; it does not reach into the folders again. Only the sources
    // that loaded something have a group at all, and that does not change as the query does.
    [TestMethod]
    public async Task Filter_DoesNotChangeWhichGroupsExist()
    {
        var vm = await Loaded();

        vm.SearchQuery = "invoice";

        Assert.HasCount(2, vm.Groups);
    }

    // The panel is closed with something typed and opened again: every open builds a new box, which is
    // empty on screen, so a query surviving on the view model would narrow the list by something the
    // user cannot see and did not type.
    [TestMethod]
    public async Task Refresh_ForgetsWhatWasTyped()
    {
        var vm = await Loaded();
        vm.SearchQuery = "report";

        await vm.RefreshAsync();

        Assert.AreEqual(string.Empty, vm.SearchQuery);
        Assert.AreEqual(2, vm.Groups[0].Count, "and the list it had narrowed comes back whole");
    }

    [TestMethod]
    public async Task Filter_SurvivesSwitchingWorkspace()
    {
        var workspace2 = new QuickPanelTab { Id = "w2", Name = "Other" };
        workspace2.Folders.Add(new QuickPanelFolderSource { Id = "s3", Path = @"C:\other" });
        var settings = new QuickPanelSettings
        {
            Tabs = new List<QuickPanelTab>
            {
                new() { Id = "w1", Name = "Work", Folders = { new QuickPanelFolderSource { Id = "s1", Path = @"C:\docs" } } },
                workspace2,
            },
            ActiveTabId = "w1",
        };

        var vm = new QuickPanelViewModel(
            () => settings,
            (source, _) => Task.FromResult(source.Id == "s1"
                ? new List<SearchResult> { Entry(source.Path, "report.docx") }
                : new List<SearchResult> { Entry(source.Path, "notes.txt"), Entry(source.Path, "report-2.docx") }),
            saveSettings: () => { });
        await vm.RefreshAsync();
        vm.SearchQuery = "report";

        await vm.SelectTabAsync("w2");

        // The box still says "report", so the workspace switched into is narrowed by it too.
        CollectionAssert.AreEqual(new[] { "report-2.docx" }, vm.Groups[0].Items.Select(i => i.Name).ToList());
    }
}
