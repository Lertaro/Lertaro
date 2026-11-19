using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Lertaro.Core.Tests.Services.Update;

[TestClass]
public sealed class ReleaseNotesScriptTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("LertaroReleaseNotes_中文_").FullName;
    private static readonly string[] Artifacts = ["Lertaro-Setup.exe", "Lertaro-Portable.zip", "Lertaro-Portable.zip.sig",
        "Lertaro-Setup-arm64.exe", "Lertaro-Portable-arm64.zip", "Lertaro-Portable-arm64.zip.sig"];
    private string OutputPath => Path.Combine(_root, "dist", "release-notes.md");

    public void Dispose()
    {
        // Git marks loose objects read-only on Windows; remove that bit only in this test's own repo.
        foreach (var path in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Generate_UsesActualArtifactHashesAndTagHistory(bool hasPreviousTag)
    {
        await PrepareAsync(hasPreviousTag);

        var result = await GenerateAsync();

        Assert.AreEqual(0, result.ExitCode, result.Output);
        var text = File.ReadAllText(OutputPath);
        Assert.Contains("# v1.0.1", text);
        Assert.Contains("修复签名", text);
        if (hasPreviousTag) Assert.DoesNotContain("initial release", text);
        else Assert.Contains("initial release", text);
        Assert.DoesNotContain("\r", text);
        foreach (var name in Artifacts)
        {
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(_root, "dist", name)))).ToLowerInvariant();
            Assert.Contains($"* **{name}**: `{hash}`", text);
        }
        Assert.Contains("openssl dgst -sha256 -verify public_key.pem -signature Lertaro-Portable-arm64.zip.sig Lertaro-Portable-arm64.zip", text);
        Assert.Contains("-----BEGIN PUBLIC KEY-----", text);
        Assert.DoesNotContain("PRIVATE KEY", text);
        Assert.IsFalse(File.ReadAllBytes(OutputPath).AsSpan().StartsWith(Encoding.UTF8.Preamble));

        var original = File.ReadAllBytes(OutputPath);
        var repeat = await GenerateAsync();
        Assert.AreEqual(0, repeat.ExitCode, repeat.Output);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(OutputPath));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Generate_MissingOrTamperedArtifactRemovesStaleNotes(bool tamper)
    {
        await PrepareAsync(true);
        File.WriteAllText(OutputPath, "stale checksums");
        var zip = Path.Combine(_root, "dist", "Lertaro-Portable.zip");
        if (tamper) File.AppendAllText(zip, "tampered");
        else File.Delete(zip + ".sig");

        var result = await GenerateAsync();

        Assert.AreNotEqual(0, result.ExitCode);
        Assert.IsFalse(File.Exists(OutputPath));
    }

    [TestMethod]
    public async Task Generate_TagDoesNotMatchCheckout_IsRejected()
    {
        await PrepareAsync(true);
        await GitAsync("commit", "--allow-empty", "-m", "unreleased change");

        var result = await GenerateAsync();

        Assert.AreNotEqual(0, result.ExitCode);
        Assert.Contains("HEAD must match release tag", result.Output);
        Assert.IsFalse(File.Exists(OutputPath));
    }

    private async Task PrepareAsync(bool previousTag)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Directory.CreateDirectory(Path.Combine(_root, "Core", "Services", "Update"));
        var publicLines = key.ExportSubjectPublicKeyInfoPem().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        File.WriteAllText(Path.Combine(_root, "Core", "Services", "Update", "UpdatePackage.cs"),
            "private const string PUBLIC_KEY_PEM =\n" + string.Join(" +\n", publicLines.Select(line => $"\"{line}\\n\"")) + ";");
        Directory.CreateDirectory(Path.Combine(_root, "dist"));
        foreach (var name in Artifacts.Where(name => !name.EndsWith(".sig", StringComparison.Ordinal)))
        {
            // Synthetic package bytes exercise hashing/signature verification without building an application.
            var bytes = Encoding.UTF8.GetBytes("fixture " + name);
            var path = Path.Combine(_root, "dist", name);
            File.WriteAllBytes(path, bytes);
            if (name.EndsWith(".zip", StringComparison.Ordinal))
                File.WriteAllBytes(path + ".sig", key.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        }
        await GitAsync("init", "-q");
        await GitAsync("config", "user.name", "Release test");
        await GitAsync("config", "user.email", "release-test@example.invalid");
        await GitAsync("config", "commit.gpgsign", "false");
        await GitAsync("config", "tag.gpgsign", "false");
        await GitAsync("add", "Core");
        await GitAsync("commit", "-q", "-m", "initial release");
        if (previousTag) await GitAsync("tag", "v1.0.0");
        await GitAsync("commit", "-q", "--allow-empty", "-m", "修复签名");
        await GitAsync("tag", "v1.0.1");
    }

    private async Task GitAsync(params string[] arguments)
    {
        var result = await RunAsync("git", arguments);
        Assert.AreEqual(0, result.ExitCode, result.Output);
    }

    private Task<(int ExitCode, string Output)> GenerateAsync() => RunAsync("pwsh.exe",
        ["-NoLogo", "-NoProfile", "-NonInteractive", "-File", FindScript(), "-Tag", "v1.0.1",
            "-RepositoryPath", _root, "-ArtifactDirectory", Path.Combine(_root, "dist")]);

    private async Task<(int ExitCode, string Output)> RunAsync(string executable, string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = _root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        start.Environment.Remove("LERTARO_SIGNING_KEY");
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        catch { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output + await error);
    }

    private static string FindScript()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "generate-release-notes.ps1");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Could not locate generate-release-notes.ps1.");
    }
}
