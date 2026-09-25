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
    /// it is not the trust boundary. <see cref="Verify"/> over the bytes it is about to extract is, so a
    /// caller that can name any directory still cannot get a single unsigned byte written to the install
    /// directory -- and the redirected <c>%TEMP%</c> some users run with stays usable.
    /// </remarks>
    public static bool TryGetStagedPackage(string? stagingDir, out string? zipPath, out string? signaturePath, out string? error)
    {
        zipPath = null;
        signaturePath = null;

        if (string.IsNullOrWhiteSpace(stagingDir) || !Path.IsPathRooted(stagingDir) ||
            !Path.GetFileName(Path.GetFullPath(stagingDir)).StartsWith(StagingDirPrefix, StringComparison.OrdinalIgnoreCase))
        {
            error = "Not a staged update directory.";
            return false;
        }

        var zip = Path.Combine(stagingDir, ZipFileName);
        var signature = Path.Combine(stagingDir, SignatureFileName);
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

    public static bool Verify(string zipPath, string signaturePath) => Verify(zipPath, signaturePath, PUBLIC_KEY_PEM);

    /// <summary>
    /// Verifies the staged zip and unpacks it into <paramref name="targetDir"/>, returning the directory
    /// that actually holds the payload files.
    /// </summary>
    /// <remarks>
    /// The zip is opened once, for read, with no write sharing, and that handle is held across verification
    /// and extraction. Same-user code can't be trusted to stay out of a temp directory, but it also can't
    /// open the file for writing while this handle is up, so the bytes the signature covers are the bytes
    /// that get unpacked -- which is the whole point of running this from the service rather than
    /// believing a verdict some other process reached earlier.
    /// </remarks>
    public static bool TryVerifyAndExtract(string? stagingDir, string targetDir, out string? payloadDir, out string? error) =>
        TryVerifyAndExtract(stagingDir, targetDir, PUBLIC_KEY_PEM, out payloadDir, out error);

    /// <overloads>
    /// Takes the trust anchor as a parameter so the whole verify-then-unpack path can be exercised by a
    /// test that generated its own key pair.
    /// </overloads>
    internal static bool TryVerifyAndExtract(string? stagingDir, string targetDir, string publicKeyPem, out string? payloadDir, out string? error)
    {
        payloadDir = null;

        if (!TryGetStagedPackage(stagingDir, out var zipPath, out var signaturePath, out error))
            return false;

        try
        {
            using (new FileStream(zipPath!, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (!Verify(zipPath!, signaturePath!, publicKeyPem))
                {
                    error = "Update package failed signature verification.";
                    return false;
                }

                ZipFile.ExtractToDirectory(zipPath!, targetDir, overwriteFiles: true);
            }
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

    /// <summary>
    /// False on any problem at all -- unreadable files, a malformed signature, a key that won't import.
    /// A package this cannot positively vouch for is a package that does not get installed.
    /// </summary>
    internal static bool Verify(string zipPath, string signaturePath, string publicKeyPem)
    {
        try
        {
            var fileBytes = File.ReadAllBytes(zipPath);
            var signatureBytes = File.ReadAllBytes(signaturePath);

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(publicKeyPem);

            return ecdsa.VerifyData(fileBytes, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception ex)
        {
            Logger.Log($"[UpdatePackage] Signature verification encountered error: {ex.Message}", LogLevel.Error);
            return false;
        }
    }
}
