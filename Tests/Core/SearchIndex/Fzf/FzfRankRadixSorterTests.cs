using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.Tests.SearchIndex.Fzf;

[TestClass]
public sealed class FzfRankRadixSorterTests
{
    [TestMethod]
    public void Sort_SmallList_OrdersAscendingBySortKey()
    {
        var ranks = new List<FzfRank>
        {
            new(0, 0, 300),
            new(1, 0, 100),
            new(2, 0, 200),
        };

        FzfRankRadixSorter.Sort(ranks);

        CollectionAssert.AreEqual(new ulong[] { 100, 200, 300 }, ranks.ConvertAll(r => r.SortKey));
    }

    [TestMethod]
    public void Sort_SmallListWithEqualKeys_TieBreaksByEntryIndexAscending()
    {
        var ranks = new List<FzfRank>
        {
            new(EntryIndex: 5, Score: 0, SortKey: 10),
            new(EntryIndex: 2, Score: 0, SortKey: 10),
            new(EntryIndex: 8, Score: 0, SortKey: 10),
        };

        FzfRankRadixSorter.Sort(ranks);

        CollectionAssert.AreEqual(new[] { 2, 5, 8 }, ranks.ConvertAll(r => r.EntryIndex));
    }

    [TestMethod]
    public void Sort_LargeList_UsesRadixPathAndOrdersAscendingBySortKey()
    {
        // >=128 entries forces the radix-sort branch instead of List.Sort.
        var random = new Random(Seed: 42);
        var ranks = new List<FzfRank>();
        for (var i = 0; i < 500; i++)
            ranks.Add(new FzfRank(i, 0, (ulong)random.NextInt64(0, 1_000_000)));

        FzfRankRadixSorter.Sort(ranks);

        var keys = ranks.ConvertAll(r => r.SortKey);
        var sortedKeys = new List<ulong>(keys);
        sortedKeys.Sort();
        CollectionAssert.AreEqual(sortedKeys, keys);
    }

    [TestMethod]
    public void Sort_LargeListWithDuplicateKeys_TieBreaksByEntryIndexAscending()
    {
        var ranks = new List<FzfRank>();
        for (var i = 0; i < 200; i++)
            ranks.Add(new FzfRank(EntryIndex: 199 - i, Score: 0, SortKey: 42)); // all same key, reverse entry-index order

        FzfRankRadixSorter.Sort(ranks);

        for (var i = 0; i < ranks.Count; i++)
            Assert.AreEqual(i, ranks[i].EntryIndex);
    }

    [TestMethod]
    public void Sort_LargeListMixingDistinctAndDuplicateKeys_MatchesTheReferenceOrdering()
    {
        // The shape the search actually produces, and the one the all-same-key case above cannot check:
        // a sort key is computed per unique name, so a result set is many small groups of equal keys
        // scattered among distinct ones. Ordering by the tie-break and the key in separate passes has to
        // come out the same as comparing both at once.
        var random = new Random(Seed: 7);
        var ranks = new List<FzfRank>();
        for (var i = 0; i < 1000; i++)
            ranks.Add(new FzfRank(EntryIndex: 999 - i, Score: 0, SortKey: (ulong)random.Next(0, 50)));

        var expected = new List<FzfRank>(ranks);
        expected.Sort(FzfResultRank.Compare);

        FzfRankRadixSorter.Sort(ranks);

        CollectionAssert.AreEqual(expected, ranks);
    }

    [TestMethod]
    public void Sort_LargeListWithNegativeEntryIndices_StillOrdersThemFirst()
    {
        // Entry indices are row numbers and never negative today. The radix passes read them as raw
        // bytes, which would order a negative index AFTER every positive one unless the sign is
        // handled -- a silent wrong answer the moment anything ever hands this a synthetic index.
        var ranks = new List<FzfRank>();
        for (var i = 0; i < 200; i++)
            ranks.Add(new FzfRank(EntryIndex: 100 - i, Score: 0, SortKey: 42));

        var expected = new List<FzfRank>(ranks);
        expected.Sort(FzfResultRank.Compare);

        FzfRankRadixSorter.Sort(ranks);

        CollectionAssert.AreEqual(expected, ranks);
        Assert.AreEqual(-99, ranks[0].EntryIndex);
    }

    [TestMethod]
    public void Sort_CalledRepeatedlyWithDifferentSizes_ReusesItsScratchCorrectly()
    {
        // The scratch buffer is kept between calls and only grown, so a later, shorter sort runs over a
        // buffer still holding the previous call's entries -- reading one back would corrupt the result.
        for (var length = 400; length >= 128; length -= 37)
        {
            var random = new Random(Seed: length);
            var ranks = new List<FzfRank>();
            for (var i = 0; i < length; i++)
                ranks.Add(new FzfRank(i, 0, (ulong)random.Next(0, 1000)));

            var expected = new List<FzfRank>(ranks);
            expected.Sort(FzfResultRank.Compare);

            FzfRankRadixSorter.Sort(ranks);

            CollectionAssert.AreEqual(expected, ranks, $"length {length}");
        }
    }

    [TestMethod]
    public void Sort_EmptyList_DoesNotThrow()
    {
        var ranks = new List<FzfRank>();

        FzfRankRadixSorter.Sort(ranks);

        Assert.IsEmpty(ranks);
    }
}
