using System.IO.Compression;
using System.Security.Cryptography;

namespace Lertaro.Core.Services.Update;

/// <summary>
/// The staged update package both halves of the update agree on: a portable zip plus its detached ECDSA
/// signature, inside a fresh per-run directory under the caller's temp folder.
/// </summary>
/// <remarks>
/// Lives in Core because the signature has to be checked twice -- once by the App before it asks for
/// anything, and again by the elevated step that actually writes the files. The staging directory belongs
/// to the unprivileged App, so the App's own verdict proves nothing at copy time; trusting it would leave
/// the usual swap window between "verified" and "consumed" open to whatever can write that folder.
/// </remarks>
public static class UpdatePackage
{
    public const string ZipFileName = "latest.zip";
    public const string SignatureFileName = "latest.zip.sig";

    /// <summary>
    /// Prefix of the per-run staging directory. A fixed name (the historical <c>%TEMP%\LertaroUpdate</c>)
    /// let any other local process pre-create the directory and own what the updater later read from it.
    /// </summary>
    public const string StagingDirPrefix = "LertaroUpdate-";

    private const string PUBLIC_KEY_PEM =
        "-----BEGIN PUBLIC KEY-----\n" +
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE117370jTbSPgIwHLntC+Bi3SD6gJ\n" +
        "QfxAySjSpUWa6zy4n0YHVv/ZWXM9zQlF2LTqpQC0iHNdJNH+MKU9UvDMTQ==\n" +
        "-----END PUBLIC KEY-----";

    /// <summary>
    /// Creates an unused per-run staging directory and hands back its path.
    /// </summary>
    public static string CreateStagingDirectory()
    {
        var tempRoot = Path.GetTempPath();
        string path;
        do
        {
            path = Path.Combine(tempRoot, StagingDirPrefix + Path.GetRandomFileName());
        }
        while (Directory.Exists(path));

        return Directory.CreateDirectory(path).FullName;
    }

    /// <summary>
    /// The zip and signature inside <paramref name="stagingDir"/>, or null when the directory isn't a
    /// package the updater may consume.
    /// </summary>
    /// <remarks>
    /// The prefix check keeps a mispaired client from pointing the elevated copy at an arbitrary tree;
    /// it is not the trust boundary. <see cref="Verify(byte[], byte[], string)"/> over the bytes about to be
    /// extracted is, so a caller that can name any directory still cannot get a single unsigned byte written
    /// to the install directory -- and the redirected <c>%TEMP%</c> some users run with stays usable.
    ///
    /// UNC and device paths (anything starting <c>\\</c> once normalised, <c>\\?\UNC\</c> included) are
    /// refused outright: the package is always staged in a local temp directory, and a share would have the
    /// service authenticate to a server the caller picked.
    /// </remarks>
    public static bool TryGetStagedPackage(string? stagingDir, out string? zipPath, out string? signaturePath, out string? error)
    {
        zipPath = null;
        signaturePath = null;

        var fullPath = string.IsNullOrWhiteSpace(stagingDir) || !Path.IsPathRooted(stagingDir) ? null : Path.GetFullPath(stagingDir);
        if (fullPath is null || fullPath.StartsWith(@"\\", StringComparison.Ordinal) ||
            !Path.GetFileName(fullPath).StartsWith(StagingDirPrefix, StringComparison.OrdinalIgnoreCase))
        {
            error = "Not a staged update directory.";
            return false;
        }

        var zip = Path.Combine(fullPath, ZipFileName);
        var signature = Path.Combine(fullPath, SignatureFileName);
        if (!File.Exists(zip) || !File.Exists(signature))
        {
            error = "Staged update package is incomplete.";
            return false;
        }

        error = null;
        zipPath = zip;
        signaturePath = signature;
        return true;
    }

    /// <summary>
    /// Ceiling on what this process will hold in memory for one package file. The staged paths are the
    /// caller's to name, and <see cref="TryReadStagedPackage"/> allocates before anything has been
    /// verified, so the size of a refusal attempt is whatever the caller's disk holds. The shipped portable
    /// packages are ~8 MB each and their signatures 70 bytes.
    /// </summary>
    internal const long MaxPackageBytes = 64 * 1024 * 1024;

    private const long MaxSignatureBytes = 4 * 1024;

