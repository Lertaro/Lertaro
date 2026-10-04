using System.IO.Compression;
using System.Security.Cryptography;

using Lertaro.Core.Services.Update;

namespace Lertaro.Core.Tests.Services.Update;

[TestClass]
public sealed class UpdatePackageTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("LertaroPackageTests-").FullName;
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _publicKeyPem;

    public UpdatePackageTests() => _publicKeyPem = _key.ExportSubjectPublicKeyInfoPem();

    public void Dispose()
    {
        _key.Dispose();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    /// <summary>
    /// A staging directory holding a real zip and its signature. The payload is a couple of text files --
    /// nothing about the code under test reads what is inside, only that the bytes match the signature.
    /// </summary>
    private string CreateStagedPackage(string packageFileName = "latest.zip", bool wrapInLertaroFolder = false,
        bool signWithOwnKey = true, bool tamperAfterSigning = false)
    {
        var stagingDir = Path.Combine(_root, UpdatePackage.StagingDirPrefix + Path.GetRandomFileName());
        var sourceDir = Path.Combine(_root, "source-" + Path.GetRandomFileName());
        var payloadDir = wrapInLertaroFolder ? Path.Combine(sourceDir, "Lertaro") : sourceDir;
        Directory.CreateDirectory(payloadDir);
        File.WriteAllText(Path.Combine(payloadDir, "Lertaro.App.exe"), "not really an executable");
        Directory.CreateDirectory(Path.Combine(payloadDir, "Plugins"));
        File.WriteAllText(Path.Combine(payloadDir, "Plugins", "one.dll"), "not really a dll");

        var zipPath = Path.Combine(stagingDir, packageFileName);
        Directory.CreateDirectory(stagingDir);
        ZipFile.CreateFromDirectory(sourceDir, zipPath);

        if (signWithOwnKey)
        {
            var signature = _key.SignData(File.ReadAllBytes(zipPath), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            File.WriteAllBytes(zipPath + ".sig", signature);
        }

        if (tamperAfterSigning)
        {
            // The swap an unprivileged process would attempt between the verdict and the copy.
            File.AppendAllText(zipPath, "extra entry");
        }

        return stagingDir;
    }

    private static void RenameSignatureOutOfPlace(string stagingDir) =>
        File.Move(Path.Combine(stagingDir, UpdatePackage.SignatureFileName), Path.Combine(stagingDir, "unused"));

    [TestMethod]
    public void CreateStagingDirectory_UsesPerRunPrefixAndExists()
    {
        var created = UpdatePackage.CreateStagingDirectory();

        StringAssert.StartsWith(Path.GetFileName(created), UpdatePackage.StagingDirPrefix);
        Assert.IsTrue(Directory.Exists(created));
        Directory.Delete(created);
    }

    [TestMethod]
    public void CreateStagingDirectory_TwoCallsNeverCollide()
    {
        var first = UpdatePackage.CreateStagingDirectory();
        var second = UpdatePackage.CreateStagingDirectory();

        Assert.AreNotEqual(first, second, "a fixed staging name is what let another process own the payload");
        Directory.Delete(first);
        Directory.Delete(second);
    }

    [TestMethod]
    public void TryGetStagedPackage_CompletePackage_ResolvesBothFiles()
    {
        var stagingDir = CreateStagedPackage();

        Assert.IsTrue(UpdatePackage.TryGetStagedPackage(stagingDir, out var zipPath, out var signaturePath, out var error));
        Assert.AreEqual(Path.Combine(stagingDir, UpdatePackage.ZipFileName), zipPath);
        Assert.AreEqual(Path.Combine(stagingDir, UpdatePackage.SignatureFileName), signaturePath);
        Assert.IsNull(error);
    }

    [TestMethod]
    public void TryGetStagedPackage_NullOrRelativeOrForeignName_IsRefused()
    {
        foreach (var candidate in new string?[] { null, "", "relative/dir", Path.Combine(_root, "SomeOtherTool") })
        {
            Assert.IsFalse(UpdatePackage.TryGetStagedPackage(candidate, out _, out _, out var error),
                $"'{candidate}' should not be accepted as a staging directory");
            Assert.IsNotNull(error);
        }
    }

    [TestMethod]
    public void TryGetStagedPackage_MissingSignature_IsRefused()
    {
        var stagingDir = CreateStagedPackage(signWithOwnKey: false);

        Assert.IsFalse(UpdatePackage.TryGetStagedPackage(stagingDir, out _, out _, out var error));
        StringAssert.Contains(error!, "incomplete");
    }

    [TestMethod]
    public void Verify_SignedPackage_Passes()
    {
        var stagingDir = CreateStagedPackage();

        Assert.IsTrue(UpdatePackage.Verify(
            Path.Combine(stagingDir, UpdatePackage.ZipFileName),
            Path.Combine(stagingDir, UpdatePackage.SignatureFileName),
            _publicKeyPem));
    }

    [TestMethod]
    public void Verify_ZipModifiedAfterSigning_Fails()
    {
        var stagingDir = CreateStagedPackage(tamperAfterSigning: true);

        Assert.IsFalse(UpdatePackage.Verify(
            Path.Combine(stagingDir, UpdatePackage.ZipFileName),
            Path.Combine(stagingDir, UpdatePackage.SignatureFileName),
            _publicKeyPem));
    }

    [TestMethod]
    public void Verify_SignedByAnotherKey_Fails()
    {
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var stagingDir = CreateStagedPackage();
        File.WriteAllBytes(Path.Combine(stagingDir, UpdatePackage.SignatureFileName),
            otherKey.SignData(File.ReadAllBytes(Path.Combine(stagingDir, UpdatePackage.ZipFileName)),
                HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));

        Assert.IsFalse(UpdatePackage.Verify(
            Path.Combine(stagingDir, UpdatePackage.ZipFileName),
            Path.Combine(stagingDir, UpdatePackage.SignatureFileName),
            _publicKeyPem));
    }

    [TestMethod]
    public void Verify_SignatureThatIsNotADerBlob_FailsWithoutThrowing()
    {
        var stagingDir = CreateStagedPackage();
        File.WriteAllBytes(Path.Combine(stagingDir, UpdatePackage.SignatureFileName), new byte[] { 1, 2, 3, 4 });

        Assert.IsFalse(UpdatePackage.Verify(
            Path.Combine(stagingDir, UpdatePackage.ZipFileName),
            Path.Combine(stagingDir, UpdatePackage.SignatureFileName),
            _publicKeyPem));
    }

    [TestMethod]
    [DataRow(@"\\server\share\LertaroUpdate-abc", "a UNC share")]
    [DataRow(@"\\?\UNC\server\share\LertaroUpdate-abc", "a UNC share in device-path form")]
    [DataRow("//server/share/LertaroUpdate-abc", "a UNC share written with forward slashes")]
    [DataRow(@"\\.\C:\Temp\LertaroUpdate-abc", "a device path")]
    public void TryGetStagedPackage_NetworkOrDevicePath_IsRefused(string stagingDir, string because)
    {
        // Refused on the name alone, before anything touches the path: even looking for the files would have
        // the service authenticate to whatever server the caller named.
        Assert.IsFalse(UpdatePackage.TryGetStagedPackage(stagingDir, out _, out _, out var error), because);
        StringAssert.Contains(error!, "Not a staged update directory");
    }

    [TestMethod]
    public void TryReadStagedPackage_MissingSignature_ReadsNothing()
    {
        var stagingDir = CreateStagedPackage();
        RenameSignatureOutOfPlace(stagingDir);

        Assert.IsFalse(UpdatePackage.TryReadStagedPackage(stagingDir, out var zip, out var signature, out var error));
        Assert.IsNull(zip);
        Assert.IsNull(signature);
        StringAssert.Contains(error!, "incomplete");
    }

    [TestMethod]
    public void TryReadStagedPackage_PackageOverTheCeiling_IsRefusedBeforeReading()
    {
        // Without the ceiling, naming one huge readable file makes the LocalSystem service allocate it before
        // any signature has been checked. The shipped ceiling is far above these bytes; it is a parameter so a
        // test can trip it without writing 64 megabytes to disk.
        var stagingDir = CreateStagedPackage();

        Assert.IsFalse(UpdatePackage.TryReadStagedPackage(stagingDir, maxZipBytes: 1, out var zip, out var signature, out var error));

        Assert.IsNull(zip);
        Assert.IsNull(signature);
        StringAssert.Contains(error!, "larger than this process will read");
    }

    [TestMethod]
    public void MaxPackageBytes_SitsAboveEveryRealPackage()
    {
        // A ceiling below what the product ships would make every update refuse itself. Measured against the
        // v5.8.1 release assets, where the largest portable zip is 8,197,983 bytes.
        Assert.IsTrue(UpdatePackage.MaxPackageBytes > 8_197_983);
    }

    [TestMethod]
    public void TryVerifyAndExtract_FlatPackage_UnpacksAndReturnsTargetRoot()
    {
        var (zip, signature) = Read(CreateStagedPackage());
        var targetDir = Path.Combine(_root, "unpacked-flat");

        Assert.IsTrue(UpdatePackage.TryVerifyAndExtract(zip, signature, targetDir, _publicKeyPem, out var payloadDir, out var error));
        Assert.AreEqual(targetDir, payloadDir);
        Assert.IsNull(error);
        Assert.IsTrue(File.Exists(Path.Combine(payloadDir!, "Lertaro.App.exe")));
        Assert.IsTrue(File.Exists(Path.Combine(payloadDir!, "Plugins", "one.dll")));
    }

    [TestMethod]
    public void TryVerifyAndExtract_PackageWrappedInLertaroFolder_ReturnsTheInnerFolder()
    {
        var (zip, signature) = Read(CreateStagedPackage(wrapInLertaroFolder: true));
        var targetDir = Path.Combine(_root, "unpacked-wrapped");

        Assert.IsTrue(UpdatePackage.TryVerifyAndExtract(zip, signature, targetDir, _publicKeyPem, out var payloadDir, out _));
        Assert.AreEqual(Path.Combine(targetDir, "Lertaro"), payloadDir);
        Assert.IsTrue(File.Exists(Path.Combine(payloadDir!, "Lertaro.App.exe")));
    }

    [TestMethod]
    public void TryVerifyAndExtract_ZipModifiedAfterSigning_UnpacksNothing()
    {
        var (zip, signature) = Read(CreateStagedPackage(tamperAfterSigning: true));
        var targetDir = Path.Combine(_root, "unpacked-tampered");

        Assert.IsFalse(UpdatePackage.TryVerifyAndExtract(zip, signature, targetDir, _publicKeyPem, out var payloadDir, out var error));
        Assert.IsNull(payloadDir);
        StringAssert.Contains(error!, "signature");
        Assert.IsFalse(Directory.Exists(targetDir));
    }

    [TestMethod]
    public void TryVerifyAndExtract_ZipSwappedOnDiskAfterTheRead_UnpacksWhatWasVerified()
    {
        // The swap an unprivileged process would attempt between the verdict and the copy: once the bytes are
        // read, the staging files no longer matter.
        var stagingDir = CreateStagedPackage();
        var (zip, signature) = Read(stagingDir);
        File.WriteAllText(Path.Combine(stagingDir, UpdatePackage.ZipFileName), "swapped");
        var targetDir = Path.Combine(_root, "unpacked-after-swap");

        Assert.IsTrue(UpdatePackage.TryVerifyAndExtract(zip, signature, targetDir, _publicKeyPem, out var payloadDir, out _));
        Assert.AreEqual("not really an executable", File.ReadAllText(Path.Combine(payloadDir!, "Lertaro.App.exe")));
    }

    [TestMethod]
    public void TryVerifyAndExtract_SelfSignedPackage_IsRefusedByTheShippedKey()
    {
        // The overload the service actually calls: whatever a test happens to accept, the real trust anchor
        // is the one that has to refuse a package signed by someone else.
        var (zip, signature) = Read(CreateStagedPackage());

        Assert.IsFalse(UpdatePackage.TryVerifyAndExtract(zip, signature, Path.Combine(_root, "unpacked-shipped-key"), out _, out _));
    }

    private static (byte[] Zip, byte[] Signature) Read(string stagingDir)
    {
        Assert.IsTrue(UpdatePackage.TryReadStagedPackage(stagingDir, out var zip, out var signature, out var error), error);
        return (zip!, signature!);
    }

    [TestMethod]
    public void ResolvePayloadRoot_SoleNonLertaroFolder_KeepsExtractionRoot()
    {
        var extractPath = Path.Combine(_root, "layout");
        Directory.CreateDirectory(Path.Combine(extractPath, "NotLertaro"));

        Assert.AreEqual(extractPath, UpdatePackage.ResolvePayloadRoot(extractPath));
    }

    [TestMethod]
    public void ResolvePayloadRoot_LertaroFolderWithASibling_KeepsExtractionRoot()
    {
        // A lone "Lertaro" folder is the wrapped layout; alongside anything else it is just a directory the
        // release happens to have, and descending into it would copy the wrong half of the package.
        var extractPath = Path.Combine(_root, "layout-siblings");
        Directory.CreateDirectory(Path.Combine(extractPath, "Lertaro"));
        Directory.CreateDirectory(Path.Combine(extractPath, "Other"));

        Assert.AreEqual(extractPath, UpdatePackage.ResolvePayloadRoot(extractPath));
    }
}
