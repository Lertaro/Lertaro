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
    public void MoveItemSelection_WrapsInBothDirections()
    {
        var support = new QuickSearchLaunchSourceSupport(_ => { });
        var first = new AppSearchResult { FullPath = "first" };
        var second = new AppSearchResult { FullPath = "second" };
        var source = new LaunchPanelSourceViewModel("source", "Source", new[] { first, second });
        support.Sources.Add(source);
        support.Select(source);

        Assert.IsTrue(support.MoveItemSelection(-1));
        Assert.AreSame(second, support.SelectedItem);
        Assert.IsTrue(support.MoveItemSelection(1));
        Assert.AreSame(first, support.SelectedItem);
    }
}
