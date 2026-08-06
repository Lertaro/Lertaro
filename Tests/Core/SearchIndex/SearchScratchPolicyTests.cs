using System.Runtime.CompilerServices;
using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.Tests.SearchIndex;

// The search path pools its working buffers because reallocating them per keystroke was measurable, but
// none of them ever gave anything back: Clear resets the count and keeps the capacity, so each buffer
// grew to fit the biggest search it had ever seen and stayed there. Reachable from static pools and
// thread statics, so not garbage and not something a collection can help with -- which is why asking for
// one after a large search reclaimed the results and left this behind.
[TestClass]
public sealed class SearchScratchPolicyTests
{
    // Stands in for the matcher's UniqueMatch, the largest of the pooled element types.
    private readonly record struct Wide(int A, long B, long C, long D);

    // ...and for FzfRank, the smallest.
    private readonly record struct Narrow(int A, int B, long C);

    [TestMethod]
    public void TheBudgetIsMeasuredInBytes_NotEntries()
    {
        // The same count means very different amounts for different element types, which is how the
        // first version of this went wrong: a count that sounded modest was 2MB for the wider one.
        Assert.AreEqual(16, Unsafe.SizeOf<Narrow>());
        Assert.AreEqual(32, Unsafe.SizeOf<Wide>());

        var entries = SearchScratchPolicy.MaxRetainedBytes / 32;
        Assert.IsTrue(SearchScratchPolicy.WorthRetaining<Wide>(entries));
        Assert.IsFalse(SearchScratchPolicy.WorthRetaining<Wide>(entries + 1));
        // Twice as many of the narrow one fit in the same budget.
        Assert.IsTrue(SearchScratchPolicy.WorthRetaining<Narrow>(entries * 2));
        Assert.IsFalse(SearchScratchPolicy.WorthRetaining<Narrow>(entries * 2 + 1));
    }

    [TestMethod]
    public void TheBudgetIsSmallEnoughToSurviveBeingPaidPerCore()
    {
        // Every one of these lives in a thread static or a per-worker pool, so the budget is paid once
        // per core. A broad query used to leave 18MB parked across seventeen workers, every one of them
        // sitting at precisely the retention ceiling.
        const int generousCoreCount = 32;
        var worstCase = (long)SearchScratchPolicy.MaxRetainedBytes * generousCoreCount;
        Assert.IsLessThan(8L * 1024 * 1024, worstCase);
    }

    [TestMethod]
    public void AnOrdinarySearchsBuffer_IsWorthKeeping()
    {
        // The whole point of the pooling: a keystroke has to find its buffer already the right size.
        // 2,048 entries is the largest a worker was measured holding after an ordinary query returning
        // tens of thousands of results, so that must stay comfortably inside the budget.
        Assert.IsTrue(SearchScratchPolicy.WorthRetaining<Wide>(0));
        Assert.IsTrue(SearchScratchPolicy.WorthRetaining<Wide>(51));
        Assert.IsTrue(SearchScratchPolicy.WorthRetaining<Wide>(2048));
        Assert.IsTrue(SearchScratchPolicy.WorthRetaining<Wide>(4096), "twice the largest observed in ordinary use");
    }

    [TestMethod]
    public void AWholeDriveSearchsBuffer_IsNot()
    {
        Assert.IsFalse(SearchScratchPolicy.WorthRetaining<Wide>(65_536), "2MB per worker, which is what this was before");
        Assert.IsFalse(SearchScratchPolicy.WorthRetaining<Wide>(660_000));
    }

    [TestMethod]
    public void ClearAndTrim_AnOrdinaryList_KeepsItsCapacity()
    {
        // Trimming a small one would hand back the very allocation the pool exists to avoid.
        var list = new List<Wide>();
        for (var i = 0; i < 2048; i++) list.Add(default);
        var capacity = list.Capacity;

        SearchScratchPolicy.ClearAndTrim(list);

        Assert.IsEmpty(list);
        Assert.AreEqual(capacity, list.Capacity, "a reusable buffer this size must survive for the next search");
    }

    [TestMethod]
    public void ClearAndTrim_AnOversizedList_ReleasesItsArray()
    {
        var list = new List<Wide>();
        for (var i = 0; i < 65_536; i++) list.Add(default);

        SearchScratchPolicy.ClearAndTrim(list);

        Assert.IsEmpty(list);
        Assert.IsTrue(SearchScratchPolicy.WorthRetaining<Wide>(list.Capacity),
            "Clear alone keeps the array -- this is the high water mark that never came back");
    }

    [TestMethod]
    public void ClearAndTrim_AnOrdinaryDictionary_KeepsItsBuckets()
    {
        var map = new Dictionary<int, int>();
        for (var i = 0; i < 512; i++) map[i] = i;

        SearchScratchPolicy.ClearAndTrim(map);

        Assert.IsEmpty(map);
        // Re-filling to the same size must not have to grow again, which is what keeping the buckets buys.
        for (var i = 0; i < 512; i++) map[i] = i;
        Assert.HasCount(512, map);
    }

    [TestMethod]
    public void ClearAndTrim_AnOversizedDictionary_ReleasesItsBuckets()
    {
        var map = new Dictionary<int, int>();
        for (var i = 0; i < 200_000; i++) map[i] = i;
        var before = GC.GetTotalMemory(true);

        SearchScratchPolicy.ClearAndTrim(map);
        var after = GC.GetTotalMemory(true);

        Assert.IsEmpty(map);
        Assert.IsLessThan(before, after, "the buckets should have been handed back, not just emptied");
    }

    [TestMethod]
    public void ClearAndTrim_JudgesADictionaryBeforeClearingIt()
    {
        // Count is zero after Clear, so deciding afterwards would find every dictionary small and trim
        // none of them -- the check has to happen while the size is still observable.
        var map = new Dictionary<int, int>();
        for (var i = 0; i < 200_000; i++) map[i] = i;
        var before = GC.GetTotalMemory(true);

        SearchScratchPolicy.ClearAndTrim(map);

        Assert.IsLessThan(before, GC.GetTotalMemory(true));
    }
}
