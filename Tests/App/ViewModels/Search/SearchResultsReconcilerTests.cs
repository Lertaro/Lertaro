using Lertaro.App.Helpers;
using Lertaro.App.ViewModels.Search;

namespace Lertaro.App.Tests.ViewModels.Search;

[TestClass]
public sealed class SearchResultsReconcilerTests
{
    private static AppSearchResult Result(string path, string name = "n", string kind = "File", string query = "") =>
        new() { FullPath = path, Name = name, ResultKind = kind, SearchQuery = query };

    [TestMethod]
    public void Replace_UpdatesCollectionToNewResults()
    {
        var results = new ObservableRangeCollection<AppSearchResult> { Result(@"C:\a") };
        AppSearchResult? selected = null;

        SearchResultsReconciler.Replace(results, new[] { Result(@"C:\b") }, null, s => selected = s);

        Assert.HasCount(1, results);
        Assert.AreEqual(@"C:\b", results[0].FullPath);
    }

    [TestMethod]
    public void Replace_CurrentSelectionStillPresentAndSelectable_KeepsSelectionUnchanged()
    {
        var current = Result(@"C:\a");
        var results = new ObservableRangeCollection<AppSearchResult> { current };
        var setSelectionCalled = false;

        // Passing an item that's ItemsEqual to `current` (same FullPath/Name/ResultKind/SearchQuery) so
        // ReconcileTo treats it as unchanged and `results.Contains(current)` still finds the original.
        SearchResultsReconciler.Replace(results, new[] { Result(@"C:\a") }, current, _ => setSelectionCalled = true);

        Assert.IsFalse(setSelectionCalled);
    }

    [TestMethod]
    public void Replace_CurrentSelectionGone_SelectsFirstSelectableResult()
    {
        var current = Result(@"C:\gone");
        var results = new ObservableRangeCollection<AppSearchResult> { current };
        AppSearchResult? selected = null;

        SearchResultsReconciler.Replace(results, new[] { Result(@"C:\new") }, current, s => selected = s);

        Assert.AreEqual(@"C:\new", selected?.FullPath);
    }

    [TestMethod]
    public void Replace_CurrentSelectionNowEmptyResult_SelectsFirstRealResultInstead()
    {
        var results = new ObservableRangeCollection<AppSearchResult>();
        AppSearchResult? selected = null;
        var header = Result("__SECTION_HEADER__", kind: "SectionHeader");
        var real = Result(@"C:\real");

        SearchResultsReconciler.Replace(results, new[] { header, real }, null, s => selected = s);

        Assert.AreSame(real, selected);
    }

    [TestMethod]
    public void Replace_NoSelectableResults_SelectsNull()
    {
        var results = new ObservableRangeCollection<AppSearchResult>();
        var selected = Result(@"C:\placeholder");

        SearchResultsReconciler.Replace(results, new[] { Result("__NO_RESULTS__", kind: "Empty") }, null, s => selected = s);

        Assert.IsNull(selected);
    }

    [TestMethod]
    public void Replace_NoCurrentSelection_SelectsFirstSelectableResult()
    {
        AppSearchResult? selected = null;
        var results = new ObservableRangeCollection<AppSearchResult>();

        SearchResultsReconciler.Replace(results, new[] { Result(@"C:\a"), Result(@"C:\b") }, null, s => selected = s);

        Assert.AreEqual(@"C:\a", selected?.FullPath);
    }
}
