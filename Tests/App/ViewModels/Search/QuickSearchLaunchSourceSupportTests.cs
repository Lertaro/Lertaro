using Lertaro.App.ViewModels.Search;
using Lertaro.Core;
using System.Windows.Media;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Tests.ViewModels.Search;

[TestClass]
public sealed class QuickSearchLaunchSourceSupportTests
{
    private static ImageSource Icon()
    {
        var icon = new DrawingImage();
        icon.Freeze();
        return icon;
    }

    [StaTestMethod]
    public void ApplySources_PreservesDisplayedIconButUsesNewActionsAndMetadata()
    {
        var support = new QuickSearchLaunchSourceSupport(_ => { });
        var old = new AppSearchResult { Name = "Old", FullPath = @"C:\Apps\tool.exe" };
        var icon = Icon();
        old.DisplayExtras.Icon = icon;
        old.DisplayExtras.IconLoadingStarted = true;
        support.Sources.Add(new("apps", "Old source", [old]));
        var executed = false;
        var next = new AppSearchResult
        {
            Name = "New", FullPath = @"c:\apps\TOOL.exe", Index = 4,
            InstantResultOnExecute = () => executed = true,
            Metadata = new FileMetadata(123, DateTime.MinValue, new DateTime(2026, 11, 10), DateTime.MinValue)
        };

        support.ApplySources([new("apps", "New source", [next])], "apps");

        var current = Assert.ContainsSingle(support.Selected!.Items);
        Assert.AreSame(next, current);
        Assert.AreSame(icon, current.ExistingExtras!.Icon);
        Assert.IsTrue(current.ExistingExtras.IconNeedsRefresh);
        Assert.IsFalse(current.ExistingExtras.IconLoadingStarted, "The old row's pending load cannot complete this new row.");
        Assert.AreEqual("New", current.Name);
        Assert.AreEqual(123L, current.Metadata.Size);
        Assert.AreEqual(4, current.Index);
        current.InstantResultOnExecute!();
        Assert.IsTrue(executed);
    }

    [StaTestMethod]
    [DataRow("other", @"C:\Apps\tool.exe", false)]
    [DataRow("apps", @"C:\Apps\different.exe", false)]
    [DataRow("apps", @"C:\Apps\tool.exe", true)]
    public void ApplySources_ChangedIdentityDoesNotInheritIcon(string sourceId, string path, bool isDirectory)
    {
        var support = new QuickSearchLaunchSourceSupport(_ => { });
        var old = new AppSearchResult { FullPath = @"C:\Apps\tool.exe" };
        old.DisplayExtras.Icon = Icon();
        support.Sources.Add(new("apps", "Apps", [old]));
        var next = new AppSearchResult { FullPath = path, IsDir = isDirectory };

        support.ApplySources([new(sourceId, "Source", [next])], sourceId);

        Assert.IsNull(next.ExistingExtras!.Icon);
        Assert.IsFalse(next.ExistingExtras.IconNeedsRefresh);
    }

    [StaTestMethod]
    public void ApplySources_NewIconOverrideWinsOverPreviousImage()
    {
        var support = new QuickSearchLaunchSourceSupport(_ => { });
        var old = new AppSearchResult { FullPath = @"C:\Apps\tool.exe" };
        old.DisplayExtras.Icon = Icon();
        support.Sources.Add(new("apps", "Apps", [old]));
        var replacement = Icon();
        var next = new AppSearchResult { FullPath = old.FullPath, IconOverride = replacement };

        support.ApplySources([new("apps", "Apps", [next])], "apps");

        Assert.AreSame(replacement, next.Icon);
        Assert.IsNull(next.ExistingExtras!.Icon);
        Assert.IsFalse(next.ExistingExtras.IconNeedsRefresh);
    }

    [StaTestMethod]
    public void ApplySources_RefreshPreservesSourceSelectionAndRemovesDeletedRows()
    {
        var support = new QuickSearchLaunchSourceSupport(_ => { });
        var old = new AppSearchResult { FullPath = @"C:\Apps\old.exe" };
        old.DisplayExtras.Icon = Icon();
        var previous = new LaunchPanelSourceViewModel("apps", "Apps", [old]);
        support.Sources.Add(previous);
        support.Select(previous);
        support.SelectItem(old);
        var other = new LaunchPanelSourceViewModel("other", "Other", []);
        var next = new LaunchPanelSourceViewModel("apps", "Apps", []);

        support.ApplySources([other, next], "apps");

        Assert.AreSame(next, support.Selected);
        Assert.IsTrue(next.IsSelected);
        Assert.IsFalse(other.IsSelected);
        Assert.IsEmpty(next.Items);
        Assert.IsNull(support.SelectedItem);
        support.ApplySources([], "apps");
        Assert.IsEmpty(support.Sources);
        Assert.IsNull(support.Selected);
    }