    /// <summary>
    /// Reads the staged zip and its signature into memory. The service calls this while impersonating the
    /// App that asked, so the read happens with that user's rights (and, through any link the user planted,
    /// that user's credentials) rather than LocalSystem's.
    /// </summary>
    /// <overloads>
    /// Takes the ceiling as a parameter so a test can trip it without writing 64 megabytes to disk.
    /// </overloads>
    public static bool TryReadStagedPackage(string? stagingDir, out byte[]? zip, out byte[]? signature, out string? error) =>
        TryReadStagedPackage(stagingDir, MaxPackageBytes, out zip, out signature, out error);

    internal static bool TryReadStagedPackage(string? stagingDir, long maxZipBytes, out byte[]? zip, out byte[]? signature, out string? error)
    {
        zip = null;
        signature = null;
        if (!TryGetStagedPackage(stagingDir, out var zipPath, out var signaturePath, out error))
            return false;

        try
        {
            zip = ReadCapped(zipPath!, maxZipBytes);
            signature = ReadCapped(signaturePath!, MaxSignatureBytes);
            if (zip is null || signature is null)
            {
                zip = null;
                signature = null;
                error = "Staged update package is larger than this process will read.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            zip = null;
            signature = null;
            error = ex.Message;
            return false;
        }
    }

    private static byte[]? ReadCapped(string path, long maxBytes) =>
        new FileInfo(path).Length > maxBytes ? null : File.ReadAllBytes(path);

    public static bool Verify(string zipPath, string signaturePath) => Verify(zipPath, signaturePath, PUBLIC_KEY_PEM);

    /// <summary>
    /// Verifies the package bytes and unpacks them into <paramref name="targetDir"/>, returning the directory
    /// that actually holds the payload files.
    /// </summary>
    /// <remarks>
    /// Works on bytes already read, never on the staging files again: the signature covers exactly what gets
    /// unpacked, so nothing that can write the (user-owned) staging directory can swap the zip between the
    /// verdict and the extraction.
    /// </remarks>
    public static bool TryVerifyAndExtract(byte[] zip, byte[] signature, string targetDir, out string? payloadDir, out string? error) =>
        TryVerifyAndExtract(zip, signature, targetDir, PUBLIC_KEY_PEM, out payloadDir, out error);

    /// <overloads>
    /// Takes the trust anchor as a parameter so the whole verify-then-unpack path can be exercised by a
    /// test that generated its own key pair.
    /// </overloads>
    internal static bool TryVerifyAndExtract(byte[] zip, byte[] signature, string targetDir, string publicKeyPem,
        out string? payloadDir, out string? error)
    {
        payloadDir = null;

        if (!Verify(zip, signature, publicKeyPem))
        {
            error = "Update package failed signature verification.";
            return false;
        }

        try
        {
            using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
            archive.ExtractToDirectory(targetDir, overwriteFiles: true);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Logger.Log($"[UpdatePackage] Could not unpack staged update: {ex}", LogLevel.Error);
            return false;
        }

        payloadDir = ResolvePayloadRoot(targetDir);
        error = null;
        return true;
    }

    /// <summary>
    /// The directory holding the application files: either the extraction root, or the single
    /// <c>Lertaro</c> folder the release zip wraps them in.
    /// </summary>
    public static string ResolvePayloadRoot(string extractPath)
    {
        var subDirs = Directory.GetDirectories(extractPath);
        return subDirs.Length == 1 && Path.GetFileName(subDirs[0]).Equals("Lertaro", StringComparison.OrdinalIgnoreCase)
            ? subDirs[0]
            : extractPath;
    }

    internal static bool Verify(string zipPath, string signaturePath, string publicKeyPem)
    {
        try
        {
            return Verify(File.ReadAllBytes(zipPath), File.ReadAllBytes(signaturePath), publicKeyPem);
        }
        catch (Exception ex)
        {
            Logger.Log($"[UpdatePackage] Could not read the package to verify it: {ex.Message}", LogLevel.Error);
            return false;
        }
    }

    /// <summary>
    /// False on any problem at all -- a malformed signature, a key that won't import. A package this cannot
    /// positively vouch for is a package that does not get installed.
    /// </summary>
    internal static bool Verify(byte[] zip, byte[] signature, string publicKeyPem)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(publicKeyPem);

            return ecdsa.VerifyData(zip, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception ex)
        {
            Logger.Log($"[UpdatePackage] Signature verification encountered error: {ex.Message}", LogLevel.Error);
            return false;
        }
    }
}
