using Lertaro.Core.IndexV2.Search.PathMode;
using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.Tests.IndexV2.Search.PathMode;

// The fan-out branch of a path-mode search is the only place in the search pipeline that runs a
// Parallel.For over the index. Its token has to reach ParallelOptions: without it the per-chunk
// ThrowIfCancellationRequested is wrapped into an AggregateException, which SearchStreamPump reports as
// "the local index broke" instead of the ordinary supersede-on-next-keystroke cancellation.
[TestClass]
public sealed class PathSearchFuzzyTests
{
    private const int UniqueNames = 1500; // past the fan-out's 1024-match threshold

    [TestMethod]
    public void ACancelledFanOut_IsOperationCanceledNotAnAggregateException()
    {
        using var fixture = BuildFixture();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        fixture.Index.Read<object?>((snapshot, delta) =>
        {
            // Without ParallelOptions.CancellationToken, Parallel.For wraps the body's throw into an
            // AggregateException, which is a different type the assertion below would reject.
            Assert.ThrowsExactly<OperationCanceledException>(() =>
                PathSearchFuzzy.SearchStreaming(snapshot, delta, @"z\report", 10, _ => { }, cancelled.Token, null));
            return null;
        });
    }

    [TestMethod]
    public void ACancelledPhaseAScan_DoesNotWalkTheWholeUniqueTable()
    {
        // Phase A used to take no token at all, so a broad path-mode query superseded by the next keystroke
        // still scanned every unique name on the drive -- several abandoned scans at once, each pinning
        // every core, which is the exact problem SearchMatcher.MatchUniques' own token exists to prevent.
        using var fixture = BuildFixture();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        fixture.Index.Read<object?>((snapshot, delta) =>
        {
            Assert.ThrowsExactly<OperationCanceledException>(() =>
                SearchMatcherPath.MatchUniquesForPath(snapshot, FzfPattern.Parse("report"), cancelled.Token));
            return null;
        });
    }

    [TestMethod]
    public void ACancelledDirOnlyScan_AbortsTheSerialBranchToo()
    {
        // A dir-only query ("src\") passes a null pattern and takes the serial branch rather than the
        // Parallel.For, so it never sees ParallelOptions' cancellation and needs the check in the loop.
        using var fixture = BuildFixture();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        fixture.Index.Read<object?>((snapshot, delta) =>
        {
            Assert.ThrowsExactly<OperationCanceledException>(() =>
                SearchMatcherPath.MatchUniquesForPath(snapshot, null, cancelled.Token));
            return null;
        });
    }

    [TestMethod]
    public void AnUncancelledPhaseA_StillReturnsEveryMatchingUnique()
    {
        using var fixture = BuildFixture();
        fixture.Index.Read<object?>((snapshot, delta) =>
        {
            Assert.HasCount(UniqueNames, SearchMatcherPath.MatchUniquesForPath(snapshot, FzfPattern.Parse("report")));
            return null;
        });
    }

    private static LiveIndexFixture BuildFixture()
    {
        var records = new List<FileRecord> { LiveIndexFixture.Root() };
        records.Add(new FileRecord(2, 1, "docs", FileRecordFlags.Directory));
        for (var i = 0; i < UniqueNames; i++)
            records.Add(new FileRecord((ulong)(i + 3), 2, $"report{i}.txt", FileRecordFlags.None));
        return LiveIndexFixture.Build("Z", records);
    }
}
