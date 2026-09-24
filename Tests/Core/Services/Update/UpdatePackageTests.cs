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
    public void TryVerifyAndExtract_FlatPackage_UnpacksAndReturnsTargetRoot()
    {
        var stagingDir = CreateStagedPackage();
        var targetDir = Path.Combine(_root, "unpacked-flat");

        Assert.IsTrue(UpdatePackage.TryVerifyAndExtract(stagingDir, targetDir, _publicKeyPem, out var payloadDir, out var error));
        Assert.AreEqual(targetDir, payloadDir);
        Assert.IsNull(error);
        Assert.IsTrue(File.Exists(Path.Combine(payloadDir!, "Lertaro.App.exe")));
        Assert.IsTrue(File.Exists(Path.Combine(payloadDir!, "Plugins", "one.dll")));
    }

    [TestMethod]
    public void TryVerifyAndExtract_PackageWrappedInLertaroFolder_ReturnsTheInnerFolder()
    {
        var stagingDir = CreateStagedPackage(wrapInLertaroFolder: true);
        var targetDir = Path.Combine(_root, "unpacked-wrapped");

        Assert.IsTrue(UpdatePackage.TryVerifyAndExtract(stagingDir, targetDir, _publicKeyPem, out var payloadDir, out var error));
        Assert.AreEqual(Path.Combine(targetDir, "Lertaro"), payloadDir);
        Assert.IsTrue(File.Exists(Path.Combine(payloadDir!, "Lertaro.App.exe")));
    }

    [TestMethod]
    public void TryVerifyAndExtract_UnsignedPackage_UnpacksNothing()
    {
        var stagingDir = CreateStagedPackage();
        RenameSignatureOutOfPlace(stagingDir);
        var targetDir = Path.Combine(_root, "unpacked-unsigned");

        Assert.IsFalse(UpdatePackage.TryVerifyAndExtract(stagingDir, targetDir, _publicKeyPem, out var payloadDir, out _));
        Assert.IsNull(payloadDir);
        Assert.IsFalse(Directory.Exists(targetDir));
    }

    [TestMethod]
    public void TryVerifyAndExtract_ZipSwappedAfterSigning_UnpacksNothing()
    {
        var stagingDir = CreateStagedPackage(tamperAfterSigning: true);
        var targetDir = Path.Combine(_root, "unpacked-tampered");

        Assert.IsFalse(UpdatePackage.TryVerifyAndExtract(stagingDir, targetDir, _publicKeyPem, out var payloadDir, out var error));
        Assert.IsNull(payloadDir);
        StringAssert.Contains(error!, "signature");
        Assert.IsFalse(Directory.Exists(targetDir));
    }

    [TestMethod]
    public void TryVerifyAndExtract_SelfSignedPackage_IsRefusedByTheShippedKey()
    {
        // The overload the service actually calls: whatever a test happens to accept, the real trust anchor
        // is the one that has to refuse a package signed by someone else.
        var stagingDir = CreateStagedPackage();

        Assert.IsFalse(UpdatePackage.TryVerifyAndExtract(stagingDir, Path.Combine(_root, "unpacked-shipped-key"), out _, out _));
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
