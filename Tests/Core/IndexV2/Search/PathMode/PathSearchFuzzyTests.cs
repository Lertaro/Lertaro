using Lertaro.Core.IndexV2.Search.PathMode;

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

    private static LiveIndexFixture BuildFixture()
    {
        var records = new List<FileRecord> { LiveIndexFixture.Root() };
        records.Add(new FileRecord(2, 1, "docs", FileRecordFlags.Directory));
        for (var i = 0; i < UniqueNames; i++)
            records.Add(new FileRecord((ulong)(i + 3), 2, $"report{i}.txt", FileRecordFlags.None));
        return LiveIndexFixture.Build("Z", records);
    }
}
