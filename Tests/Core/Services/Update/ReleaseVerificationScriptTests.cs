using System.Diagnostics;
using System.Security.Cryptography;

namespace Lertaro.Core.Tests.Services.Update;

[TestClass]
public sealed class ReleaseVerificationScriptTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("LertaroReleaseVerification-").FullName;
    private readonly ECDsa _trusted = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public void Dispose()
    {
        _trusted.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [TestMethod]
    public async Task CheckSigningKey_CurrentKeyPassesAndRetiredKeyIsRejected()
    {
        using var retired = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var valid = await RunAsync(_trusted, checkKey: true);
        var invalid = await RunAsync(retired, checkKey: true);

        Assert.AreEqual(0, valid.ExitCode, valid.Output);
        Assert.AreNotEqual(0, invalid.ExitCode);
        Assert.Contains("does not match the updater trust anchor", invalid.Output);
        Assert.DoesNotContain(retired.ExportECPrivateKeyPem(), invalid.Output);
    }

    [TestMethod]
    public async Task CheckSigningKey_PublicHalfAloneCannotSignARelease()
    {
        using var publicOnly = ECDsa.Create();
        publicOnly.ImportFromPem(_trusted.ExportSubjectPublicKeyInfoPem());

        var result = await RunAsync(publicOnly, checkKey: true, publicOnly: true);

        Assert.AreNotEqual(0, result.ExitCode);
    }

    [TestMethod]
    public async Task Package_SignedByTrustedKeyPasses_WithoutAConfiguredPrivateKey()
    {
        var result = await RunAsync(_trusted);

        Assert.AreEqual(0, result.ExitCode, result.Output);
        Assert.Contains("Verified package.zip", result.Output);
    }

    [TestMethod]
    public async Task Package_SelfConsistentSignatureByAnotherKeyIsRejected()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var result = await RunAsync(other);

        Assert.AreNotEqual(0, result.ExitCode);
        Assert.Contains("Release signature verification failed", result.Output);
    }

    [TestMethod]
    public async Task Package_ModifiedAfterSigningIsRejected()
    {
        var result = await RunAsync(_trusted, tamper: true);

        Assert.AreNotEqual(0, result.ExitCode);
        Assert.Contains("Release signature verification failed", result.Output);
    }

    [TestMethod]
    public async Task Package_MissingSignatureIsRejected()
    {
        var result = await RunAsync(_trusted, omitSignature: true);

        Assert.AreNotEqual(0, result.ExitCode);
    }

    private async Task<(int ExitCode, string Output)> RunAsync(ECDsa signer, bool checkKey = false,
        bool tamper = false, bool omitSignature = false, bool publicOnly = false)
    {
        var trustSource = Path.Combine(_directory, "UpdatePackage.cs");
        var lines = _trusted.ExportSubjectPublicKeyInfoPem().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        File.WriteAllText(trustSource, "private const string PUBLIC_KEY_PEM =\n"
            + string.Join(" +\n", lines.Select(line => $"\"{line}\\n\"")) + ";");
        var start = new ProcessStartInfo("pwsh.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", FindScript(),
            "-TrustSourcePath", trustSource }) start.ArgumentList.Add(argument);
        start.Environment.Remove("LERTARO_SIGNING_KEY");
        if (checkKey)
        {
            var pem = publicOnly ? signer.ExportSubjectPublicKeyInfoPem() : signer.ExportECPrivateKeyPem();
            start.Environment["LERTARO_SIGNING_KEY"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(pem));
            start.ArgumentList.Add("-CheckSigningKey");
        }
        else
        {
            var path = Path.Combine(_directory, "package.zip");
            byte[] package = [0x50, 0x4b, 0x03, 0x04, 1, 2, 3];
            File.WriteAllBytes(path, package);
            if (!omitSignature)
                File.WriteAllBytes(path + ".sig", signer.SignData(package, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
            if (tamper) File.AppendAllText(path, "changed");
            start.ArgumentList.Add("-PackagePath");
            start.ArgumentList.Add(path);
        }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        catch { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output + await error);
    }

    private static string FindScript()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine(directory.FullName, "verify-release.ps1");
            if (File.Exists(path)) return path;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Could not locate verify-release.ps1.");
    }
}
