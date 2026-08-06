namespace Lertaro.Core.Tests;

[TestClass]
public sealed class SearchResultRankComparerTests
{
    [TestMethod]
    public void Compare_HigherHistoryPriority_SortsBeforeUnranked()
    {
        var history = new Dictionary<string, int> { [@"c:\a.txt"] = 0 };
        var comparer = new SearchResultRankComparer(history);
        var ranked = new SearchResult { Path = @"c:\a.txt" };
        var unranked = new SearchResult { Path = @"c:\b.txt" };

        Assert.IsLessThan(0, comparer.Compare(ranked, unranked));
    }

    [TestMethod]
    public void Compare_LowerHistoryPriorityNumber_SortsFirst()
    {
        var history = new Dictionary<string, int> { [@"c:\a.txt"] = 0, [@"c:\b.txt"] = 5 };
        var comparer = new SearchResultRankComparer(history);
        var first = new SearchResult { Path = @"c:\a.txt" };
        var second = new SearchResult { Path = @"c:\b.txt" };

        Assert.IsLessThan(0, comparer.Compare(first, second));
    }

    [TestMethod]
    public void Compare_TrailingBackslash_IsNormalizedForHistoryLookup()
    {
        // A directory path with a trailing separator must still match its history entry recorded
        // without one (NormalizeForLookup strips it, but only for paths longer than "C:\").
        var history = new Dictionary<string, int> { [@"c:\folder"] = 0 };
        var comparer = new SearchResultRankComparer(history);
        var withSlash = new SearchResult { Path = @"c:\folder\" };
        var unranked = new SearchResult { Path = @"c:\other" };

        Assert.IsLessThan(0, comparer.Compare(withSlash, unranked));
    }

    [TestMethod]
    public void Compare_NoHistoryMatch_FallsBackToShorterPathFirst()
    {
        var comparer = new SearchResultRankComparer(new Dictionary<string, int>());
        var shorter = new SearchResult { Path = @"c:\a.txt" };
        var longer = new SearchResult { Path = @"c:\a\very\long\path.txt" };

        Assert.IsLessThan(0, comparer.Compare(shorter, longer));
    }

    [TestMethod]
    public void Compare_SamePathLength_FallsBackToDriveThenPathOrdinalIgnoreCase()
    {
        var comparer = new SearchResultRankComparer(new Dictionary<string, int>());
        var onC = new SearchResult { Path = @"c:\a.txt", Drive = "C" };
        var onD = new SearchResult { Path = @"d:\a.txt", Drive = "D" };

        Assert.IsLessThan(0, comparer.Compare(onC, onD));
    }

    [TestMethod]
    public void Compare_NullHandling_NullSortsAfterNonNull()
    {
        var comparer = new SearchResultRankComparer(new Dictionary<string, int>());
        var result = new SearchResult { Path = @"c:\a.txt" };

        Assert.IsLessThan(0, comparer.Compare(result, null));
        Assert.IsGreaterThan(0, comparer.Compare(null, result));
        Assert.AreEqual(0, comparer.Compare(null, null));
    }

    [TestMethod]
    public void Compare_SameReference_ReturnsZero()
    {
        var comparer = new SearchResultRankComparer(new Dictionary<string, int>());
        var result = new SearchResult { Path = @"c:\a.txt" };

        Assert.AreEqual(0, comparer.Compare(result, result));
    }
}
