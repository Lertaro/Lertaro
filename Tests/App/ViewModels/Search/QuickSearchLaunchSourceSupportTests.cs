using Lertaro.App.ViewModels.Search;

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
}
