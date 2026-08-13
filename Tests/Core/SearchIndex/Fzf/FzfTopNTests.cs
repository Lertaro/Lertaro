using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.Tests.SearchIndex.Fzf;

[TestClass]
public sealed class FzfTopNTests
{
    // Smaller SortKey means a better-ranked candidate (see FzfResultRank.ScorePoint's inversion) --
    // FzfTopN retains the entries with the SMALLEST sort keys up to capacity.
    [TestMethod]
    public void Add_WithinCapacity_RetainsAllEntries()
    {
        var topN = new FzfTopN(3);
        topN.Add(new FzfRank(0, 10, 100));
        topN.Add(new FzfRank(1, 20, 50));

        Assert.AreEqual(2, topN.Count);
    }

    [TestMethod]
    public void Add_BeyondCapacity_EvictsTheWorstSortKey()
    {
        var topN = new FzfTopN(2);
        topN.Add(new FzfRank(0, 0, 30));
        topN.Add(new FzfRank(1, 0, 10));
        topN.Add(new FzfRank(2, 0, 20)); // should evict entry 0 (SortKey 30, the worst of the three)

        var finished = topN.Finish(10);

        Assert.HasCount(2, finished);
        CollectionAssert.DoesNotContain(finished.ConvertAll(r => r.EntryIndex), 0);
    }

    [TestMethod]
    public void Add_BetterThanCurrentWorst_ReplacesIt()
    {
        var topN = new FzfTopN(1);
        topN.Add(new FzfRank(0, 0, 100));
        topN.Add(new FzfRank(1, 0, 1)); // strictly smaller SortKey -- should replace entry 0

        var finished = topN.Finish(10);

        Assert.HasCount(1, finished);
        Assert.AreEqual(1, finished[0].EntryIndex);
    }

    [TestMethod]
    public void Add_WorseThanCurrentWorst_IsDropped()
    {
        var topN = new FzfTopN(1);
        topN.Add(new FzfRank(0, 0, 1));
        topN.Add(new FzfRank(1, 0, 100)); // larger SortKey -- should be dropped

        var finished = topN.Finish(10);

        Assert.HasCount(1, finished);
        Assert.AreEqual(0, finished[0].EntryIndex);
    }

    [TestMethod]
    public void Finish_ReturnsEntriesSortedBySortKeyAscending()
    {
        var topN = new FzfTopN(5);
        topN.Add(new FzfRank(0, 0, 300));
        topN.Add(new FzfRank(1, 0, 100));
        topN.Add(new FzfRank(2, 0, 200));

        var finished = topN.Finish(10);

        CollectionAssert.AreEqual(new[] { 1, 2, 0 }, finished.ConvertAll(r => r.EntryIndex));
    }

    [TestMethod]
    public void Finish_LimitSmallerThanCount_TruncatesToLimit()
    {
        var topN = new FzfTopN(5);
        for (var i = 0; i < 5; i++)
            topN.Add(new FzfRank(i, 0, (ulong)(50 - i)));

        var finished = topN.Finish(2);

        Assert.HasCount(2, finished);
    }

    [TestMethod]
    public void Reset_ClearsPreviouslyAddedEntries()
    {
        var topN = new FzfTopN(2);
        topN.Add(new FzfRank(0, 0, 10));
        topN.Reset();

        Assert.AreEqual(0, topN.Count);
        topN.Add(new FzfRank(1, 0, 5));
        Assert.AreEqual(1, topN.Count);
    }

    // The set buffers twice its capacity and only trims back to the best half when that fills, so
    // everything below covers a stream long enough to trim several times -- the shorter cases above all
    // finish inside the first buffer and never exercise it.
    [TestMethod]
    public void FarMoreEntriesThanCapacity_RetainsExactlyTheBest()
    {
        const int capacity = 8;
        var topN = new FzfTopN(capacity);

        // Interleaved so the good keys are scattered across the stream rather than arriving together:
        // a trim in the middle must not be able to drop one that arrived early.
        for (var i = 0; i < 1000; i++)
            topN.Add(new FzfRank(i, 0, (ulong)((i * 7919) % 1000)));

        var finished = topN.Finish(capacity);

        Assert.HasCount(capacity, finished);
        CollectionAssert.AreEqual(
            Enumerable.Range(0, capacity).Select(k => (ulong)k).ToArray(),
            finished.ConvertAll(r => r.SortKey));
    }

    [TestMethod]
    public void AnEntryArrivingAfterATrim_StillWinsIfItIsBetter()
    {
        // The trim leaves behind a threshold that later entries are compared against. A threshold left
        // too tight would silently discard the best entry of the whole stream for arriving last.
        var topN = new FzfTopN(4);
        for (var i = 0; i < 500; i++)
            topN.Add(new FzfRank(i, 0, 1000 + (ulong)i));
        topN.Add(new FzfRank(999, 0, 1));

        var finished = topN.Finish(4);

        Assert.AreEqual(999, finished[0].EntryIndex);
        Assert.AreEqual(1UL, finished[0].SortKey);
    }

