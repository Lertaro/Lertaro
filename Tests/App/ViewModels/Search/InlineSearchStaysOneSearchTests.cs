using System.IO;

namespace Lertaro.App.Tests.ViewModels.Search;

// The inline window must stay "the ordinary global search, with the current folder's rows ordered first".
//
// It used to additionally run a directory-scoped engine search of its own for the folder's subtree. That
// cost the same as the global search -- the engine scans a drive's whole unique-name table before applying
// any directory filter, so scoping buys nothing -- and it had to be waited on, which is what made the
// inline window lag the quick window. Re-adding one would silently reintroduce that lag, and no
// behavioural test would notice (the rows still come out right, just later).
[TestClass]
public sealed class InlineSearchStaysOneSearchTests
{
    [TestMethod]
    public void TheLocalPhaseListsTheFolder_ButRunsNoSecondEngineSearch()
    {
        var helper = Source("App/ViewModels/Search/ExplorerSearchHelper.cs");

        // Still lists the folder's own files (that guarantee is what the current-folder section is for).
        Assert.Contains("DirectChildrenLocator.MatchInto", helper,
            "the folder's own files must still be listed directly");

        // But no engine query of its own.
        Assert.DoesNotContain("SearchStreamingAsync", helper,
            "the inline local phase must not run a second engine search; the global results already cover "
            + "the folder's subtree and are re-ordered by InlineListSearchHelper instead");
    }

    [TestMethod]
    public void TheEngineDoesNotGateTheGlobalSearchBehindTheLocalListing()
    {
        var engine = Source("App/ViewModels/Search/SearchExecutionEngine.cs");

        // The global search IS the list now; holding it behind the local listing is the lag being fixed.
        var renderCall = Between(engine, "RenderInlineSearchAsync", "private void EmitInstantResults");
        Assert.DoesNotContain("Gate", renderCall,
            "the inline path must not put a gate in front of the global search");
    }

    [TestMethod]
    public void TheDeletedInlineOnlyMechanismsStayDeleted()
    {
        Assert.IsFalse(File.Exists(Path.Combine(RepoRoot(), "App", "ViewModels", "Search", "InlineGlobalSearchGate.cs")),
            "the local head-start gate should be gone");
        Assert.IsFalse(File.Exists(Path.Combine(RepoRoot(), "App", "ViewModels", "Search", "InlineSmallResultRenderDelay.cs")),
            "the small-result settle delay should be gone");
    }

    [TestMethod]
    public void TheInlineCardHeightDoesNotFollowTheResultCount()
    {
        // The card holds its result budget while searching and trims to a settled result set once it is not.
        // What must not come back is a height summed over the result ROWS mid-search: that is what made it
        // jumpy, and it would show up only as a feel regression rather than a wrong value.
        var layout = Source("App/Views/InlineSearchWindow/Helpers/InlineSearchWindowLayoutManager.cs");

        Assert.DoesNotContain("resultsHeight +=", layout,
            "the results area must not be summed over the result rows");
        Assert.Contains("CurrentLayout()", layout,
            "the layout must size itself from the state-derived layout");

        var metrics = Source("App/Views/InlineSearchWindow/Helpers/InlineCardMetrics.cs");
        Assert.Contains("ComputeLayout", metrics,
            "the row split must be a single testable calculation");
        Assert.Contains("DefaultRows = 9", metrics,
            "the result budget is the Ctrl+1..9 range");
    }

    [TestMethod]
    public void TheInlineCardHasNoUserResizeHandle()
    {
        // Resizing was dropped: the list already scrolls, so a drag handle only added UI surface and a
        // persisted setting to maintain.
        var xaml = Source("App/Views/InlineSearchWindow/InlineSearchWindow.xaml");
        Assert.DoesNotContain("ResizeGrip", xaml, "the resize grip should be gone");
        Assert.IsFalse(File.Exists(Path.Combine(RepoRoot(), "App", "Views", "InlineSearchWindow", "Helpers", "InlineSearchResizeSupport.cs")),
            "the resize support should be gone");

        // And no leftover setting for it.
        var settings = Source("Core/Settings/UserSettingsModels.cs");
        Assert.DoesNotContain("InlineSearchSettings", settings,
            "the inline row-count setting should be gone with the drag it existed for");
    }

    [TestMethod]
    public void ThePathBannerIsAddedToTheCardHeightRatherThanTakenFromTheList()
    {
        // The banner sits above a star-sized results area, so an Auto row for it would steal height from
        // the list -- which showed up as the list losing half a row and the search box shifting down when
        // a row outside the current folder was selected. It is counted into the card's height instead, so
        // the card grows upward and both stay put.
        var sizing = Source("App/Views/InlineSearchWindow/Helpers/InlineCardSizingSupport.cs");
        var heightMethod = Between(sizing, "internal double CardHeight(int rows)", "private double PathBannerHeight");

        Assert.Contains("PathBannerHeight()", heightMethod,
            "the card's height must include the banner when it is shown");

        // Every visibility change has to go through the one place that re-sizes the card, or a banner
        // shown by another path would again resize nothing.
        var layout = Source("App/Views/InlineSearchWindow/Helpers/InlineSearchWindowLayoutManager.cs");
        Assert.Contains("SetPathBannerVisible", layout, "the banner needs a single visibility entry point");
        Assert.DoesNotContain("PathPreviewBorder.Visibility =", layout,
            "no call site may change the banner's visibility directly");
    }

