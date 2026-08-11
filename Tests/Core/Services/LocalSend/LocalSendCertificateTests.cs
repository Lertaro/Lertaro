using Lertaro.Core.Services.LocalSend;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public sealed class LocalSendCertificateTests
{
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
