using Lertaro.App.ViewModels.Search;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.Search;

[TestClass]
public sealed class QuickSearchLaunchSourceSupportTests
{
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
        Assert.IsTrue(support.MoveItemSelection(-1, 0, 3));
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
