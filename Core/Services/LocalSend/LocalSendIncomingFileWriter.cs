using System.Buffers;
using System.Security.Cryptography;

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

        byte[]? buffer = null;
        Task<int>? pendingRead = null;
        long written = 0;
        try
        {
            using var hash = string.IsNullOrWhiteSpace(expectedSha256) ? null : IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var destination = new FileStream(targetPath, FileMode.Create, FileAccess.Write,
                FileShare.None, bufferSize: 1, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
                while (true)
                {
                    pendingRead = source.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken).AsTask();
                    var read = await pendingRead.KeepObserved().WaitAsync(IdleTimeout, cancellationToken).ConfigureAwait(false);
                    if (read == 0) break;
                    if (isCanceled() || cancellationToken.IsCancellationRequested)
                        return new(LocalSendFileSaveStatus.Canceled, written);
                    if (read > expectedSize - written)
                        return new(LocalSendFileSaveStatus.SizeMismatch, written + read,
                            $"Expected {expectedSize} bytes, received at least {written + read}");

                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    hash?.AppendData(buffer.AsSpan(0, read));
                    written += read;
                    onProgress?.Invoke(written);
                }
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            if (isCanceled() || cancellationToken.IsCancellationRequested)
                return new(LocalSendFileSaveStatus.Canceled, written);
            if (written != expectedSize)
                return new(LocalSendFileSaveStatus.SizeMismatch, written,
                    $"Expected {expectedSize} bytes, received {written}");

            if (hash != null)
            {
                // Hash the received bytes while writing; verification needs no second full read from disk.
                onChecksumProgress?.Invoke(written);
                if (isCanceled() || cancellationToken.IsCancellationRequested)
                    return new(LocalSendFileSaveStatus.Canceled, written);
                var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
                if (!LocalSendChecksum.Matches(actual, expectedSha256!))
                    return new(LocalSendFileSaveStatus.ChecksumMismatch, written,
                        $"Checksum mismatch: expected {expectedSha256}, got {actual}");
            }

            return new(LocalSendFileSaveStatus.Success, written);
        }
        catch (OperationCanceledException) when (isCanceled() || cancellationToken.IsCancellationRequested)
        {
            return new(LocalSendFileSaveStatus.Canceled, written);
        }
        catch (TimeoutException)
        {
            return new(LocalSendFileSaveStatus.Error, written, "Idle timeout while receiving data");
        }
        catch (Exception ex)
        {
            return new(LocalSendFileSaveStatus.Error, written, ex.Message);
        }
        finally
        {
            if (buffer != null)
            {
                // A stream may ignore cancellation. Do not pool memory until its outstanding read ends.
                if (pendingRead is { IsCompleted: false })
                    _ = pendingRead.ContinueWith((_, state) => ArrayPool<byte>.Shared.Return((byte[])state!), buffer,
                        CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                else
                    ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}
