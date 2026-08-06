namespace Lertaro.Core.Services.LocalSend;

/// <summary>
/// Stream wrapper that limits reads to a specified length to prevent over-reading network streams.
/// ponytail: Split out purely to keep LocalSendServerHandler.cs under the repo's 300-line limit.
/// </summary>
public sealed class LengthLimitedStream : Stream
{
    private readonly Stream _inner;
    private long _remaining;

    public LengthLimitedStream(Stream inner, long limit)
    {
        _inner = inner;
        _remaining = limit;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_remaining <= 0) return 0;
        var toRead = (int)Math.Min(count, _remaining);
        var read = _inner.Read(buffer, offset, toRead);
        _remaining -= read;
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_remaining <= 0) return 0;
        var toRead = (int)Math.Min(count, _remaining);
        var read = await _inner.ReadAsync(buffer.AsMemory(offset, toRead), cancellationToken).ConfigureAwait(false);
        _remaining -= read;
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_remaining <= 0) return 0;
        var toRead = (int)Math.Min(buffer.Length, _remaining);
        var read = await _inner.ReadAsync(buffer[..toRead], cancellationToken).ConfigureAwait(false);
        _remaining -= read;
        return read;
    }
}