    [TestMethod]
    public void Count_NeverExceedsCapacity_EvenWhileBuffered()
    {
        var topN = new FzfTopN(3);
        for (var i = 0; i < 50; i++)
            topN.Add(new FzfRank(i, 0, (ulong)i));

        Assert.AreEqual(3, topN.Count);
    }

    [TestMethod]
    public void DrainInto_AfterBuffering_MovesOnlyTheRetainedEntries()
    {
        var worker = new FzfTopN(2);
        for (var i = 0; i < 100; i++)
            worker.Add(new FzfRank(i, 0, (ulong)(100 - i)));

        var merged = new FzfTopN(2);
        worker.DrainInto(merged);
        var finished = merged.Finish(10);

        Assert.HasCount(2, finished);
        CollectionAssert.AreEqual(new ulong[] { 1, 2 }, finished.ConvertAll(r => r.SortKey));
    }

    [TestMethod]
    public void Reset_AfterTrimming_ForgetsTheThresholdToo()
    {
        // The threshold outliving a Reset would reject everything worse than the previous query's
        // results -- on a pooled instance, that is the next search silently returning nothing.
        var topN = new FzfTopN(2);
        for (var i = 0; i < 100; i++)
            topN.Add(new FzfRank(i, 0, (ulong)i));
        topN.Reset();

        topN.Add(new FzfRank(500, 0, 99_999));

        Assert.AreEqual(1, topN.Count);
        Assert.AreEqual(500, topN.Finish(10)[0].EntryIndex);
    }

    [TestMethod]
    public void EntriesSharingOneSortKey_AreStillSelectedCorrectly()
    {
        // Sort keys are computed per unique NAME, so every row of one name shares a key and duplicates
        // are the rule. Selecting the best half of a buffer where most keys are equal has to keep
        // exactly capacity of them, and keep the genuinely better ones that are mixed in.
        var topN = new FzfTopN(10);
        for (var i = 0; i < 5000; i++)
            topN.Add(new FzfRank(i, 0, 42));
        topN.Add(new FzfRank(90_001, 0, 1));
        topN.Add(new FzfRank(90_002, 0, 2));

        var finished = topN.Finish(10);

        Assert.HasCount(10, finished);
        Assert.AreEqual(1UL, finished[0].SortKey);
        Assert.AreEqual(2UL, finished[1].SortKey);
        Assert.IsTrue(finished.Skip(2).All(r => r.SortKey == 42));
    }

    [TestMethod]
    // CooperativeCancellation because the alternative is MSTest aborting the test thread, which .NET no
    // longer supports properly. Nothing here polls the token -- the loop below is one tight call into the
    // code under test -- so a regression is reported once that call returns rather than the moment the
    // deadline passes. Later, but still a failure, and the quadratic shape this guards against takes
    // minutes rather than the fraction of a second it should.
    [Timeout(20_000, CooperativeCancellation = true)]
    public void AStreamOfIdenticalSortKeys_DoesNotDegradeToQuadratic()
    {
        // A guard on cost, not on the answer. Selecting with a two-way partition sends every key equal
        // to the pivot to one side, so an all-equal buffer advances the bound by a single element per
        // pass and each trim costs the square of the buffer instead of its length -- measured at 1.6
        // million comparisons for one 3200-element trim in the real search. This shape takes a moment
        // three-way and minutes two-way, so the timeout is what actually asserts.
        var topN = new FzfTopN(2000);
        for (var i = 0; i < 400_000; i++)
            topN.Add(new FzfRank(i, 0, 7));

        Assert.AreEqual(2000, topN.Count);
    }

    [TestMethod]
    public void DrainInto_MergesEntriesRespectingTargetCapacity()
    {
        var worker = new FzfTopN(2);
        worker.Add(new FzfRank(0, 0, 5));
        worker.Add(new FzfRank(1, 0, 1));

        var merged = new FzfTopN(2);
        merged.Add(new FzfRank(2, 0, 3));

        worker.DrainInto(merged);
        var finished = merged.Finish(10);

        // merged started with {2:3}, worker drains in {0:5} and {1:1} -- best 2 of {3,5,1} are {1,3}.
        CollectionAssert.AreEqual(new[] { 1, 2 }, finished.ConvertAll(r => r.EntryIndex));
    }

    [TestMethod]
    public void LargeCapacity_GrowsToHoldActualMatchesWithoutTrimmingThem()
    {
        var topN = new FzfTopN(1_000_000);
        for (var i = 0; i < 10_000; i++)
            topN.Add(new FzfRank(i, 0, (ulong)(10_000 - i)));

        var finished = topN.Finish(int.MaxValue);

        Assert.HasCount(10_000, finished);
        Assert.AreEqual(1UL, finished[0].SortKey);
        Assert.AreEqual(10_000UL, finished[^1].SortKey);
    }
}
