using System.Text;
using Lertaro.Core.Services.LocalSend;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public sealed class LengthLimitedStreamTests
{
    [TestMethod]
    public async Task ReadAsync_LimitsBytesToDeclaredLimit()
    {
        var data = Encoding.UTF8.GetBytes("1234567890EXTRA_BYTES");
        using var ms = new MemoryStream(data);
        using var limited = new LengthLimitedStream(ms, 10);

        using var outMs = new MemoryStream();
        await limited.CopyToAsync(outMs);

        Assert.AreEqual(10, outMs.Length);
        Assert.AreEqual("1234567890", Encoding.UTF8.GetString(outMs.ToArray()));
    }
}
