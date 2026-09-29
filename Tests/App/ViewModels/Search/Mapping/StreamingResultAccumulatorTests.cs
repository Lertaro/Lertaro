using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core;
using Lertaro.Core.SearchIndex;

namespace Lertaro.App.Tests.ViewModels.Search.Mapping;

[TestClass]
public sealed class StreamingResultAccumulatorTests
{
    private static readonly Dictionary<string, int> NoHistory = new();
    // With no history and no rank key set, SearchResultRankComparer falls through to path LENGTH before
    // the path itself -- so paths of differing length give a ranked order that is deliberately not the
    // arrival order, which is what makes the ordering assertions below mean something.
    private static SearchResult Result(string path) => new()
    {
        Name = System.IO.Path.GetFileName(path),
        Path = path,
        IsDir = false,
        Drive = "D",
    };

    private static List<SearchResult> Arrivals(params string[] paths) => paths.Select(Result).ToList();
    private static List<string> Paths(IEnumerable<AppSearchResult> rows) =>
        rows.Select(r => r.FullPath).ToList();
    [TestMethod]
    public void Absorb_RanksResultsRatherThanKeepingArrivalOrder()
    {
        var accumulator = new StreamingResultAccumulator("a", NoHistory);

        var rows = accumulator.Absorb(Arrivals(@"D:\aaaaaa", @"D:\a", @"D:\aaa"));

        CollectionAssert.AreEqual(new[] { @"D:\a", @"D:\aaa", @"D:\aaaaaa" }, Paths(rows));
    }

    [TestMethod]
    public void Absorb_InChunks_MatchesAbsorbingEverythingAtOnce()
    {
        // The property the whole design rests on: painting progressively must produce exactly the list
        // that painting once at the end would have. A merge that got the order subtly wrong would show
        // up here and nowhere else, because every other symptom of it looks like plausible ranking.
        var paths = Enumerable.Range(0, 500).Select(i => @"D:\" + new string('x', 1 + i % 40) + i).ToArray();

        var oneShot = new StreamingResultAccumulator("x", NoHistory);
        var expected = Paths(oneShot.Absorb(Arrivals(paths)));

        var streamed = new StreamingResultAccumulator("x", NoHistory);
        var growing = new List<SearchResult>();
        List<string> actual = new();
        foreach (var chunk in new[] { 1, 7, 3, 60, 200, 229 })
        {
            growing.AddRange(paths.Skip(growing.Count).Take(chunk).Select(Result));
            actual = Paths(streamed.Absorb(growing));
        }

        CollectionAssert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void AbsorbBatch_IndependentChunksMatchAbsorbingAGrowingPrefix()
    {
        var arrivals = Arrivals(@"D:\aaaa", @"D:\a", @"D:\aaa", @"D:\aa");
        var expected = Paths(new StreamingResultAccumulator("a", NoHistory).Absorb(arrivals));
        var batchAccumulator = new StreamingResultAccumulator("a", NoHistory);
        batchAccumulator.AbsorbBatch(arrivals.GetRange(0, 2));
        CollectionAssert.AreEqual(expected, Paths(batchAccumulator.AbsorbBatch(arrivals.GetRange(2, 2))));
        Assert.AreEqual(4, batchAccumulator.Consumed);
    }

    [TestMethod]
    public void Absorb_ALaterArrivalThatOutranksEverything_LandsAtTheTop()
    {
        var accumulator = new StreamingResultAccumulator("a", NoHistory);
        var growing = Arrivals(@"D:\aaaa", @"D:\aaaaa");
        accumulator.Absorb(growing);

        growing.Add(Result(@"D:\a"));
        var rows = accumulator.Absorb(growing);

        CollectionAssert.AreEqual(new[] { @"D:\a", @"D:\aaaa", @"D:\aaaaa" }, Paths(rows));
    }

    [TestMethod]
    public void Absorb_BuildsEachRowExactlyOnce()
    {
        // The reason progressive painting is affordable at all. If a later paint rebuilt earlier rows,
        // the cost would be quadratic in the number of paints and we would be back to rationing them.
        var accumulator = new StreamingResultAccumulator("a", NoHistory);
        var growing = Arrivals(@"D:\aa", @"D:\aaa");
        var originals = accumulator.Absorb(growing).ToList();

        growing.Add(Result(@"D:\aaaa"));
        var second = accumulator.Absorb(growing);

        Assert.AreSame(originals[0], second[0]);
        Assert.AreSame(originals[1], second[1]);
    }

    [TestMethod]
    public void Absorb_TheSameArrivalsTwice_AddsNothing()
    {
        var accumulator = new StreamingResultAccumulator("a", NoHistory);
        var growing = Arrivals(@"D:\aa", @"D:\aaa");
        accumulator.Absorb(growing);

        var rows = accumulator.Absorb(growing);

        Assert.HasCount(2, rows);
        Assert.AreEqual(2, accumulator.Consumed);
    }

    [TestMethod]
    public void Absorb_StampsRowIndexesInRankOrder()
    {
        var accumulator = new StreamingResultAccumulator("a", NoHistory);
        var rows = accumulator.Absorb(Arrivals(@"D:\aaaaaa", @"D:\a", @"D:\aaa"));

        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, rows.Select(r => r.Index).ToList());
    }

    [TestMethod]
    public void Absorb_RestampsIndexesAfterALaterArrivalReordersTheList()
    {
        var accumulator = new StreamingResultAccumulator("a", NoHistory);
        var growing = Arrivals(@"D:\aaaa", @"D:\aaaaa");
        accumulator.Absorb(growing);

        growing.Add(Result(@"D:\a"));
        var rows = accumulator.Absorb(growing);

        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, rows.Select(r => r.Index).ToList());
    }

