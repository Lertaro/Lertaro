using System.Text;
using Lertaro.Core.Services.LocalSend;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public sealed class ProgressiveStreamContentTests
{
    [TestMethod]
    public async Task ReadAsStreamAsync_ReportsProgress()
    {
        var testData = Encoding.UTF8.GetBytes("Hello Progressive Stream Content");
        using var ms = new MemoryStream(testData);

        long lastRead = 0;
        long totalLength = 0;
        var content = new ProgressiveStreamContent(ms, (read, total) =>
        {
            lastRead = read;
            totalLength = total;
        });

        using var outMs = new MemoryStream();
        await content.CopyToAsync(outMs);

        Assert.AreEqual(testData.Length, lastRead);
        Assert.AreEqual(testData.Length, totalLength);
        Assert.AreEqual(Encoding.UTF8.GetString(testData), Encoding.UTF8.GetString(outMs.ToArray()));
    }
}
