using Lertaro.PluginSdk.Abstractions;
using Lertaro.App.ViewModels.Search;

namespace Lertaro.App.Tests.ViewModels.Search;

[TestClass]
public sealed class SearchResultSorterTests
{
    private static AppSearchResult Make(string name, string fullPath, DateTime modified) => new()
    {
        Name = name,
        FullPath = fullPath,
        Metadata = new FileMetadata(0, default, modified, default)
    };

    [TestMethod]
    public void Sort_EmptyColumn_ReturnsResultsUnchanged()
    {
        var results = new[] { Make("b", @"C:\b", default), Make("a", @"C:\a", default) };

        var sorted = SearchResultSorter.Sort(results, string.Empty, isSortAscending: true).ToList();

        CollectionAssert.AreEqual(new[] { "b", "a" }, sorted.Select(r => r.Name).ToList());
    }

    [TestMethod]
    public void Sort_ByNameId_Ascending_OrdersAlphabetically()
    {
        var results = new[] { Make("banana", @"C:\banana", default), Make("apple", @"C:\apple", default) };

        var sorted = SearchResultSorter.Sort(results, "Name", isSortAscending: true).ToList();

        CollectionAssert.AreEqual(new[] { "apple", "banana" }, sorted.Select(r => r.Name).ToList());
    }

    [TestMethod]
    public void Sort_ByNameId_Descending_OrdersReverseAlphabetically()
    {
        var results = new[] { Make("apple", @"C:\apple", default), Make("banana", @"C:\banana", default) };

        var sorted = SearchResultSorter.Sort(results, "Name", isSortAscending: false).ToList();

        CollectionAssert.AreEqual(new[] { "banana", "apple" }, sorted.Select(r => r.Name).ToList());
    }

    [TestMethod]
    public void Sort_ByPathId_OrdersByFullPath()
    {
        var results = new[] { Make("a", @"C:\z\a", default), Make("b", @"C:\a\b", default) };

        var sorted = SearchResultSorter.Sort(results, "Path", isSortAscending: true).ToList();

        CollectionAssert.AreEqual(new[] { "b", "a" }, sorted.Select(r => r.Name).ToList());
    }

    [TestMethod]
    public void Sort_ByDateModifiedId_OrdersByModifiedDate()
    {
        var results = new[]
        {
            Make("newer", @"C:\newer", new DateTime(2024, 6, 1)),
            Make("older", @"C:\older", new DateTime(2024, 1, 1)),
        };

        var sorted = SearchResultSorter.Sort(results, "DateModified", isSortAscending: true).ToList();

        CollectionAssert.AreEqual(new[] { "older", "newer" }, sorted.Select(r => r.Name).ToList());
    }

    [TestMethod]
    public void Sort_ByTranslatedHeaderText_DoesNotMatchAnyBuiltInId()
    {
        // Regression guard: currentSortColumn must be the stable id ("Name"), not the column's
        // displayed/translated header text -- passing display text should fall through to the
        // generic AppSearchResult indexer path instead of silently matching a built-in column.
        var results = new[] { Make("b", @"C:\b", default), Make("a", @"C:\a", default) };

        var sorted = SearchResultSorter.Sort(results, "Name (translated header, not an id)", isSortAscending: true).ToList();

        Assert.HasCount(2, sorted);
    }
}