    [TestMethod]
    public void Absorb_NoArrivals_ReturnsAnEmptyList()
    {
        var accumulator = new StreamingResultAccumulator("a", NoHistory);
        Assert.IsEmpty(accumulator.Absorb(new List<SearchResult>()));
    }

    [TestMethod]
    public void Absorb_QueryIsADirectoryPath_DropsThatDirectoryItself()
    {
        // Typing an exact directory is a request to look inside it, so its own index record is not one
        // of its own results. Applied per arrival here rather than as a RemoveAll over the whole
        // snapshot, since the accumulator only ever sees each arrival once.
        var root = @"D:\";
        var accumulator = new StreamingResultAccumulator(root, NoHistory);
        var rows = accumulator.Absorb(Arrivals(root, @"D:\keep-me"));

        CollectionAssert.AreEqual(new[] { @"D:\keep-me" }, Paths(rows));
    }

    [TestMethod]
    public void Absorb_HistoryPriority_OutranksEverythingElse()
    {
        var history = new Dictionary<string, int> { [@"D:\zzzzzzzzzz"] = 0 };
        var accumulator = new StreamingResultAccumulator("z", history);

        var rows = accumulator.Absorb(Arrivals(@"D:\a", @"D:\zzzzzzzzzz"));

        CollectionAssert.AreEqual(new[] { @"D:\zzzzzzzzzz", @"D:\a" }, Paths(rows));
    }

    [TestMethod]
    public void Absorb_SeededHistoryMatchAppearsAndIsNotDuplicatedByAnArrival()
    {
        var row = new AppSearchResult
        {
            Name = "BCompare.exe",
            FullPath = @"D:\Apps\BCompare.exe",
            ResultKind = "Application"
        };
        var seed = new SearchResultMapper.RankedCandidate(
            row, true, -50, int.MaxValue, new MatchRank(MatchRank.TierName, 0, 1), @"D:\Apps\BCompare.exe");
        var history = new Dictionary<string, int> { [@"D:\Apps\BCompare.exe"] = -50 };
        var accumulator = new StreamingResultAccumulator("bc", history, new[] { seed });

        var rows = accumulator.Absorb(Arrivals(@"D:\Apps\BCompare.exe", @"D:\bc.txt"));

        CollectionAssert.AreEqual(new[] { @"D:\Apps\BCompare.exe", @"D:\bc.txt" }, Paths(rows));
        Assert.AreSame(row, rows[0]);
        Assert.AreEqual("Application", rows[0].ResultKind);
    }

