using Lertaro.App.ViewModels.QuickPanel;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.QuickPanel;

[TestClass]
public sealed class QuickPanelGroupViewModelTests
{
    [TestMethod]
    public void LargeGroup_MaterializesOnlyTheFirstPageUntilRequested()
    {
        var group = Group(300);

        Assert.AreEqual(300, group.Count);
        Assert.HasCount(32, group.Items);

        var loadedPages = 0;
        while (group.LoadNextPage())
            loadedPages++;

        Assert.AreEqual(9, loadedPages);
        Assert.HasCount(300, group.Items);
        Assert.IsFalse(group.LoadNextPage());
    }

    [TestMethod]
    public void ResetMaterialization_PreviouslyScrolledGroup_ReturnsToFirstPage()
    {
        var group = Group(300);
        group.LoadNextPage();
        group.LoadNextPage();
        Assert.HasCount(96, group.Items);

        group.ResetMaterialization();

        Assert.AreEqual(300, group.Count);
        Assert.HasCount(32, group.Items);
    }

    [TestMethod]
    public void ConfiguredMaximum_StillCapsLoadingAndMaterialization()
    {
        var group = new QuickPanelGroupViewModel("source", "Source", @"C:\source", Pairs(Enumerable.Range(0, 300).Select(index => index.ToString()).ToArray()),
            QuickPanelSortMode.NameAscending, maxItems: 20);

        Assert.AreEqual(20, group.Count);
        Assert.HasCount(20, group.Items);
        Assert.IsFalse(group.LoadNextPage());
    }

    [TestMethod]
    public void LoadingGroup_KeepsArrivalOrderUntilTheCompletedSortArrives()
    {
        var group = new QuickPanelGroupViewModel("source", "Source", @"C:\source", Pairs("z", "a"),
            QuickPanelSortMode.NameAscending, isLoading: true);

        CollectionAssert.AreEqual(new[] { "z", "a" }, group.Items.Select(item => item.Name).ToList());

        group.Replace(Pairs("z", "a"));

        CollectionAssert.AreEqual(new[] { "a", "z" }, group.Items.Select(item => item.Name).ToList());
    }

    private static QuickPanelGroupViewModel Group(int count) => new("source", "Source", @"C:\source",
        Pairs(Enumerable.Range(0, count).Select(index => index.ToString("D4")).ToArray()), QuickPanelSortMode.NameAscending);

    private static List<(AppSearchResult Item, DateTime? Modified)> Pairs(params string[] names) => names
        .Select(name => (Item: new AppSearchResult { Name = name, FullPath = @"C:\source\" + name }, Modified: (DateTime?)null))
        .ToList();
}
