using System.Collections.Concurrent;
using Lertaro.Core.Services.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public sealed class LocalSendUploadAuthorizationCheckerTests
{
    [TestMethod]
    public async Task UploadAsync_ProcessesTwoFilesConcurrently()
    {
        var firstPath = Path.GetTempFileName();
        var secondPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(firstPath, "first");
            await File.WriteAllTextAsync(secondPath, "second");
            using var handler = new DelayedSuccessHandler();
            using var client = new HttpClient(handler);
            var transfer = CreateTransfer(firstPath, secondPath);

            var responses = new ConcurrentBag<LocalSendFileConfirmationArgs>();
            var result = await LocalSendFileTransferSender.UploadAsync(client, null, transfer, null, responses.Add, CancellationToken.None);

            Assert.AreEqual(LocalSendSendResult.Success, result.Result);
            Assert.IsGreaterThanOrEqualTo(handler.MaxConcurrentRequests, 2);
            Assert.HasCount(2, responses);
            Assert.IsTrue(responses.All(response => response.Result == LocalSendSendResult.Success));
        }
        finally
        {
            File.Delete(firstPath);
            File.Delete(secondPath);
        }
    }

    [TestMethod]
    public void PendingTransfer_RetriesOnlyPreviouslyFailedFiles()
    {
        var transfer = CreateTransfer("first", "second");
        transfer.MarkFileFailed("second");

        var files = transfer.GetFilesForAttempt();

        Assert.HasCount(1, files);
        Assert.AreEqual("second", files[0].Id);
    }

    [TestMethod]
    public void ClassifyFailure_ServerErrorCanBeRetriedAsRemoteError()
    {
        var attempt = LocalSendFileTransferSender.ClassifyFailure(System.Net.HttpStatusCode.InternalServerError, "Internal Server Error");

        Assert.AreEqual(LocalSendSendResult.RemoteError, attempt.Result);
        Assert.IsTrue(attempt.CanRetry);
    }

    [TestMethod]
    public void ClassifyFailure_ChecksumMismatchIsTerminal()
    {
        var attempt = LocalSendFileTransferSender.ClassifyFailure((System.Net.HttpStatusCode)422, "Unprocessable Entity");

        Assert.AreEqual(LocalSendSendResult.Error, attempt.Result);
        Assert.IsFalse(attempt.CanRetry);
        StringAssert.Contains(attempt.Error, "Checksum mismatch");
    }

    [TestMethod]
    public void GetCancellationResult_DistinguishesReceiverCancellation()
    {
        using var userCancellation = new CancellationTokenSource();

        var receiverResult = LocalSendFileTransferSender.GetCancellationResult(userCancellation.Token);
        userCancellation.Cancel();
        var userResult = LocalSendFileTransferSender.GetCancellationResult(userCancellation.Token);

        Assert.AreEqual(LocalSendSendResult.ReceiverCanceled, receiverResult);
        Assert.AreEqual(LocalSendSendResult.Canceled, userResult);
    }

    [TestMethod]
    public async Task UploadAsync_ServerErrorReturnsRemoteErrorAndCanBeRetried()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "test");
            using var client = new HttpClient(new FixedStatusHandler(System.Net.HttpStatusCode.InternalServerError));
            var result = await LocalSendFileTransferSender.UploadAsync(client, null, CreateTransfer(path, path), null, null, CancellationToken.None);

            Assert.AreEqual(LocalSendSendResult.RemoteError, result.Result);
            Assert.IsTrue(result.CanRetry);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task UploadAsync_ChecksumMismatchRetriesEachFileThreeTimes()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "test");
            using var handler = new CountingStatusHandler((System.Net.HttpStatusCode)422);
            using var client = new HttpClient(handler);

            var result = await LocalSendFileTransferSender.UploadAsync(
                client, null, CreateTransfer(path, path), null, null, CancellationToken.None);

            Assert.AreEqual(LocalSendSendResult.Error, result.Result);
            Assert.IsFalse(result.CanRetry);
            Assert.AreEqual(6, handler.RequestCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task UploadAsync_ConflictReturnsRetryableProtocolError()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "test");
            using var client = new HttpClient(new FixedStatusHandler(System.Net.HttpStatusCode.Conflict));
            var result = await LocalSendFileTransferSender.UploadAsync(client, null, CreateTransfer(path, path), null, null, CancellationToken.None);

            Assert.AreEqual(LocalSendSendResult.Error, result.Result);
            Assert.IsTrue(result.CanRetry);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task UploadAsync_BadRequestKeepsTheFileRetryable()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "test");
            using var client = new HttpClient(new FixedStatusHandler(System.Net.HttpStatusCode.BadRequest));
            var transfer = CreateTransfer(path, path);

            var responses = new ConcurrentBag<LocalSendFileConfirmationArgs>();
            var result = await LocalSendFileTransferSender.UploadAsync(client, null, transfer, null, responses.Add, CancellationToken.None);

            Assert.AreEqual(LocalSendSendResult.Error, result.Result);
            Assert.IsTrue(result.CanRetry);
            Assert.IsTrue(transfer.HasFailedFiles);
            Assert.HasCount(2, responses);
            Assert.IsTrue(responses.All(response => response.Result == LocalSendSendResult.Error));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void TryAuthorize_V2UnknownSession_ReturnsProtocolError()
    {
        var authorized = LocalSendUploadAuthorizationChecker.TryAuthorize(new ConcurrentDictionary<string, LocalSendUploadAuthorization>(), "missing", "file", "token", "192.168.1.20", v2: true, out _, out var error);

        Assert.IsFalse(authorized);
        Assert.AreEqual("Invalid token or IP address", error);
    }

    [TestMethod]
    public void TryAuthorize_WrongSender_ReturnsProtocolError()
    {
        var authorizations = new ConcurrentDictionary<string, LocalSendUploadAuthorization>();
        authorizations["session"] = new LocalSendUploadAuthorization("192.168.1.20", new Dictionary<string, string> { ["file"] = "token" });

        var authorized = LocalSendUploadAuthorizationChecker.TryAuthorize(authorizations, "session", "file", "token", "192.168.1.21", v2: true, out _, out var error);

        Assert.IsFalse(authorized);
        Assert.AreEqual("Invalid token or IP address", error);
    }

    private static LocalSendPendingFileTransfer CreateTransfer(string firstPath, string secondPath) => new()
    {
        TargetIp = "192.168.1.20",
        TargetPort = 53317,
        Https = false,
        SessionId = "session",
        TargetVersion = "2.1",
        Tokens = new Dictionary<string, string> { ["first"] = "token-1", ["second"] = "token-2" },
        Files = [
            new LocalSendPendingFile("first", new LocalSendFileDto { Id = "first", FileName = "first.txt" }, firstPath),
            new LocalSendPendingFile("second", new LocalSendFileDto { Id = "second", FileName = "second.txt" }, secondPath)
        ]
    };

    private sealed class DelayedSuccessHandler : HttpMessageHandler
    {
        private int _currentRequests;
        private int _maxConcurrentRequests;

        internal int MaxConcurrentRequests => _maxConcurrentRequests;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref _currentRequests);
            InterlockedExtensions.Max(ref _maxConcurrentRequests, current);
            try
            {
                await Task.Delay(50, cancellationToken);
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            }
            finally
            {
                Interlocked.Decrement(ref _currentRequests);
            }
        }
    }

    private sealed class FixedStatusHandler(System.Net.HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }

    private sealed class CountingStatusHandler(System.Net.HttpStatusCode statusCode) : HttpMessageHandler
    {
        private int _requestCount;

        internal int RequestCount => _requestCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            return Task.FromResult(new HttpResponseMessage(statusCode));
        }
    }

    private static class InterlockedExtensions
    {
        internal static void Max(ref int target, int value)
        {
            while (true)
            {
                var observed = target;
                if (observed >= value || Interlocked.CompareExchange(ref target, value, observed) == observed)
                    return;
            }
        }
    }
}
