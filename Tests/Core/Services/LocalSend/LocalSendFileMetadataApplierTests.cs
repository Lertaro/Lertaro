using Lertaro.Core.Services.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public sealed class LocalSendFileMetadataApplierTests
{
    [TestMethod]
    public void Apply_LastModified_RestoresTheTransferredTimestamp()
    {
        var path = Path.GetTempFileName();
        var expected = DateTimeOffset.Parse("2024-01-02T03:04:05Z");
        try
        {
            LocalSendFileMetadataApplier.Apply(path, new LocalSendFileMetadataDto { LastModified = expected.UtcDateTime });

            Assert.AreEqual(expected.UtcDateTime, File.GetLastWriteTimeUtc(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
