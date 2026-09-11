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

    // The point of excluding SearchQuery from row identity: typing another character changes every row's
    // query, so including it replaced every row instance -- and every realized row re-bound its icon,
    // rebuilt its highlight Runs and restarted its marquee, on the UI thread, per keystroke.

    [TestMethod]
    public void Replace_SameRowUnderANewQuery_KeepsTheExistingInstance()
    {
        var before = Result(@"C:\a", query: "a");
        var results = new ObservableRangeCollection<AppSearchResult> { before };

        SearchResultsReconciler.Replace(results, new[] { Result(@"C:\a", query: "ab") }, null, _ => { });

        Assert.AreSame(before, results[0], "a row that did not change should not be replaced");
    }

    [TestMethod]
    public void Replace_SameRowUnderANewQuery_MovesTheNewQueryOntoTheKeptRow()
    {
        var before = Result(@"C:\a", query: "a");
        var results = new ObservableRangeCollection<AppSearchResult> { before };

        SearchResultsReconciler.Replace(results, new[] { Result(@"C:\a", query: "ab") }, null, _ => { });

        Assert.AreEqual("ab", before.SearchQuery, "the kept row must carry the new query or its highlight freezes");
    }

    [TestMethod]
    public void Replace_SameRowUnderANewQuery_NotifiesSoHighlightingRepaints()
    {
        var before = Result(@"C:\a", query: "a");
        var results = new ObservableRangeCollection<AppSearchResult> { before };
        var raised = new List<string?>();
        before.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        SearchResultsReconciler.Replace(results, new[] { Result(@"C:\a", query: "ab") }, null, _ => { });

        // Bound (TextHighlighter.HighlightText), so a silent field write would leave the old highlight up.
        Assert.Contains(nameof(AppSearchResult.SearchQuery), raised);
    }

    [TestMethod]
    public void Replace_SameRowUnchangedQuery_DoesNotNotify()
    {
        var before = Result(@"C:\a", query: "a");
        var results = new ObservableRangeCollection<AppSearchResult> { before };
        var raised = 0;
        before.PropertyChanged += (_, _) => raised++;

        SearchResultsReconciler.Replace(results, new[] { Result(@"C:\a", query: "a") }, null, _ => { });

        Assert.AreEqual(0, raised, "an unchanged query must not cost a repaint");
    }

    [TestMethod]
    public void Replace_DifferentPath_StillReplacesTheRow()
    {
        // The other direction: retention must be limited to genuinely the same row.
        var before = Result(@"C:\a");
        var results = new ObservableRangeCollection<AppSearchResult> { before };

        SearchResultsReconciler.Replace(results, new[] { Result(@"C:\b") }, null, _ => { });

        Assert.AreNotSame(before, results[0]);
        Assert.AreEqual(@"C:\b", results[0].FullPath);
    }

    [TestMethod]
    public void Replace_KeepsPositionallyStableRowsAndReplacesReorderedOnes()
    {
        // Reconciliation is POSITIONAL: only live[i] == target[i] is retained. A row that moved is a
        // different row at that slot and is replaced, which is the existing, intended behaviour (a
        // re-sort invalidates position). What changed is only that an unmoved row now survives a query
        // change instead of being replaced along with everything else.
        var a = Result(@"C:\a", query: "x");
        var b = Result(@"C:\b", query: "x");
        var results = new ObservableRangeCollection<AppSearchResult> { a, b };

        SearchResultsReconciler.Replace(results, new[] { Result(@"C:\a", query: "xy"), Result(@"C:\b", query: "xy") }, null, _ => { });

        Assert.AreSame(a, results[0], "an unmoved row should survive");
        Assert.AreSame(b, results[1], "an unmoved row should survive");
        Assert.AreEqual("xy", a.SearchQuery);
    }

    [TestMethod]
    public void Replace_ReorderedRows_AreReplacedNotCarriedOver()
    {
        // The other side of the positional rule, so the retention above cannot silently turn into
        // identity-based set matching.
        var a = Result(@"C:\a");
        var b = Result(@"C:\b");
        var results = new ObservableRangeCollection<AppSearchResult> { a, b };

        SearchResultsReconciler.Replace(results, new[] { Result(@"C:\b"), Result(@"C:\a") }, null, _ => { });

        Assert.AreEqual(@"C:\b", results[0].FullPath);
        Assert.AreEqual(@"C:\a", results[1].FullPath);
        Assert.AreNotSame(a, results[0], "a row that moved must not be reported as the row that was there");
    }

    [TestMethod]
    public void SyncMutableDisplayState_SkipsReplacedInstances()
    {
        // A replaced row already carries the new values; copying onto it would be wasted work.
        var live = new List<AppSearchResult> { Result(@"C:\a", query: "old") };
        var target = new List<AppSearchResult> { Result(@"C:\a", query: "new") };

        SearchResultsReconciler.SyncMutableDisplayState(live, target);

        Assert.AreEqual("new", live[0].SearchQuery);
    }
}
