using System.Globalization;
using System.Text;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>
/// Decodes an HTTP chunked request body without buffering file uploads in memory.
/// </summary>
internal sealed class ChunkedReadStream(Stream inner) : Stream
{
    private readonly Stream _inner = inner;
    private long _remaining;
    private bool _completed;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0 || _completed)
            return 0;

        if (_remaining == 0)
            await ReadChunkHeaderAsync(cancellationToken).ConfigureAwait(false);
        if (_completed)
            return 0;

        var read = await _inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, _remaining)], cancellationToken).ConfigureAwait(false);
        if (read == 0)
            throw new InvalidDataException("Unexpected end of chunked request body.");

        _remaining -= read;
        if (_remaining == 0)
            await ExpectEmptyLineAsync(cancellationToken).ConfigureAwait(false);
        return read;
    }

    private async Task ReadChunkHeaderAsync(CancellationToken token)
    {
        var line = await ReadLineAsync(token).ConfigureAwait(false);
        var separator = line.IndexOf(';');
        var sizeText = separator < 0 ? line : line[..separator];
        if (!long.TryParse(sizeText.Trim(), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _remaining) || _remaining < 0)
            throw new InvalidDataException("Invalid HTTP chunk size.");

        if (_remaining != 0)
            return;

        while (!string.IsNullOrEmpty(await ReadLineAsync(token).ConfigureAwait(false))) { }
        _completed = true;
    }

    private async Task ExpectEmptyLineAsync(CancellationToken token)
    {
        if (!string.IsNullOrEmpty(await ReadLineAsync(token).ConfigureAwait(false)))
            throw new InvalidDataException("Invalid HTTP chunk terminator.");
    }

    private async Task<string> ReadLineAsync(CancellationToken token)
    {
        var text = new StringBuilder();
        var byteBuffer = new byte[1];
        while (true)
        {
            if (text.Length > 8192)
                throw new InvalidDataException("HTTP chunk header is too long.");
            var read = await _inner.ReadAsync(byteBuffer.AsMemory(0, 1), token).ConfigureAwait(false);
            if (read == 0)
                throw new InvalidDataException("Unexpected end of chunked request body.");
            if (byteBuffer[0] == (byte)'\n')
                return text.ToString();
            if (byteBuffer[0] != (byte)'\r')
                text.Append((char)byteBuffer[0]);
        }
    }
}
