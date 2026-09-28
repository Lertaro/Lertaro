using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.Search.Mapping;

/// <summary>
/// A search asks its instant providers and action plugins once. The quick and inline windows re-run
/// <see cref="SearchResultMapper.BuildQuickResults"/> on every paint of a streaming search, so without the
/// cache handed in by the dispatch, every one of those paints would wait on a fresh pass over every plugin
/// before the index matches could reach the screen.
/// </summary>
[TestClass]
public sealed class InstantPassCacheTests
{
    private static AppSearchResult ProvidedRow(string name) =>
        new() { Name = name, FullPath = $"__INSTANT_RESULT__:{name}", ResultKind = "InstantResult" };

    private static List<SearchResult> TwoFiles() =>
    [
        new SearchResult { Path = @"C:\Notes\alpha.txt", Name = "alpha.txt" },
        new SearchResult { Path = @"C:\Notes\beta.txt", Name = "beta.txt" },
    ];

    [TestMethod]
    public void CachedPass_LandsAheadOfTheRankedFileRowsAndIsNumberedWithThem()
    {
        var cached = new SearchResultMapper.InstantPassCache { Collected = true };
        cached.Rows.Add(ProvidedRow("Calculator"));

        var result = SearchResultMapper.BuildQuickResults(
            TwoFiles(), "alpha", scope: null, contextDirectory: null, isInlineWindow: false,
            instantPass: cached);

        Assert.AreEqual("Calculator", result[0].Name, "a plugin row keeps its place at the head of the list");
        Assert.AreEqual(0, result[0].Index);
        CollectionAssert.AreEqual(
            new[] { @"C:\Notes\alpha.txt", @"C:\Notes\beta.txt" },
            result.Skip(1).Select(row => row.FullPath).ToList());
        CollectionAssert.AreEqual(
            new[] { 0, 1, 2 }, result.Select(row => row.Index).ToList(), "positions are restamped for this render");
    }

    [TestMethod]
    public void OneCacheAcrossTwoPaints_SharesTheRowInstancesWithoutLosingPositions()
    {
        // The streaming case: paint 1 saw two files, paint 2 has more arrived. The cached row is one object
        // handed to both renders, which is only sound if each render still numbers it where it actually put
        // it -- the list control reconciles on row identity.
        var cached = new SearchResultMapper.InstantPassCache { Collected = true };
        cached.Rows.Add(ProvidedRow("Web result"));

        var firstPaint = SearchResultMapper.BuildQuickResults(
            TwoFiles(), "alpha", scope: null, contextDirectory: null, isInlineWindow: false, instantPass: cached);
        var secondPaint = SearchResultMapper.BuildQuickResults(
            [.. TwoFiles(), new SearchResult { Path = @"C:\Notes\gamma.txt", Name = "gamma.txt" }],
            "alpha", scope: null, contextDirectory: null, isInlineWindow: false, instantPass: cached);

        Assert.AreSame(firstPaint[0], secondPaint[0], "the cached row is reused, not rebuilt");
        Assert.AreEqual(0, secondPaint[0].Index);
        Assert.HasCount(4, secondPaint);
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, secondPaint.Select(row => row.Index).ToList());
    }
}
