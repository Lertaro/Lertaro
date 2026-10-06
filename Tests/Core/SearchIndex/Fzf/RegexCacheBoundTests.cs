using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.Tests.SearchIndex.Fzf;

// A compiled Regex is expensive enough to hold that the clause cache must not accumulate forever. The
// key is the raw pattern text, and every intermediate state of an edit is a distinct pattern, so a long
// editing session is unbounded input even though any single query holds only a handful of clauses.
[TestClass]
[DoNotParallelize]
public sealed class RegexCacheBoundTests
{
    [TestMethod]
    public void AllMatch_QueryExceedsSharedCacheCapacity_DoesNotRepopulateCacheForEachCandidate()
    {
        var patterns = Enumerable.Range(0, 257).Select(i => $"a{{0,{i}}}").ToArray();
        Assert.IsTrue(RegexClauses.AllMatch(patterns, "a"));
        var cachedCount = RegexClauses.CachedCount;

        Assert.IsTrue(RegexClauses.AllMatch(patterns, "aa"));
        Assert.AreEqual(cachedCount, RegexClauses.CachedCount);
        Assert.IsLessThanOrEqualTo(256, cachedCount);
    }

    [TestMethod]
    public void AllMatch_ManyDistinctPatterns_KeepsTheCacheBounded()
    {
        for (var i = 0; i < 600; i++)
        {
            Assert.IsTrue(RegexClauses.AllMatch([$"^name{i}\\.txt$"], $"name{i}.txt"));
        }

        Assert.IsLessThanOrEqualTo(256, RegexClauses.CachedCount);
    }

    [TestMethod]
    public void AllMatch_PatternStillMatchesAfterTheCacheHasBeenCleared()
    {
        // Clearing is a capacity decision, not a behavioural one: a pattern compiled before the clear
        // must still answer identically afterwards.
        Assert.IsTrue(RegexClauses.AllMatch([@"^stable\.txt$"], "stable.txt"));

        for (var i = 0; i < 600; i++)
        {
            _ = RegexClauses.AllMatch([$"^filler{i}$"], "filler");
        }

        Assert.IsTrue(RegexClauses.AllMatch([@"^stable\.txt$"], "stable.txt"));
        Assert.IsFalse(RegexClauses.AllMatch([@"^stable\.txt$"], "other.txt"));
    }
}
