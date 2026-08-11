using Lertaro.Core.Services.LocalSend;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public sealed class LocalSendIncomingFileWriterTests
{
    [TestMethod]
    public async Task SaveAsync_ExactSizeAndChecksum_Succeeds()
    {
        var data = "payload"u8.ToArray();
        var path = Path.GetTempFileName();
        try
        {
            await using var source = new MemoryStream(data);
            var result = await LocalSendIncomingFileWriter.SaveAsync(
                source, path, data.Length, LocalSendChecksum.Compute(data), () => false);

            Assert.AreEqual(LocalSendFileSaveStatus.Success, result.Status);
            CollectionAssert.AreEqual(data, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task SaveAsync_TruncatedBody_ReturnsSizeMismatch()
    {
        var path = Path.GetTempFileName();
        try
        {
            await using var source = new MemoryStream("short"u8.ToArray());
            var result = await LocalSendIncomingFileWriter.SaveAsync(source, path, 10, null, () => false);

            Assert.AreEqual(LocalSendFileSaveStatus.SizeMismatch, result.Status);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task SaveAsync_WrongChecksum_ReturnsChecksumMismatch()
    {
        var data = "payload"u8.ToArray();
        var path = Path.GetTempFileName();
        try
        {
            await using var source = new MemoryStream(data);
            var result = await LocalSendIncomingFileWriter.SaveAsync(
                source, path, data.Length, new string('0', 64), () => false);

            Assert.AreEqual(LocalSendFileSaveStatus.ChecksumMismatch, result.Status);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task SaveAsync_CanceledDuringChecksum_ReturnsCanceled()
    {
        var data = "payload"u8.ToArray();
        var path = Path.GetTempFileName();
        try
        {
            var canceled = false;
            await using var source = new MemoryStream(data);
            var result = await LocalSendIncomingFileWriter.SaveAsync(source, path, data.Length,
                LocalSendChecksum.Compute(data), () => canceled, onChecksumProgress: bytes => canceled = bytes > 0);

            Assert.AreEqual(LocalSendFileSaveStatus.Canceled, result.Status);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
