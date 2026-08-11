using Lertaro.Core.Services.LocalSend;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public sealed class LocalSendCertificateTests
{
    [TestMethod]
    public void CreateEphemeral_UsesPracticallyNonExpiringValidity()
    {
        using var certificate = LocalSendCertificate.CreateEphemeral();

        Assert.AreEqual(1975, certificate.NotBefore.ToUniversalTime().Year);
        Assert.IsGreaterThanOrEqualTo(certificate.NotAfter.ToUniversalTime().Year, 4095);
        Assert.IsFalse(LocalSendCertificate.NeedsRenewal(certificate, DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public void NeedsRenewal_BeforeValidityOrNearExpiration_ReturnsTrue()
    {
        using var certificate = LocalSendCertificate.CreateEphemeral();
        var notBefore = new DateTimeOffset(certificate.NotBefore.ToUniversalTime());
        var nearExpiration = new DateTimeOffset(certificate.NotAfter.ToUniversalTime()).AddDays(-20);

        Assert.IsTrue(LocalSendCertificate.NeedsRenewal(certificate, notBefore.AddSeconds(-1)));
        Assert.IsTrue(LocalSendCertificate.NeedsRenewal(certificate, nearExpiration));
    }

    [TestMethod]
    public void LoadOrCreate_ReusesThePersistentCertificateFingerprint()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Lertaro.LocalSend.Tests." + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "localsend.pfx");
        try
        {
            using var first = LocalSendCertificate.LoadOrCreate(path);
            using var second = LocalSendCertificate.LoadOrCreate(path);

            Assert.AreEqual(LocalSendCertificate.GetFingerprint(first), LocalSendCertificate.GetFingerprint(second));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void LoadOrCreate_DifferentUserPathsCreateDifferentIdentities()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Lertaro.LocalSend.Tests." + Guid.NewGuid().ToString("N"));
        try
        {
            using var first = LocalSendCertificate.LoadOrCreate(Path.Combine(directory, "user-a", "localsend.pfx"));
            using var second = LocalSendCertificate.LoadOrCreate(Path.Combine(directory, "user-b", "localsend.pfx"));

            Assert.AreNotEqual(LocalSendCertificate.GetFingerprint(first), LocalSendCertificate.GetFingerprint(second));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
