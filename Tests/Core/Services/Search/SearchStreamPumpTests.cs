using Lertaro.Core.Services.Search;
using System.IO.Pipes;
using Lertaro.Core.Wire;

namespace Lertaro.Core.Tests.Services.Search;

[TestClass]
public sealed class SearchStreamPumpTests
{
    [TestMethod]
    public void ResultChannel_IsBoundedToPreventSlowClientsAccumulatingResults()
    {
        var channel = SearchStreamPump.CreateResultChannel();

        for (var i = 0; i < SearchStreamPump.ResultBufferCapacity; i++)
            Assert.IsTrue(channel.Writer.TryWrite(new SearchResult()));

        Assert.IsFalse(channel.Writer.TryWrite(new SearchResult()));
    }

    [TestMethod]
    public async Task DisconnectWatcher_CancelsRequestWhenClientClosesPipe()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var query = new CancellationTokenSource();
        var name = "LertaroSpaceDisconnect_" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token), client.ConnectAsync(timeout.Token));
        var watching = SearchStreamPump.WatchForClientDisconnectAsync(server, query, timeout.Token);

        client.Dispose();
        await watching;

        Assert.IsTrue(query.IsCancellationRequested, "Closing the client must cancel the server-side walk.");
    }

    [TestMethod]
    public async Task DisconnectWatcher_StoppingAfterSuccessfulRequestDoesNotCancelIt()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var query = new CancellationTokenSource();
        using var stop = new CancellationTokenSource();
        var name = "LertaroSpaceComplete_" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token), client.ConnectAsync(timeout.Token));
        var watching = SearchStreamPump.WatchForClientDisconnectAsync(server, query, stop.Token);

        stop.Cancel();
        await watching.WaitAsync(timeout.Token);

        Assert.IsFalse(query.IsCancellationRequested);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(127)]
    [DataRow(128)]
    [DataRow(129)]
    [DataRow(300)]
    public async Task WriteResults_ProfileExclusions_AreAppliedBeforeWritingAndPreserveOrder(int count)
    {
        var channel = SearchStreamPump.CreateResultChannel();
        using var visibility = new CallerVisibility([@"C:\hidden"]);
        var expected = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var path = $@"C:\{(i % 3 == 0 ? "hidden" : "visible")}\{i}.txt";
            Assert.IsTrue(channel.Writer.TryWrite(new SearchResult { Path = path, Name = $"{i}.txt" }));
            if (i % 3 != 0) expected.Add(path);
        }
        channel.Writer.Complete();
        using var stream = new MemoryStream();
        await SearchResponseBinarySerializer.WriteHeaderAsync(stream);

        await SearchStreamPump.WriteResultsAsync(channel.Reader, stream, visibility, CancellationToken.None);

        await SearchResponseBinarySerializer.WriteEndAsync(stream);
        stream.Position = 0;
        var actual = new List<string>();
        await SearchResponseBinarySerializer.ReadAsync(stream, row => actual.Add(row.Path));
        CollectionAssert.AreEqual(expected, actual);
    }

    [TestMethod]
    public async Task WriteResults_FirstResult_FlushesWithoutWaitingForCompletion()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var channel = SearchStreamPump.CreateResultChannel();
        Assert.IsTrue(channel.Writer.TryWrite(new SearchResult { Path = @"C:\visible.txt" }));
        using var stream = new FlushSignalingStream();
        var writing = SearchStreamPump.WriteResultsAsync(channel.Reader, stream, CallerVisibility.Everything, timeout.Token);
        try
        {
            await stream.Flushed.Task.WaitAsync(timeout.Token);
            Assert.IsGreaterThan(0L, stream.Length);
            Assert.IsFalse(writing.IsCompleted, "The first result must be flushed while the producer is still running.");
        }
        finally
        {
            channel.Writer.TryComplete();
            await writing;
        }
    }

    [TestMethod]
    public async Task WriteResults_CancelledWhileWaiting_StopsWithoutWriting()
    {
        var channel = SearchStreamPump.CreateResultChannel();
        using var cancelled = new CancellationTokenSource();
        using var stream = new MemoryStream();
        var writing = SearchStreamPump.WriteResultsAsync(channel.Reader, stream, CallerVisibility.Everything, cancelled.Token);
        cancelled.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => writing);

        Assert.AreEqual(0L, stream.Length);
    }

    private sealed class FlushSignalingStream : MemoryStream
    {
        public TaskCompletionSource Flushed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            Flushed.TrySetResult();
            return base.FlushAsync(cancellationToken);
        }
    }
}
