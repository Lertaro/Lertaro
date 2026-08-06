using Lertaro.Core.Services.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public class LocalSendClientTests
{
    [TestMethod]
    public async Task SendFilesAsync_EmptyFiles_ReturnsError()
    {
        using var client = new LocalSendClient();
        var senderInfo = new LocalSendDeviceInfo { Alias = "TestSender" };

        var result = await client.SendFilesAsync("127.0.0.1", 53317, false, senderInfo, Array.Empty<string>());

        Assert.AreEqual(LocalSendSendResult.Error, result);
    }

    [TestMethod]
    public async Task GetDeviceInfoAsync_InvalidPort_ReturnsNull()
    {
        using var client = new LocalSendClient();
        using var cts = new CancellationTokenSource(500);

        var device = await client.GetDeviceInfoAsync("127.0.0.1", 59999, false, cts.Token);

        Assert.IsNull(device);
    }
}
