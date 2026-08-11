using System.Security.Cryptography;

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
            BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        long processed = 0;
        onProgress?.Invoke(0);
        int read;
        while ((read = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (isCanceled()) throw new OperationCanceledException(token);
            hash.AppendData(buffer, 0, read);
            processed += read;
            onProgress?.Invoke(processed);
        }
        if (isCanceled()) throw new OperationCanceledException(token);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal static bool Matches(string actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
}
