using System.Net;
using System.Text;
using Lertaro.Core.Services.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public sealed class LocalSendPrepareUploadHandlerTests
{
    private const string RequestBody = """
        {"info":{"alias":"Sender","version":"2.2"},"files":{"file":{"id":"file","fileName":"test.txt","size":0,"fileType":"text/plain"}}}
        """;

    [TestMethod]
    public async Task HandleAsync_SenderDisconnectsWhileWaiting_CancelsSession()
    {
        using var server = new LocalSendServer();
        await using var stream = new PendingDisconnectStream();
        var requestShown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? canceledSessionId = null;
        server.UploadRequested += (_, _) => requestShown.TrySetResult();
        server.SessionCanceled += (_, sessionId) => canceledSessionId = sessionId;

        var handling = HandleAsync(server, stream);
        await requestShown.Task;
        stream.Disconnect();
        await handling;

        Assert.IsNotNull(canceledSessionId);
        Assert.IsFalse(server.HasActiveSessions);
        Assert.AreEqual(0, stream.WrittenLength);
    }

    [TestMethod]
    public async Task HandleAsync_ReceiverAccepts_CancelsMonitorAndReturnsSession()
    {
        using var server = new LocalSendServer();
        await using var stream = new PendingDisconnectStream();
        var requestShown = new TaskCompletionSource<LocalSendUploadRequestArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.UploadRequested += (_, args) => requestShown.TrySetResult(args);

        var handling = HandleAsync(server, stream);
        var request = await requestShown.Task;
        request.Respond(true);
        await handling;

        Assert.IsTrue(server.HasActiveSessions);
        StringAssert.Contains(stream.GetWrittenText(), "HTTP/1.1 200 OK");
    }

    private static Task HandleAsync(LocalSendServer server, Stream stream) => LocalSendPrepareUploadHandler.HandleAsync(
        server, stream, [], RequestBody, new IPEndPoint(IPAddress.Loopback, 12345), null, v2: true, CancellationToken.None);

    private sealed class PendingDisconnectStream : Stream
    {
        private readonly MemoryStream _output = new();
        private readonly TaskCompletionSource _disconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal long WrittenLength => _output.Length;
        internal void Disconnect() => _disconnected.TrySetResult();
        internal string GetWrittenText() => Encoding.UTF8.GetString(_output.ToArray());
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _disconnected.Task.WaitAsync(cancellationToken);
            return 0;
        }
        public override void Write(byte[] buffer, int offset, int count) => _output.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            _output.WriteAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _output.Dispose();
            base.Dispose(disposing);
        }
    }
}
