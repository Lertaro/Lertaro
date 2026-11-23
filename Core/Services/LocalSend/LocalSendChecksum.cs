using System.Security.Cryptography;
using System.Buffers;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>Computes the lowercase hexadecimal SHA-256 values used by LocalSend protocol v2.2.</summary>
internal static class LocalSendChecksum
{
    private const int BufferSize = 512 * 1024;

    internal static string Compute(ReadOnlySpan<byte> data) =>
        Convert.ToHexStringLower(SHA256.HashData(data));

    internal static Task<string> ComputeFileAsync(string path, CancellationToken token, Action<long>? onProgress = null) =>
        ComputeFileAsync(path, () => token.IsCancellationRequested, token, onProgress);

    internal static Task<string> ComputeFileAsync(string path, Func<bool> isCanceled, Action<long>? onProgress = null) =>
        ComputeFileAsync(path, isCanceled, CancellationToken.None, onProgress);

    private static async Task<string> ComputeFileAsync(string path, Func<bool> isCanceled,
        CancellationToken token, Action<long>? onProgress)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        long processed = 0;
        try
        {
            onProgress?.Invoke(0);
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), token).ConfigureAwait(false)) > 0)
            {
                if (isCanceled()) throw new OperationCanceledException(token);
                hash.AppendData(buffer.AsSpan(0, read));
                processed += read;
                onProgress?.Invoke(processed);
            }
            if (isCanceled()) throw new OperationCanceledException(token);
            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    internal static bool Matches(string actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
}
