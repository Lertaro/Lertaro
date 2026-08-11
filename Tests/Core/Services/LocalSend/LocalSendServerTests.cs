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
        Assert.IsTrue(server.TryRegisterActiveSession("session", new PrepareUploadRequestDto()));
        Assert.IsTrue(server.IsBusy);
    }

    [TestMethod]
    public void TryRegisterActiveSession_ConcurrentClaimsAllowOnlyOneSession()
    {
        var server = new LocalSendServer();
        var accepted = 0;

        Parallel.For(0, 32, index =>
        {
            if (server.TryRegisterActiveSession($"session-{index}", new PrepareUploadRequestDto()))
                Interlocked.Increment(ref accepted);
        });

        Assert.AreEqual(1, accepted);
        Assert.HasCount(1, server.GetActiveSessions());
    }

    [TestMethod]
    public async Task HandleUploadAsync_WhenFileCannotBeSaved_ReturnsErrorAndEndsSession()
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
            LocalSendProgressArgs? failure = null;
            server.ProgressChanged += (_, args) => { if (args.IsFailed) failure = args; };
            Assert.IsTrue(server.TryRegisterActiveSession("session", request));
            server.RegisterUploadAuthorization("session", "192.168.1.20", new Dictionary<string, string> { ["file"] = "token" });

            await using var response = new MemoryStream();
            await using var body = new MemoryStream("data"u8.ToArray());
            await server.HandleUploadAsync(response, body, "session", "file", "token", "192.168.1.20", v2: true);

            StringAssert.Contains(System.Text.Encoding.UTF8.GetString(response.ToArray()), "HTTP/1.1 500");
            Assert.IsNotNull(failure);
            Assert.IsTrue(failure.IsAllDone);
            Assert.IsFalse(failure.IsFinished);
            Assert.IsFalse(server.HasActiveSessions);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task HandleUploadAsync_ChecksumMismatch_Returns422AndKeepsSessionForRetry()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"Lertaro.LocalSend.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var server = new LocalSendServer { DownloadDirectory = temporaryDirectory };
            var request = new PrepareUploadRequestDto
            {
                Files = new Dictionary<string, LocalSendFileDto>
                {
                    ["file"] = new()
                    {
                        Id = "file", FileName = "payload.bin", Size = 4, Sha256 = new string('0', 64)
                    }
                }
            };
            LocalSendProgressArgs? failure = null;
            var verificationReported = false;
            server.ProgressChanged += (_, args) =>
            {
                if (args.IsFailed) failure = args;
                verificationReported |= args.Stage == LocalSendTransferStage.VerifyingChecksum;
            };
            Assert.IsTrue(server.TryRegisterActiveSession("session", request));
            server.RegisterUploadAuthorization("session", "192.168.1.20", new Dictionary<string, string> { ["file"] = "token" });

            await using var response = new MemoryStream();
            await using var body = new MemoryStream("data"u8.ToArray());
            await server.HandleUploadAsync(response, body, "session", "file", "token", "192.168.1.20", v2: true);

            StringAssert.Contains(System.Text.Encoding.UTF8.GetString(response.ToArray()), "HTTP/1.1 422 Unprocessable Entity");
            Assert.IsNotNull(failure);
            Assert.IsFalse(failure.IsAllDone);
            Assert.IsTrue(verificationReported);
            Assert.IsTrue(server.HasActiveSessions);
            Assert.IsFalse(File.Exists(Path.Combine(temporaryDirectory, "payload.bin")));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task HandleUploadAsync_ThirdChecksumMismatchEndsSession()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"Lertaro.LocalSend.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var server = new LocalSendServer { DownloadDirectory = temporaryDirectory };
            LocalSendProgressArgs? failure = null;
            server.ProgressChanged += (_, args) => { if (args.IsFailed) failure = args; };
            Assert.IsTrue(server.TryRegisterActiveSession("session", new PrepareUploadRequestDto
            {
                Files = new Dictionary<string, LocalSendFileDto>
                {
                    ["file"] = new() { Id = "file", FileName = "payload.bin", Size = 4, Sha256 = new string('0', 64) }
                }
            }));
            server.RegisterUploadAuthorization("session", "192.168.1.20", new Dictionary<string, string> { ["file"] = "token" });

            for (var attempt = 1; attempt <= 3; attempt++)
            {
                await using var response = new MemoryStream();
                await using var body = new MemoryStream("data"u8.ToArray());
                await server.HandleUploadAsync(response, body, "session", "file", "token", "192.168.1.20", v2: true);
                StringAssert.Contains(System.Text.Encoding.UTF8.GetString(response.ToArray()), "HTTP/1.1 422 Unprocessable Entity");
            }

            Assert.IsNotNull(failure);
            Assert.IsTrue(failure.IsAllDone);
            Assert.IsFalse(server.HasActiveSessions);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task HandleUploadAsync_VerificationDisabled_AcceptsChecksumMismatch()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"Lertaro.LocalSend.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var server = new LocalSendServer { DownloadDirectory = temporaryDirectory, VerifyChecksums = false };
            Assert.IsTrue(server.TryRegisterActiveSession("session", new PrepareUploadRequestDto
            {
                Files = new Dictionary<string, LocalSendFileDto>
                {
                    ["file"] = new() { Id = "file", FileName = "payload.bin", Size = 4, Sha256 = new string('0', 64) }
                }
            }));
            server.RegisterUploadAuthorization("session", "192.168.1.20", new Dictionary<string, string> { ["file"] = "token" });

            await using var response = new MemoryStream();
            await using var body = new MemoryStream("data"u8.ToArray());
            await server.HandleUploadAsync(response, body, "session", "file", "token", "192.168.1.20", v2: true);

            StringAssert.Contains(System.Text.Encoding.UTF8.GetString(response.ToArray()), "HTTP/1.1 200 OK");
            Assert.IsTrue(File.Exists(Path.Combine(temporaryDirectory, "payload.bin")));
            Assert.IsFalse(server.HasActiveSessions);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task HandleUploadAsync_WhenReceiverCancelsDuringWrite_DoesNotReportFailure()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"Lertaro.LocalSend.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var server = new LocalSendServer { DownloadDirectory = temporaryDirectory };
            Assert.IsTrue(server.TryRegisterActiveSession("session", new PrepareUploadRequestDto
            {
                Files = new Dictionary<string, LocalSendFileDto>
                {
                    ["file"] = new() { Id = "file", FileName = "payload.bin", Size = 4 }
                }
            }));
            server.RegisterUploadAuthorization("session", "192.168.1.20", new Dictionary<string, string> { ["file"] = "token" });
            var failureReported = false;
            server.ProgressChanged += (_, args) => failureReported |= args.IsFailed;

            await using var response = new MemoryStream();
            await using var body = new CancelingReadStream("data"u8.ToArray(), () => server.CancelSession("session"));
            await server.HandleUploadAsync(response, body, "session", "file", "token", "192.168.1.20", v2: true);

            Assert.IsFalse(failureReported);
            Assert.IsFalse(server.HasActiveSessions);
            StringAssert.Contains(System.Text.Encoding.UTF8.GetString(response.ToArray()), "HTTP/1.1 200 OK");
            Assert.IsTrue(File.Exists(Path.Combine(temporaryDirectory, "payload.bin")));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private sealed class CancelingReadStream(byte[] data, Action cancel) : MemoryStream(data)
    {
        private bool _canceled;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken);
            if (!_canceled) { _canceled = true; cancel(); }
            return read;
        }
    }
}
