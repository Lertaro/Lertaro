using Lertaro.Core.Services.Search;
using System.IO.Pipes;

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
}
