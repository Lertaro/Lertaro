using Lertaro.App.ViewModels.QuickPanel;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.QuickPanel;

// The two things the strip does TO the settings rather than with them: closing a tab disables its
// workspace, and dragging one stores the new order. Both write, so both are covered here rather than
// alongside the panel's read-only assembly (see QuickPanelViewModelTests).
[TestClass]
public sealed class QuickPanelTabStripTests
{
    private static Task<List<SearchResult>> OneEach(QuickPanelFolderSource source, CancellationToken token)
        => Task.FromResult(new List<SearchResult>
        {
            new()
            {
                Name = "file.txt",
                Path = System.IO.Path.Combine(source.Path, "file.txt"),
                Metadata = new PluginSdk.Abstractions.FileMetadata(0, new DateTime(2026, 1, 1), new DateTime(2026, 1, 1), new DateTime(2026, 1, 1)),
            },
        });

    private static QuickPanelTab Workspace(string id, bool enabled = true)
    {
        var tab = new QuickPanelTab { Id = id, Name = id, Enabled = enabled };
        tab.Folders.Add(new QuickPanelFolderSource { Id = id + "s", Path = @"C:\" + id });
        return tab;
    }

    private static QuickPanelSettings Settings(params QuickPanelTab[] tabs)
        => new() { Tabs = tabs.ToList(), ActiveTabId = tabs.Length > 0 ? tabs[0].Id : string.Empty };

    private static (QuickPanelViewModel Vm, Counter Saves) Build(QuickPanelSettings settings)
    {
        var saves = new Counter();
        return (new QuickPanelViewModel(() => settings, OneEach, () => saves.Count++), saves);
    }

    private sealed class Counter { public int Count; }

    [TestMethod]
    public async Task CloseTab_DisablesTheWorkspaceRatherThanDeletingIt()
    {
        var settings = Settings(Workspace("w1"), Workspace("w2"));
        var (vm, saves) = Build(settings);
        await vm.RefreshAsync();

        await vm.CloseTabAsync("w2");

        Assert.HasCount(2, settings.Tabs, "a closed workspace is disabled, never deleted");
        Assert.IsFalse(settings.Tabs.Single(t => t.Id == "w2").Enabled);
        CollectionAssert.AreEqual(new[] { "w1" }, vm.Tabs.Select(t => t.Id).ToList());
        Assert.AreEqual(1, saves.Count);
    }

    // Closing the tab being looked at has to land somewhere: a workspace with no tab left to reach it by
    // would leave the panel showing something the strip says is not there.
    [TestMethod]
    public async Task CloseTab_TheActiveOne_MovesToWhatIsLeft()
    {
        var settings = Settings(Workspace("w1"), Workspace("w2"));
        var (vm, _) = Build(settings);
        await vm.RefreshAsync();
        await vm.SelectTabAsync("w2");

        await vm.CloseTabAsync("w2");

        Assert.AreEqual("w1", vm.Tabs.Single(t => t.IsSelected).Id);
        CollectionAssert.AreEqual(new[] { "w1s" }, vm.Groups.Select(g => g.SourceId).ToList());
    }

    // Closing the last one is what the window watches for: HasTabStrip going false is the signal that
    // there is nothing left the panel could ever show, and it closes on it whatever else was asked.
    [TestMethod]
    public async Task CloseTab_TheLastOne_LeavesNothingAndSaysSo()
    {
        var settings = Settings(Workspace("w1"));
        var (vm, _) = Build(settings);
        await vm.RefreshAsync();

        await vm.CloseTabAsync("w1");

        Assert.IsEmpty(vm.Tabs);
        Assert.IsEmpty(vm.Groups);
        Assert.IsFalse(vm.HasTabStrip);
        Assert.IsFalse(vm.HasContent);
        Assert.IsFalse(settings.Tabs.Single().Enabled, "still there, just disabled");
    }

    [TestMethod]
    public async Task CloseTab_AlreadyClosedOrUnknown_ChangesNothing()
    {
        var settings = Settings(Workspace("w1"), Workspace("w2", enabled: false));
        var (vm, saves) = Build(settings);
        await vm.RefreshAsync();

        await vm.CloseTabAsync("w2");
        await vm.CloseTabAsync("nope");

        Assert.AreEqual(0, saves.Count);
        CollectionAssert.AreEqual(new[] { "w1" }, vm.Tabs.Select(t => t.Id).ToList());
    }

    // Dragging is a remove and an insert on the strip itself, which is what DragReorder does to any
    // IList -- so this is the shape the view model actually has to notice.
    [TestMethod]
    public async Task DraggingATab_StoresTheNewOrder()
    {
        var settings = Settings(Workspace("w1"), Workspace("w2"), Workspace("w3"));
        var (vm, saves) = Build(settings);
        await vm.RefreshAsync();

        var dragged = vm.Tabs[2];
        vm.Tabs.RemoveAt(2);
        vm.Tabs.Insert(0, dragged);

        // One order over both kinds of tab, which is what lets a plugin tab be dragged to between two
        // workspaces. It is stored on its own rather than by shuffling the workspace list, because a
        // plugin tab has no place in that list to be shuffled to.
        CollectionAssert.AreEqual(new[] { "w3", "w1", "w2" }, settings.TabOrder);
        Assert.AreEqual(1, saves.Count, "the half-finished strip mid-drag is not an order worth storing");
    }

    // A disabled workspace has no tab to drag, so a drag says nothing about it: it is left out of the
    // order entirely, which is what keeps its discovery position waiting for it when it comes back.
    [TestMethod]
    public async Task DraggingATab_SaysNothingAboutWorkspacesWithNoTab()
    {
        var settings = Settings(Workspace("w1"), Workspace("off", enabled: false), Workspace("w2"));
        var (vm, _) = Build(settings);
        await vm.RefreshAsync();

        var dragged = vm.Tabs[1];
        vm.Tabs.RemoveAt(1);
        vm.Tabs.Insert(0, dragged);

        CollectionAssert.AreEqual(new[] { "w2", "w1" }, settings.TabOrder);
        CollectionAssert.AreEqual(
            new[] { "w1", "off", "w2" },
            settings.Tabs.Select(t => t.Id).ToList(),
            "the workspaces themselves are not what the strip's order is stored in");
    }

    [TestMethod]
    public async Task Refreshing_DoesNotCountAsReordering()
    {
        var settings = Settings(Workspace("w1"), Workspace("w2"));
        var (vm, saves) = Build(settings);

        await vm.RefreshAsync();
        await vm.RefreshAsync();

        Assert.AreEqual(0, saves.Count, "rebuilding the strip is not an order the user chose");
        CollectionAssert.AreEqual(new[] { "w1", "w2" }, settings.Tabs.Select(t => t.Id).ToList());
    }
}
