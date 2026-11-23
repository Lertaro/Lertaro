using Lertaro.Core.Services.LocalSend;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public sealed class LocalSendPrepareUploadRequestParserTests
{
    [TestMethod]
    public void TryParse_TotalFileSizesOverflow_ReturnsFalse()
    {
        const string body = """
        {"info":{"alias":"Sender"},"files":{
          "a":{"id":"a","fileName":"a","size":9223372036854775807,"fileType":"text"},
          "b":{"id":"b","fileName":"b","size":1,"fileType":"text"}}}
        """;
        Assert.IsFalse(LocalSendPrepareUploadRequestParser.TryParse(body, out var request));
        Assert.IsNull(request);
    }
    [TestMethod]
    public void TryParse_MissingRequiredFileField_ReturnsFalse()
    {
        const string body = """
        {"info":{"alias":"Sender"},"files":{"file":{"id":"file","fileName":"test.txt","size":1}}}
        """;

        var parsed = LocalSendPrepareUploadRequestParser.TryParse(body, out var request);

        Assert.IsFalse(parsed);
        Assert.IsNull(request);
    }

    [TestMethod]
    public void TryParse_OfficialRequiredFields_ReturnsRequest()
    {
        const string body = """
        {"info":{"alias":"Sender"},"files":{"file":{"id":"file","fileName":"test.txt","size":1,"fileType":"text"}}}
        """;

        var parsed = LocalSendPrepareUploadRequestParser.TryParse(body, out var request);

        Assert.IsTrue(parsed);
        Assert.IsNotNull(request);
        Assert.HasCount(1, request.Files);
    }

    [TestMethod]
    public void TryParse_NegativeFileSize_ReturnsFalse()
    {
        const string body = """
        {"info":{"alias":"Sender"},"files":{"file":{"id":"file","fileName":"test.txt","size":-1,"fileType":"text"}}}
        """;

        var parsed = LocalSendPrepareUploadRequestParser.TryParse(body, out var request);

        Assert.IsFalse(parsed);
        Assert.IsNull(request);
    }
}
