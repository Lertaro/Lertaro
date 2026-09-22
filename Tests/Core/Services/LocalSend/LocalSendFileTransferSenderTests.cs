using System.Net;
using System.Net.Sockets;
using Lertaro.Core.Services.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public sealed class LocalSendFileTransferSenderTests
{
    [TestMethod]
    public async Task UploadWithSenderCancellationAsync_NotifiesReceiverBeforeAbortingUpload()
    {
        using var userCancellation = new CancellationTokenSource();
        var uploadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiverNotified = false;
        var uploadObservedNotification = false;

        var resultTask = LocalSendFileTransferSender.UploadWithSenderCancellationAsync(async uploadToken =>
        {
            uploadStarted.SetResult();
            await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => Task.Delay(Timeout.Infinite, uploadToken));
            uploadObservedNotification = receiverNotified;
            return new LocalSendFileTransferAttempt(LocalSendSendResult.Canceled, null, false);
        }, () =>
        {
            receiverNotified = true;
            return Task.CompletedTask;
        }, userCancellation.Token);

        await uploadStarted.Task;
        userCancellation.Cancel();
        var result = await resultTask;

        Assert.IsTrue(uploadObservedNotification);
        Assert.AreEqual(LocalSendSendResult.Canceled, result.Result);
    }

    [TestMethod]
    public async Task UploadWithSenderCancellationAsync_CompletedUploadDoesNotNotifyReceiver()
    {
        using var userCancellation = new CancellationTokenSource();
        var receiverNotified = false;

        var result = await LocalSendFileTransferSender.UploadWithSenderCancellationAsync(
            _ => Task.FromResult(new LocalSendFileTransferAttempt(LocalSendSendResult.Success, null, false)),
            () =>
            {
                receiverNotified = true;
                return Task.CompletedTask;
            }, userCancellation.Token);

        Assert.IsFalse(receiverNotified);
        Assert.AreEqual(LocalSendSendResult.Success, result.Result);
    }

    [TestMethod]
    public async Task UploadAsync_WhenThePeerAcceptsAndNeverAnswers_EndsAsAStall()
    {
        // The send client's own timeout is infinite, so without the inactivity deadline this request
        // would still be pending when the test run gave up. The stub reads nothing and never responds.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var heldOpen = new List<TcpClient>();
        using var listening = new CancellationTokenSource();
        var accepting = Task.Run(async () =>
        {
            try
            {
                while (!listening.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(listening.Token);
                    lock (heldOpen) heldOpen.Add(client);
                }
            }
            catch (OperationCanceledException) { }
        });

        try
        {
            var transfer = new LocalSendPendingFileTransfer
            {
                TargetIp = "127.0.0.1",
                TargetPort = port,
                Https = false,
                SessionId = "session",
                TargetVersion = "2.2",
                Files =
                [
                    new LocalSendPendingFile("file",
                        new LocalSendFileDto { Id = "file", FileName = "note.txt", Size = 4 },
                        () => new MemoryStream("data"u8.ToArray()))
                ],
                Tokens = new Dictionary<string, string> { ["file"] = "token" }
            };
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

            var attempt = await LocalSendFileTransferSender.UploadAsync(http, server: null, transfer,
                onProgress: null, onFileConfirmed: null, CancellationToken.None, TimeSpan.FromMilliseconds(250));

            Assert.AreEqual(LocalSendSendResult.Error, attempt.Result);
            StringAssert.Contains(attempt.Error ?? string.Empty, "stopped accepting",
                "the stall deadline, not a connection error, is what ended the request");
            Assert.IsTrue(attempt.CanRetry, "a peer that stopped responding is a transient failure");
        }
        finally
        {
            listening.Cancel();
            listener.Stop();
            lock (heldOpen) foreach (var client in heldOpen) client.Dispose();
        }
    }
}