    [TestMethod]
    public void NothingResizesTheCardSynchronouslyFromInsideACollectionChange()
    {
        // This crashed the app: a results-collection handler called ApplyCardHeight, which sets the window
        // Height and then runs PositionWindowImmediate -> UpdateLayout synchronously. Forcing a layout pass
        // from inside a CollectionChanged notification makes the ListBox's container generator throw
        // "ItemsControl inconsistent with its items source" (the generator sees 5 items where the collection
        // has 6), and the app dies. See the crash log in AppCrashHandler.
        //
        // So: a collection-change handler may only QUEUE layout work, never apply it inline. It may
        // subscribe -- re-sizing on a content change is required (typing "*" adds a prompt row without any
        // IsSearching transition, and an un-recomputed height clipped the search box) -- but only through
        // the deferred RequestCardHeight.
        var sizing = Source("App/Views/InlineSearchWindow/Helpers/InlineCardSizingSupport.cs");

        Assert.Contains("RequestCardHeight", sizing,
            "size changes must go through the deferred entry point");

        // The collection subscription line itself must name the deferred entry point and never the
        // synchronous one. (Matched against the line rather than a range: the deferred method's own body
        // legitimately calls ApplyCardHeight, so a range would catch that too.)
        var subscription = System.Text.RegularExpressions.Regex.Match(
            sizing, @"Results\.CollectionChanged \+=[^;]*;");
        Assert.IsTrue(subscription.Success, "expected the results-collection subscription");
        Assert.Contains("RequestCardHeight", subscription.Value,
            "a collection-change handler must queue the size change, never apply it inline");
        Assert.DoesNotContain("ApplyCardHeight", subscription.Value,
            "calling the synchronous one from a notification is what crashed the app");

        // The window's own collection handler must stay queued.
        var window = Source("App/Views/InlineSearchWindow/InlineSearchWindow.xaml.cs");
        var handler = Between(window, "Results.CollectionChanged += (s, e)", "InlineSearchWindowResultsWiring");
        Assert.Contains("QueueResultsLayoutUpdate", handler, "the collection handler must queue, not apply");
        Assert.DoesNotContain("ApplyCardHeight", handler, "and never apply the card size inline");
    }

    [TestMethod]
    public void ReSizingTheCardIsSkippedWhenTheHeightWouldNotChange()
    {
        // The results collection is replaced on every streaming paint, and the card's height depends on how
        // many rows fit rather than on which rows they are -- so for most keystrokes the computed height is
        // unchanged. Without an early return, each of those still ran the whole resize path, including a
        // synchronous UpdateLayout and a native window move, which is what made the inline window feel
        // heavier than the quick window while typing.
        var sizing = Source("App/Views/InlineSearchWindow/Helpers/InlineCardSizingSupport.cs");
        var apply = Between(sizing, "internal void ApplyCardHeight()", "internal double CardHeight(int rows)");

        var earlyReturn = apply.IndexOf("return;", StringComparison.Ordinal);
        var firstResize = apply.IndexOf("_window.Height = shellHeight;", StringComparison.Ordinal);

        Assert.IsGreaterThan(-1, earlyReturn, "ApplyCardHeight must be able to skip the work entirely");
        Assert.IsGreaterThan(-1, firstResize, "and must still actually resize");
        Assert.IsLessThan(firstResize, earlyReturn,
            "the skip must happen BEFORE anything is resized or repositioned, or it saves nothing");
        Assert.Contains("Math.Abs(_window.Height - shellHeight)", apply,
            "the skip must be decided by comparing against the current height");
    }

    [TestMethod]
    public void TheFolderSnapshotIsNotRebuiltOnEveryPaint()
    {
        // The renderer asks for the folder snapshot on EVERY paint, and one keystroke can produce several as
        // results stream in. Building it is not free -- CreateLocalSnapshot copies the whole
        // history-priority dictionary and re-ranks every local match -- and nothing about the answer changes
        // until the local matches or the query do. Rebuilding per paint was repeated work that grew with the
        // user's history size.
        var engine = Source("App/ViewModels/Search/SearchExecutionEngine.cs");
        var snapshotMethod = Between(engine, "List<AppSearchResult> GetLocalSnapshot()", "await _streamRenderer.RenderAsync");

        Assert.Contains("cachedLocalSnapshot", snapshotMethod,
            "the snapshot must be memoized rather than rebuilt per request");
        Assert.Contains("localUpdateVersion", snapshotMethod,
            "and invalidated by the version that tracks the local match set");
    }

    private static string Between(string source, string from, string to)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, start, $"could not find '{from}'");
        var end = source.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, end, $"could not find '{to}' after '{from}'");
        return source.Substring(start, end - start);
    }

    private static string Source(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "could not locate the repository root");
        return dir!.FullName;
    }
}
