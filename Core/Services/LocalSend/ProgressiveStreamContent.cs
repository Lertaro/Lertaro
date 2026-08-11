namespace Lertaro.Core.Services.LocalSend;

/// <summary>
/// Stream content with upload progress tracking for LocalSendClient.
/// ponytail: Split out purely to keep LocalSendClient.cs under the repo's 300-line limit.
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

    protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
    {
        var buffer = new byte[1024 * 1024];
        long totalRead = 0;
        int read;
        while ((read = await _stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
        {
            await stream.WriteAsync(buffer, 0, read).ConfigureAwait(false);
            totalRead += read;
            _onProgress(totalRead, _stream.Length);
        }
        if (totalRead == 0) _onProgress(0, _stream.Length);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _stream.Length;
        return true;
    }
}
