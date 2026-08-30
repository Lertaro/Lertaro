namespace Lertaro.Core.Services.LocalSend;

internal enum LocalSendFileSaveStatus
{
    Success,
    SizeMismatch,
    ChecksumMismatch,
    Canceled,
    Error
}

internal readonly record struct LocalSendFileSaveResult(
    LocalSendFileSaveStatus Status, long BytesWritten, string? Error = null);

/// <summary>Writes one incoming upload while enforcing its advertised size and optional v2.2 checksum.</summary>
internal static class LocalSendIncomingFileWriter
{
    private const int BufferSize = 1024 * 1024;

    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);

    internal static async Task<LocalSendFileSaveResult> SaveAsync(Stream source, string targetPath,
        long expectedSize, string? expectedSha256, Func<bool> isCanceled, Action<long>? onProgress = null,
        Action<long>? onChecksumProgress = null, CancellationToken cancellationToken = default)
    {
        if (expectedSize < 0)
            return new(LocalSendFileSaveStatus.SizeMismatch, 0, "Negative advertised size");

        try
        {
            long written = 0;
            await using (var destination = new FileStream(targetPath, FileMode.Create, FileAccess.Write,
                FileShare.None, BufferSize, useAsync: true))
            {
                var buffer = new byte[BufferSize];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).AsTask()
                    .WaitAsync(IdleTimeout, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    if (isCanceled() || cancellationToken.IsCancellationRequested)
                        return new(LocalSendFileSaveStatus.Canceled, written);
                    if (written + read > expectedSize)
                        return new(LocalSendFileSaveStatus.SizeMismatch, written + read,
                            $"Expected {expectedSize} bytes, received at least {written + read}");

                    await destination.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
                    written += read;
                    onProgress?.Invoke(written);
                }
                await destination.FlushAsync().ConfigureAwait(false);
            }
            if (written != expectedSize)
                return new(LocalSendFileSaveStatus.SizeMismatch, written,
                    $"Expected {expectedSize} bytes, received {written}");

            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                var actual = await LocalSendChecksum.ComputeFileAsync(targetPath, isCanceled, onChecksumProgress).ConfigureAwait(false);
                if (!LocalSendChecksum.Matches(actual, expectedSha256!))
                    return new(LocalSendFileSaveStatus.ChecksumMismatch, written,
                        $"Checksum mismatch: expected {expectedSha256}, got {actual}");
            }

            return new(LocalSendFileSaveStatus.Success, written);
        }
        catch (OperationCanceledException) when (isCanceled() || cancellationToken.IsCancellationRequested)
        {
            return new(LocalSendFileSaveStatus.Canceled, 0);
        }
        catch (TimeoutException)
        {
            return new(LocalSendFileSaveStatus.Error, 0, "Idle timeout while receiving data");
        }
        catch (Exception ex)
        {
            return new(LocalSendFileSaveStatus.Error, 0, ex.Message);
        }
    }
}
