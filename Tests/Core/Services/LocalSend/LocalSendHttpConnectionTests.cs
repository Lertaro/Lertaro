using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Lertaro.Core.Services.LocalSend;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public sealed class LocalSendHttpConnectionTests
{
    [TestMethod]
    public async Task ProcessAsync_Http11KeepAlive_ProcessesMultipleRequestsOnOneConnection()
    {
        const string request = "GET /api/localsend/v2/info HTTP/1.1\r\nHost: test\r\n\r\n" +
            "GET /api/localsend/v2/info HTTP/1.1\r\nHost: test\r\nConnection: close\r\n\r\n";
        using var stream = new DuplexTestStream(request);

        await LocalSendServerHandler.ProcessAsync(new LocalSendServer(), stream, new IPEndPoint(IPAddress.Loopback, 53317), CancellationToken.None);

        Assert.HasCount(2, Regex.Matches(stream.GetWrittenText(), "HTTP/1.1 200 OK"));
        Assert.IsFalse(stream.GetWrittenText().Contains("Connection: close", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class DuplexTestStream : Stream
    {
        private readonly MemoryStream _input;
        private readonly MemoryStream _output = new();

        internal DuplexTestStream(string input) => _input = new MemoryStream(Encoding.UTF8.GetBytes(input));

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => _input.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _input.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _input.ReadAsync(buffer, cancellationToken);
        public override void Write(byte[] buffer, int offset, int count) => _output.Write(buffer, offset, count);
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _output.WriteAsync(buffer, offset, count, cancellationToken);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => _output.WriteAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        internal string GetWrittenText() => Encoding.UTF8.GetString(_output.ToArray());

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _input.Dispose();
                _output.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
