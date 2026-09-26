using Lertaro.Core.Wire;

namespace Lertaro.Core.Tests.Wire;

// The App search pipe bounds each request read with a timeout so a client that connects and then goes
// silent is dropped instead of parking a handler -- and its connection slot -- until the process exits.
// That defence only works if the read actually observes the token, which is what this pins. Without it,
// a single stalled client would hold one of the pipe's connection slots indefinitely.
[TestClass]
public sealed class SearchRequestReadCancellationTests
{
    [TestMethod]
    public async Task ReadSearchRequestAsync_StalledStream_HonoursCancellation()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => SearchRequestBinarySerializer.ReadSearchRequestAsync(new StalledStream(), cts.Token));
    }

    [TestMethod]
    public async Task ReadSearchRequestAsync_AlreadyCancelledToken_DoesNotRead()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => SearchRequestBinarySerializer.ReadSearchRequestAsync(new StalledStream(), cts.Token));
    }

    // A connected client that has sent nothing at all: reads never complete and never return 0, so only
    // the token can end the wait.
    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
