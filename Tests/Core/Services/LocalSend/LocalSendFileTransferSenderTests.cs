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
}