    [TestMethod]
    public void Absorb_ManyChunks_KeepsTheListFullyOrdered()
    {
        var accumulator = new StreamingResultAccumulator("f", NoHistory);
        var growing = new List<SearchResult>();
        var rnd = 7;
        for (var round = 0; round < 40; round++)
        {
            for (var i = 0; i < 25; i++)
            {
                rnd = rnd * 1103515245 + 12345;
                growing.Add(Result(@"D:\" + new string('f', 1 + Math.Abs(rnd % 60)) + growing.Count));
            }
            accumulator.Absorb(growing);
        }

        var rows = accumulator.Absorb(growing);
        var lengths = rows.Select(r => r.FullPath.Length).ToList();
        for (var i = 1; i < lengths.Count; i++)
            Assert.IsLessThanOrEqualTo(lengths[i], lengths[i - 1], $"row {i} is out of rank order");
    }

    [TestMethod]
    public void Absorb_ReusesOneOutputBuffer()
    {
        // A fresh multi-megabyte list per paint would be a large-object allocation several times a
        // second on a big search. Safe because the render pump waits for the UI to finish applying one
        // paint before computing the next, so no synchronous consumer ever overlaps a call.
        var accumulator = new StreamingResultAccumulator("a", NoHistory);
        var growing = Arrivals(@"D:\aa");
        var first = accumulator.Absorb(growing);

        growing.Add(Result(@"D:\aaa"));

        Assert.AreSame(first, accumulator.Absorb(growing));
    }

    [TestMethod]
    public void FirstChangedIndex_APureAppend_PointsAtTheOldEnd()
    {
        // The case that makes late-search paints affordable: arrivals that rank below everything shown
        // leave the existing rows exactly where they were, so the view has only the tail to update.
        var accumulator = new StreamingResultAccumulator("a", NoHistory);
        var growing = Arrivals(@"D:\a", @"D:\aa");
        accumulator.Absorb(growing);

        growing.Add(Result(@"D:\aaaa"));
        growing.Add(Result(@"D:\aaaaa"));
        accumulator.Absorb(growing);

        Assert.AreEqual(2, accumulator.FirstChangedIndex);
    }

    [TestMethod]
    public void FirstChangedIndex_AnArrivalThatOutranksEverything_PointsAtZero()
    {
        var accumulator = new StreamingResultAccumulator("a", NoHistory);
        var growing = Arrivals(@"D:\aaaa", @"D:\aaaaa");
        accumulator.Absorb(growing);

        growing.Add(Result(@"D:\a"));
        accumulator.Absorb(growing);

        Assert.AreEqual(0, accumulator.FirstChangedIndex);
    }

    [TestMethod]
    public void FirstChangedIndex_AnArrivalLandingMidList_PointsAtWhereItLanded()
    {
        var accumulator = new StreamingResultAccumulator("a", NoHistory);
        var growing = Arrivals(@"D:\a", @"D:\aa", @"D:\aaaa");
        accumulator.Absorb(growing);

        growing.Add(Result(@"D:\aaa"));
        accumulator.Absorb(growing);

        Assert.AreEqual(2, accumulator.FirstChangedIndex);
    }

    [TestMethod]
    public void FirstChangedIndex_NothingNew_PointsPastTheEnd()
    {
        var accumulator = new StreamingResultAccumulator("a", NoHistory);
        var growing = Arrivals(@"D:\a", @"D:\aa");
        accumulator.Absorb(growing);
        accumulator.Absorb(growing);

        Assert.AreEqual(2, accumulator.FirstChangedIndex);
    }

        [TestMethod]
    public void FirstChangedIndex_NeverUnderstatesWhatMoved()
    {
        // The promise the view acts on: every row before FirstChangedIndex must already be correct on
        // screen. If it ever pointed too far right, rows that had genuinely moved would be left showing
        // stale content with nothing to reveal it.
        var rnd = 11;
        var accumulator = new StreamingResultAccumulator("f", NoHistory);
        var growing = new List<SearchResult>();
        var previous = new List<string>();
        for (var round = 0; round < 30; round++)
        {
            for (var i = 0; i < 9; i++)
            {
                rnd = rnd * 1103515245 + 12345;
                growing.Add(Result(@"D:\" + new string('f', 1 + Math.Abs(rnd % 50)) + growing.Count));
            }

            var paths = Paths(accumulator.Absorb(growing));
            for (var i = 0; i < Math.Min(accumulator.FirstChangedIndex, previous.Count); i++)
                Assert.AreEqual(previous[i], paths[i], $"round {round}: row {i} moved but was reported unchanged");
            previous = paths;
        }
    }

    private static AppSearchResult ContentRow(string path) => new()
    {
        Name = System.IO.Path.GetFileName(path),
        FullPath = path,
        ResultKind = "File"
    };

    private static List<AppSearchResult> ContentRows(params string[] paths) =>
        paths.Select(ContentRow).ToList();

    [TestMethod]
    public void QueueContentPrefix_LandsAtTheFrontOnTheNextAbsorb()
    {
        var accumulator = new StreamingResultAccumulator("a", NoHistory);
        accumulator.Absorb(Arrivals(@"D:\aaa"));

        accumulator.QueueContentPrefix(ContentRows(@"D:\hit.md"));
        var rows = accumulator.Absorb(Arrivals(@"D:\aaa", @"D:\aa"));

        CollectionAssert.AreEqual(new[] { @"D:\hit.md", @"D:\aa", @"D:\aaa" }, Paths(rows));
    }

    [TestMethod]
    public void QueueContentPrefix_ThePaintThatCarriesItReportsZero()
    {
        // Prepending moves every row that was already there, so the view cannot skip comparing any of
        // them. Reporting the merge's own position instead would leave the whole list showing rows that
        // are now one or more places further down.
        var accumulator = new StreamingResultAccumulator("a", NoHistory);
        accumulator.Absorb(Arrivals(@"D:\a", @"D:\aa"));

        accumulator.QueueContentPrefix(ContentRows(@"D:\hit.md"));
        accumulator.Absorb(Arrivals(@"D:\a", @"D:\aa", @"D:\aaa"));

        Assert.AreEqual(0, accumulator.FirstChangedIndex);
    }

    [TestMethod]
    public void QueueContentPrefix_AfterTheSearchSettles_AnEmptyAbsorbAppliesIt()
    {
        // No further batch is coming once the file search has answered, so the append takes the prefix up
        // by absorbing nothing at all -- and the view must still be handed the same list it had before.
        var accumulator = new StreamingResultAccumulator("a", NoHistory);
        var rows = accumulator.Absorb(Arrivals(@"D:\aaa"));

        accumulator.QueueContentPrefix(ContentRows(@"D:\hit.md"));
        var after = accumulator.AbsorbBatch(new List<SearchResult>());

        Assert.AreSame(rows, after);
        CollectionAssert.AreEqual(new[] { @"D:\hit.md", @"D:\aaa" }, Paths(after));
        Assert.AreEqual(0, accumulator.FirstChangedIndex);
    }

    [TestMethod]
    public void QueueContentPrefix_SurvivesEveryLaterPaint()
    {
        // The whole point of holding them here rather than merging them in afterwards: a paint that
        // rebuilt the list from the ranked index matches alone would drop the content hits off the top as
        // soon as the next batch arrived, which is what made them wait for the end of the search.
        var accumulator = new StreamingResultAccumulator("a", NoHistory);
        accumulator.AbsorbBatch(Arrivals(@"D:\a"));
        accumulator.QueueContentPrefix(ContentRows(@"D:\hit1.md", @"D:\hit2.md"));
        accumulator.AbsorbBatch(Arrivals(@"D:\aa"));

        for (var i = 3; i < 8; i++)
            accumulator.AbsorbBatch(Arrivals(@"D:\" + new string('x', i)));

        var rows = accumulator.Rows;
        CollectionAssert.AreEqual(new[] { @"D:\hit1.md", @"D:\hit2.md" }, rows.Take(2).Select(r => r.FullPath).ToArray());
        Assert.AreEqual(2, accumulator.ContentPrefixCount);
    }

    [TestMethod]
    public void QueueContentPrefix_RowIndexesCountThePrefix()
    {
        var accumulator = new StreamingResultAccumulator("a", NoHistory);
        accumulator.AbsorbBatch(Arrivals(@"D:\a", @"D:\aa"));
        accumulator.QueueContentPrefix(ContentRows(@"D:\hit1.md", @"D:\hit2.md"));

        var rows = accumulator.AbsorbBatch(Arrivals(@"D:\aaa"));

        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, rows.Select(r => r.Index).ToList());
    }

    [TestMethod]
    public void FirstChangedIndex_WithAPrefixAlreadyDown_PointsPastIt()
    {
        // Once the prefix is on screen it is stable, so a later append still only disturbs the tail -- but
        // the tail's position is now measured from after the prefix, and a view told "nothing moved before
        // row 2" would be handed the content rows to compare against as though they were index matches.
        var accumulator = new StreamingResultAccumulator("a", NoHistory);
        accumulator.AbsorbBatch(Arrivals(@"D:\a", @"D:\aa"));
        accumulator.QueueContentPrefix(ContentRows(@"D:\hit.md"));
        accumulator.AbsorbBatch(new List<SearchResult>());

        accumulator.AbsorbBatch(Arrivals(@"D:\aaa"));

        Assert.AreEqual(3, accumulator.FirstChangedIndex, "1 prefix row + 2 rows already ranked below it");
    }

    [TestMethod]
    public void QueueContentPrefix_SecondBatchExtendsTheFirst_RatherThanReplacingIt()
    {
        // A provider that streams answers in batches, and the host paints each one. The old hand-off kept a
        // single queued list, so a second batch landing before the pump took up the first silently threw
        // the first away -- rows the user had already been shown would vanish mid-search.
        var accumulator = new StreamingResultAccumulator("a", NoHistory);
        accumulator.AbsorbBatch(Arrivals(@"D:\aaa"));

        accumulator.QueueContentPrefix(ContentRows(@"D:\hit1.md"));
        accumulator.QueueContentPrefix(ContentRows(@"D:\hit2.md", @"D:\hit3.md"));
        var rows = accumulator.AbsorbBatch(new List<SearchResult>());

        CollectionAssert.AreEqual(
            new[] { @"D:\hit1.md", @"D:\hit2.md", @"D:\hit3.md", @"D:\aaa" }, Paths(rows));
        Assert.AreEqual(3, accumulator.ContentPrefixCount);
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, rows.Select(r => r.Index).ToList());
        Assert.AreEqual(0, accumulator.FirstChangedIndex, "the growth moves the index matches, so nothing on screen can be trusted");
    }

    [TestMethod]
    public void QueueContentPrefix_GrowthAfterThePaintBefore_KeepsEarlierRowsInPlace()
    {
        var accumulator = new StreamingResultAccumulator("a", NoHistory);
        accumulator.AbsorbBatch(Arrivals(@"D:\a"));
        accumulator.QueueContentPrefix(ContentRows(@"D:\hit1.md"));
        var firstBlock = accumulator.AbsorbBatch(new List<SearchResult>());
        var settledRow = firstBlock[1];

        accumulator.QueueContentPrefix(ContentRows(@"D:\hit2.md"));
        accumulator.AbsorbBatch(new List<SearchResult>());

        Assert.AreEqual(@"D:\hit1.md", accumulator.Rows[0].FullPath, "the batch already shown must not move");
        Assert.AreSame(settledRow, accumulator.Rows[2], "nor be rebuilt");
    }

    [TestMethod]
    public void QueueContentPrefix_EmptyOrAbsent_ChangesNothing()
    {
        var accumulator = new StreamingResultAccumulator("a", NoHistory);
        accumulator.AbsorbBatch(Arrivals(@"D:\a"));

        accumulator.QueueContentPrefix(new List<AppSearchResult>());
        var rows = accumulator.AbsorbBatch(new List<SearchResult>());

        Assert.AreEqual(0, accumulator.ContentPrefixCount);
        CollectionAssert.AreEqual(new[] { @"D:\a" }, Paths(rows));
    }
}
