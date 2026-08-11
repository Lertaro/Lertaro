using Lertaro.Core.Services.LocalSend;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public sealed class LocalSendChecksumTests
{
    [TestMethod]
    public void Compute_UsesLowercaseSha256() => Assert.AreEqual(
            "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
            LocalSendChecksum.Compute("hello"u8));

    [TestMethod]
    public async Task ComputeFileAsync_MatchesByteHash()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, "file content"u8.ToArray());

            var hash = await LocalSendChecksum.ComputeFileAsync(path, CancellationToken.None);

            Assert.AreEqual(LocalSendChecksum.Compute("file content"u8), hash);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
