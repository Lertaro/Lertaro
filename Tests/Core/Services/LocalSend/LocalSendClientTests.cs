using Lertaro.Core.Services.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;
using System.Net;
using System.Net.Sockets;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public class LocalSendClientTests
{
    [TestMethod]
    public async Task SendFilesAsync_MissingSelectedItem_DoesNotSilentlySendTheRest()
    {
        var directory = Directory.CreateTempSubdirectory("localsend-selection-");
        try
        {
            var valid = Path.Combine(directory.FullName, "present.txt");
            var missing = Path.Combine(directory.FullName, "missing.txt");
            File.WriteAllText(valid, "data");
            using var client = new LocalSendClient(createChecksums: false);
            var result = await client.SendFilesAsync("127.0.0.1", 1, false, new LocalSendDeviceInfo(), [valid, missing]);
            Assert.AreEqual(LocalSendSendResult.Error, result);
            Assert.Contains("unavailable", client.LastError!);
        }
        finally { directory.Delete(true); }
    }

    [TestMethod]
    public async Task SendFilesAsync_EmptyFiles_ReturnsError()
    {
        using var client = new LocalSendClient();
        var senderInfo = new LocalSendDeviceInfo { Alias = "TestSender" };

        var result = await client.SendFilesAsync("127.0.0.1", 53317, false, senderInfo, Array.Empty<string>());

        Assert.AreEqual(LocalSendSendResult.Error, result);
    }

    [TestMethod]
    public async Task SendTextAsync_LargeUnicodeMessage_TransfersFileWithoutInlinePreview()
    {
        var directory = Directory.CreateTempSubdirectory("localsend-large-text-");
        using var receiver = new LocalSendServer { DownloadDirectory = directory.FullName, QuickSave = true };
        using var sender = new LocalSendServer { IdentityCertificate = LocalSendCertificate.CreateEphemeral() };
        var preview = "not requested";
        receiver.UploadRequested += (_, args) => { preview = args.Dto.Files.Values.Single().Preview; args.Respond(true); };
        receiver.Start(GetFreePort());
        try
        {
            using var client = new LocalSendClient(sender);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var text = new string('汉', 6000);
            var result = await client.SendTextAsync("127.0.0.1", receiver.ActualPort, false,
                new LocalSendDeviceInfo { Alias = "Sender" }, text, token: deadline.Token, targetVersion: "2.2");

            Assert.AreEqual(LocalSendSendResult.Success, result, client.LastError);
            Assert.IsNull(preview);
            var file = Assert.ContainsSingle(Directory.GetFiles(directory.FullName));
            Assert.AreEqual(text, await File.ReadAllTextAsync(file, deadline.Token));
        }
        finally { receiver.Stop(); directory.Delete(true); }
    }

    [TestMethod]
    public async Task GetDeviceInfoAsync_InvalidPort_ReturnsNull()
    {
        using var client = new LocalSendClient();
        using var cts = new CancellationTokenSource(500);

        var device = await client.GetDeviceInfoAsync("127.0.0.1", 59999, false, cts.Token);

        Assert.IsNull(device);
    }

    [TestMethod]
    public async Task SendTextAsync_ReceiverAcceptsMessage_ReturnsSuccessWithoutWritingFile()
    {
        var downloadDirectory = Path.Combine(Path.GetTempPath(), $"lertaro-text-{Guid.NewGuid():N}");
        Directory.CreateDirectory(downloadDirectory);
        using var receiver = new LocalSendServer { DownloadDirectory = downloadDirectory, QuickSave = true };
        receiver.UploadRequested += (_, args) => args.Respond(true);
        using var sender = new LocalSendServer { IdentityCertificate = LocalSendCertificate.CreateEphemeral() };
        var port = GetFreePort();
        receiver.Start(port);
        try
        {
            using var client = new LocalSendClient(sender);
            var senderInfo = new LocalSendDeviceInfo { Alias = "Sender", IpAddress = "127.0.0.1" };

            var result = await client.SendTextAsync("127.0.0.1", port, false, senderInfo, "hello", targetVersion: "2.2");

            Assert.AreEqual(LocalSendSendResult.Success, result);
            Assert.IsFalse(Directory.EnumerateFiles(downloadDirectory).Any());
        }
        finally
        {
            receiver.Stop();
            Directory.Delete(downloadDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void BuildCancellationUrl_PendingV2RequestOmitsUnknownSessionId()
    {
        var url = LocalSendClient.BuildCancellationUrl("192.168.1.20", 53317, false, string.Empty, "2.2");

        Assert.AreEqual("http://192.168.1.20:53317/api/localsend/v2/cancel", url);
    }

    [TestMethod]
    public void BuildCancellationUrl_ActiveV2RequestIncludesSessionId()
    {
        var url = LocalSendClient.BuildCancellationUrl("192.168.1.20", 53317, false, "session id", "2.2");

        Assert.AreEqual("http://192.168.1.20:53317/api/localsend/v2/cancel?sessionId=session%20id", url);
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
