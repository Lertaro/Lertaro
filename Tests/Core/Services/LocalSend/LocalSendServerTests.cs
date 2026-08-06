using Lertaro.Core.Services.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
[DoNotParallelize]
public sealed class LocalSendServerTests
{
    [TestInitialize]
    public void Initialize() => LocalSendServiceManager.Instance.WindowOpenCheck = null;

    [TestCleanup]
    public void Cleanup() => LocalSendServiceManager.Instance.WindowOpenCheck = null;

    [TestMethod]
    public void IsBusy_TracksAnyOpenLocalSendWindowOrIncomingSession()
    {
        LocalSendServiceManager.Instance.WindowOpenCheck = () => true;
        var server = new LocalSendServer();

        Assert.IsTrue(server.IsBusy);
        LocalSendServiceManager.Instance.WindowOpenCheck = () => false;
        server.RegisterActiveSession("session", new PrepareUploadRequestDto());
        Assert.IsTrue(server.IsBusy);
    }

    [TestMethod]
    public async Task HandleUploadAsync_WhenFileCannotBeSaved_ReturnsErrorAndKeepsSessionForRetry()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"Lertaro.LocalSend.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(temporaryDirectory, "destination"));
        try
        {
            var server = new LocalSendServer { DownloadDirectory = temporaryDirectory };
            var request = new PrepareUploadRequestDto
            {
                Files = new Dictionary<string, LocalSendFileDto>
                {
                    ["file"] = new() { Id = "file", FileName = "destination", Size = 4 }
                }
            };
            server.RegisterActiveSession("session", request);
            server.RegisterUploadAuthorization("session", "192.168.1.20", new Dictionary<string, string> { ["file"] = "token" });

            await using var response = new MemoryStream();
            await using var body = new MemoryStream("data"u8.ToArray());
            await server.HandleUploadAsync(response, body, "session", "file", "token", "192.168.1.20", v2: true);

            StringAssert.Contains(System.Text.Encoding.UTF8.GetString(response.ToArray()), "HTTP/1.1 500");
            Assert.IsTrue(server.HasActiveSessions);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