    [StaTestMethod]
    public void ApplySources_ColdRowsRemainLazy()
    {
        var support = new QuickSearchLaunchSourceSupport(_ => { });
        var old = new AppSearchResult { FullPath = @"C:\Apps\tool.exe" };
        support.Sources.Add(new("apps", "Apps", [old]));
        var next = new AppSearchResult { FullPath = old.FullPath };

        support.ApplySources([new("apps", "Apps", [next])], "apps");

        Assert.IsNull(old.ExistingExtras!.Icon);
        Assert.IsNull(next.ExistingExtras!.Icon);
        Assert.IsFalse(old.ExistingExtras.IconLoadingStarted);
        Assert.IsFalse(next.ExistingExtras.IconLoadingStarted);
    }

    [TestMethod]
    public void Cycle_WrapsAroundAndKeepsOnlySelectedSourceMarked()
    {
        var support = new QuickSearchLaunchSourceSupport(_ => { });
        var first = new LaunchPanelSourceViewModel("first", "First", Array.Empty<AppSearchResult>());
        var second = new LaunchPanelSourceViewModel("second", "Second", Array.Empty<AppSearchResult>());
        support.Sources.Add(first);
        support.Sources.Add(second);

        support.Select(second);
        support.Cycle(1);

        Assert.AreSame(first, support.Selected);
        Assert.IsTrue(first.IsSelected);
        Assert.IsFalse(second.IsSelected);
    }

    [TestMethod]
    public void OrderManualItems_MatchesDisplayedPathsAndPreservesUnshownItems()
    {
        var first = new QuickLaunchItemSetting { Name = "First", Path = @"C:\test\first" };
        var second = new QuickLaunchItemSetting { Name = "Second", Path = @"C:\test\second" };
        var unshown = new QuickLaunchItemSetting { Name = "Unshown", Path = @"C:\test\unshown" };
        var displayed = new[]
        {
            new AppSearchResult { FullPath = second.Path },
            new AppSearchResult { FullPath = first.Path },
        };

        var ordered = QuickSearchLaunchSourceSupport.OrderManualItems(
            new[] { first, second, unshown }, displayed);

        CollectionAssert.AreEqual(new[] { second, first, unshown }, ordered);
    }

    [TestMethod]
    public void MoveItemSelection_UsesVisualGridAndStopsAtEdges()
    {
        var support = new QuickSearchLaunchSourceSupport(_ => { });
        var items = Enumerable.Range(1, 6)
            .Select(index => new AppSearchResult { FullPath = $"item-{index}" })
            .ToArray();
        var source = new LaunchPanelSourceViewModel("source", "Source", items);
        support.Sources.Add(source);
        support.Select(source);

        support.SelectItem(items[0]);
        Assert.IsTrue(support.MoveItemSelection(0, 1, 3));
        Assert.AreSame(items[1], support.SelectedItem);
        Assert.IsTrue(support.MoveItemSelection(1, 0, 3));
        Assert.AreSame(items[4], support.SelectedItem);
        Assert.IsTrue(support.MoveItemSelection(0, 1, 3));
        Assert.AreSame(items[5], support.SelectedItem);
        Assert.IsFalse(support.MoveItemSelection(0, 1, 3));
        Assert.AreSame(items[5], support.SelectedItem);
        support.SelectItem(items[2]);
        Assert.IsTrue(support.MoveItemSelection(0, 1, 3));
        Assert.AreSame(items[3], support.SelectedItem);
        Assert.IsTrue(support.MoveItemSelection(0, -1, 3));
        Assert.AreSame(items[2], support.SelectedItem);
        Assert.IsFalse(support.MoveItemSelection(-1, 0, 3));
        Assert.AreSame(items[2], support.SelectedItem);
        Assert.IsTrue(support.MoveItemSelection(0, -1, 3));
        Assert.AreSame(items[1], support.SelectedItem);
        Assert.IsTrue(support.MoveItemSelection(0, -1, 3));
        Assert.AreSame(items[0], support.SelectedItem);
        Assert.IsFalse(support.MoveItemSelection(0, -1, 3));
        Assert.AreSame(items[0], support.SelectedItem);
    }

    [TestMethod]
    public void MoveItemSelection_StopsWhenTargetRowHasNoMatchingColumn()
    {
        var support = new QuickSearchLaunchSourceSupport(_ => { });
        var items = Enumerable.Range(1, 5)
            .Select(index => new AppSearchResult { FullPath = $"item-{index}" })
            .ToArray();
        var source = new LaunchPanelSourceViewModel("source", "Source", items);
        support.Sources.Add(source);
        support.Select(source);

        support.SelectItem(items[2]);
        Assert.IsFalse(support.MoveItemSelection(1, 0, 3));
        Assert.AreSame(items[2], support.SelectedItem);

        support.SelectItem(items[4]);
        Assert.IsTrue(support.MoveItemSelection(-1, 0, 3));
        Assert.AreSame(items[1], support.SelectedItem);
    }
}
