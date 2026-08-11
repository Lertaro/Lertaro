using System.Security.Cryptography;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>Computes the lowercase hexadecimal SHA-256 values used by LocalSend protocol v2.2.</summary>
internal static class LocalSendChecksum
{
    private const int BufferSize = 512 * 1024;

    internal static string Compute(ReadOnlySpan<byte> data) =>
        Convert.ToHexStringLower(SHA256.HashData(data));

    internal static async Task<string> ComputeFileAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        int read;
        while ((read = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            hash.AppendData(buffer, 0, read);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal static bool Matches(string actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
}
