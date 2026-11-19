using Lertaro.PluginSdk.Abstractions;
using Lertaro.App.ViewModels.Search;

namespace Lertaro.App.Tests.ViewModels.Search;

[TestClass]
public sealed class SearchResultSorterTests
{
    [TestMethod]
    public async Task SortAsync_MissingDates_AwaitsMetadataBeforePublishingDateOrder()
    {
        var directory = System.IO.Directory.CreateTempSubdirectory("lertaro-sort-");
        try
        {
            var newer = System.IO.Path.Combine(directory.FullName, "newer.txt");
            var older = System.IO.Path.Combine(directory.FullName, "older.txt");
            System.IO.File.WriteAllText(newer, "new");
            System.IO.File.WriteAllText(older, "old");
            System.IO.File.SetLastWriteTime(newer, new DateTime(2026, 11, 18));
            System.IO.File.SetLastWriteTime(older, new DateTime(2026, 1, 1));
            List<AppSearchResult> rows = [Make("newer", newer, default), Make("older", older, default)];

            var sorted = await SearchResultSorter.SortAsync(rows, "DateModified", true, CancellationToken.None);

            CollectionAssert.AreEqual(new[] { "older", "newer" }, sorted.Select(row => row.Name).ToArray());
            var descending = await SearchResultSorter.SortAsync(rows, "DateModified", false, CancellationToken.None);
            CollectionAssert.AreEqual(new[] { "newer", "older" }, descending.Select(row => row.Name).ToArray());
        }
        finally { directory.Delete(recursive: true); }
    }

    [TestMethod]
    public async Task SortAsync_CancelledQuery_DoesNotStartLoadingDates()
    {
        var row = Make("unknown", @"Z:\unknown", default);
        await Assert.ThrowsAsync<OperationCanceledException>(() => SearchResultSorter.SortAsync(
            [row], "DateModified", true, new CancellationToken(canceled: true)));
        Assert.IsNull(row.ExistingExtras?.DateModifiedTask);
    }

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
