using System.IO;
using System.Threading.Channels;
using Lertaro.App.Services.Pipe;
using Lertaro.Core;
using Lertaro.Core.Wire;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Tests.Services.Pipe;

// Each result used to be written straight from SearchStreamingAsync's callback, under a SemaphoreSlim, with
// GetAwaiter().GetResult() on an async pipe write -- sync-over-async on a thread-pool thread once per result,
// and nothing that could time it out. Rows now cross a bounded queue into one awaited writer instead, so
// these pin the two things that rewrite must not break: every row the search produced still reaches the wire
// in the order it was produced, and a client that stops reading is abandoned inside its deadline rather than
// held open for the life of the process.
[TestClass]
public sealed class AppSearchPipeWriteQueueTests
{
    private const int Rows = 300;

    private static SearchResult Row(int i) => new()
    {
        Name = $"file{i}.txt",
        Path = $@"c:\folder\file{i}.txt",
        Drive = "C",
        Attributes = FileAttributes.Normal,
        Metadata = new FileMetadata(512, DateTime.UtcNow.ToLocalTime(), DateTime.UtcNow.ToLocalTime(), DateTime.UtcNow.ToLocalTime())
    };

    private static Channel<(SearchResult, int[])> Queue(int rows)
    {
        // Unbounded here on purpose: the bound under test lives in RunStreamingSearchAsync, and a test that
        // filled it would be testing BCL channel semantics rather than this writer.
        var channel = Channel.CreateUnbounded<(SearchResult, int[])>();
        for (var i = 0; i < rows; i++)
            channel.Writer.TryWrite((Row(i), new[] { 0, 1 + i % 5 }));
        channel.Writer.TryComplete();
        return channel;
    }

    [TestMethod]
    public async Task EveryQueuedRow_ReachesThePipeInTheOrderItWasQueued()
    {
        using var stream = new MemoryStream();
        await SearchResultWithHighlightBinarySerializer.WriteHeaderAsync(stream);

        var channel = Queue(Rows);
        using var abandoned = new CancellationTokenSource();
        await AppSearchPipeService.WriteQueuedResultsAsync(channel.Reader, stream, CancellationToken.None, abandoned, TimeSpan.FromSeconds(30));

        await SearchResultWithHighlightBinarySerializer.WriteEndAsync(stream);
        stream.Position = 0;

        var read = new List<(SearchResult Result, int[] Ranges)>();
        await SearchResultWithHighlightBinarySerializer.ReadAsync(stream, (r, ranges) => read.Add((r, ranges)));

        Assert.HasCount(Rows, read, "a row the search produced must never be lost on the way to the client");
        for (var i = 0; i < Rows; i++)
        {
            Assert.AreEqual($"file{i}.txt", read[i].Result.Name, $"order at {i}");
            CollectionAssert.AreEqual(new[] { 0, 1 + i % 5 }, read[i].Ranges, $"ranges at {i}");
        }

        Assert.IsFalse(abandoned.IsCancellationRequested, "a healthy client must not trip the stall path");
    }

    [TestMethod]
    public async Task TheFlushPolicy_StillHandsRowsOutProgressively()
    {
        // The first ten results flush individually and then every fiftieth, so a client reading progressively
        // sees rows as they arrive instead of waiting for the End frame. Moving the writes into a consumer task
        // must not quietly collapse that into a single flush at the end.
        using var stream = new FlushCountingStream();
        await SearchResultWithHighlightBinarySerializer.WriteHeaderAsync(stream);
        var afterHeader = stream.FlushCount;

        var channel = Queue(Rows);
        using var abandoned = new CancellationTokenSource();
        await AppSearchPipeService.WriteQueuedResultsAsync(channel.Reader, stream, CancellationToken.None, abandoned, TimeSpan.FromSeconds(30));

        Assert.AreEqual(10 + Rows / 50, stream.FlushCount - afterHeader);
    }

    [TestMethod]
    public async Task AClientThatStopsReading_IsAbandonedInsideItsDeadline()
    {
        // This is the case that used to be unbounded: a connected client that never reads does not produce
        // ERROR_BROKEN_PIPE, so the disconnect watchdog cannot see it, and the write simply never returned.
        using var stalled = new NeverFinishingWriteStream();
        var channel = Queue(1);
        using var abandoned = new CancellationTokenSource();

        var ex = await Assert.ThrowsExactlyAsync<IOException>(() =>
            AppSearchPipeService.WriteQueuedResultsAsync(channel.Reader, stalled, CancellationToken.None, abandoned, TimeSpan.FromMilliseconds(50)));

        StringAssert.Contains(ex.Message, "stopped reading");
        Assert.IsTrue(abandoned.IsCancellationRequested,
            "the search feeding this queue has to be cancelled with it, or it keeps producing rows into a queue nobody drains");
    }

    [TestMethod]
    public async Task AClientThatDisconnected_IsStillReportedAsCancellation()
    {
        // The stall handler must not swallow the ordinary case: when the caller's own token fires, the write
        // is cancelled for a reason that has nothing to do with a slow read, and the caller's catch expects
        // an OperationCanceledException it can treat as "this request was superseded".
        using var disconnected = new CancellationTokenSource();
        using var stalled = new NeverFinishingWriteStream();
        var channel = Queue(1);
        using var abandoned = CancellationTokenSource.CreateLinkedTokenSource(disconnected.Token);

        disconnected.CancelAfter(50);

        // Derived types count here on purpose: the write surfaces as TaskCanceledException, and what this
        // test is really pinning is that the stall handler left it alone instead of rewriting it -- an
        // OperationCanceledException reaching the caller means "this request was superseded", which is true
        // here and would be a lie if a plain disconnect got reported as a stalled write.
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            AppSearchPipeService.WriteQueuedResultsAsync(channel.Reader, stalled, disconnected.Token, abandoned, TimeSpan.FromSeconds(30)));
    }

    private sealed class FlushCountingStream : MemoryStream
    {
        private int _flushes;

        public int FlushCount => _flushes;

        public override void Flush() => Interlocked.Increment(ref _flushes);

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _flushes);
            return Task.CompletedTask;
        }
    }

    // Stands in for a full pipe buffer: the OS accepts no more bytes, so the write neither completes nor
    // fails. Only the async path needs stalling -- that is the one the serializer uses.
    private sealed class NeverFinishingWriteStream : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => new(Task.Delay(Timeout.Infinite, cancellationToken));
    }
}
