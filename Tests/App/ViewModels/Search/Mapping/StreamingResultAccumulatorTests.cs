using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core;

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
            row, true, -50, int.MaxValue, 1, @"D:\Apps\BCompare.exe");
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
}
