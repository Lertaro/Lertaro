using System.Buffers;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>
/// Tracks LocalSend upload progress while streaming HTTP content.
/// </summary>
public sealed class ProgressiveStreamContent : HttpContent
{
    private readonly Stream _stream;
    private readonly Action<long, long> _onProgress;

    public ProgressiveStreamContent(Stream stream, Action<long, long> onProgress)
    {
        _stream = stream;
        _onProgress = onProgress;
    }

    protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
        SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
        try
        {
            long totalRead = 0;
            int read;
            while ((read = await _stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                totalRead += read;
                _onProgress(totalRead, _stream.Length);
            }
            if (totalRead == 0) _onProgress(0, _stream.Length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _stream.Length;
        return true;
    }
}
