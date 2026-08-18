using Lertaro.Core.Services.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public sealed class LocalSendTextMessageHelperTests
{
    [TestMethod]
    public void TryGetMessage_RecognizesLegacyAndMimeTextWithPreview()
    {
        var legacy = CreateTextRequest("text", "hello");
        var mime = CreateTextRequest("text/plain", "hello");

        Assert.IsTrue(LocalSendTextMessageHelper.TryGetMessage(legacy, out var legacyMessage));
        Assert.AreEqual("hello", legacyMessage);
        Assert.IsTrue(LocalSendTextMessageHelper.TryGetMessage(mime, out var mimeMessage));
        Assert.AreEqual("hello", mimeMessage);
    }

    [TestMethod]
    public void TryGetMessage_RejectsFilesWithoutPreviewOrWithMultipleFiles()
    {
        var fileUpload = CreateTextRequest("text/plain", null);
        var multiple = CreateTextRequest("text/plain", "hello");
        multiple.Files["second"] = new LocalSendFileDto { Id = "second", FileName = "second.txt", FileType = "text/plain", Preview = "world" };

        Assert.IsFalse(LocalSendTextMessageHelper.TryGetMessage(fileUpload, out _));
        Assert.IsFalse(LocalSendTextMessageHelper.TryGetMessage(multiple, out _));
    }

    private static PrepareUploadRequestDto CreateTextRequest(string fileType, string? preview) => new()
    {
        Files = new Dictionary<string, LocalSendFileDto>
        {
            ["text"] = new LocalSendFileDto { Id = "text", FileName = "message.txt", FileType = fileType, Preview = preview }
        }
    };
}
