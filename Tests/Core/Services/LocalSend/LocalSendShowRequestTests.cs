using Lertaro.Core.Services.LocalSend;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public sealed class LocalSendShowRequestTests
{
    [TestMethod]
    public void ParseFiles_ArgsArray_ReturnsTheRequestedFiles()
    {
        var files = LocalSendShowRequest.ParseFiles("{\"args\":[\"C:\\\\files\\\\one.txt\",\"C:\\\\files\\\\two.txt\"]}");

        CollectionAssert.AreEqual(new[] { "C:\\files\\one.txt", "C:\\files\\two.txt" }, files!.ToArray());
    }

    [TestMethod]
    public void ParseFiles_InvalidPayload_ReturnsNoFiles()
    {
        Assert.IsNull(LocalSendShowRequest.ParseFiles("not-json"));
    }
}
