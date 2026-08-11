using Lertaro.App.ViewModels.LocalSend;
using Lertaro.Core.Services.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;
using System.IO;

namespace Lertaro.App.Tests.ViewModels.LocalSend;

[TestClass]
public sealed class LocalSendSendProgressTrackerTests
{
    [TestMethod]
    public void MarkConfirmed_OnlyCountsTheReceiverConfirmedFile()
    {
        var tracker = new LocalSendSendProgressTracker();
        tracker.PrepareText("hello", "Text");

        tracker.UpdateProgress(new LocalSendSendProgressArgs("Text", 5, 5, 1, 1), "Waiting");
        Assert.AreEqual(0, tracker.ConfirmedCount);
        Assert.AreEqual(99d, tracker.Items[0].ProgressPercentage);

        tracker.MarkConfirmed(new LocalSendFileConfirmationArgs("file", "Text", 1, 1), "Completed");
        Assert.AreEqual(1, tracker.ConfirmedCount);
        Assert.IsTrue(tracker.Items[0].IsConfirmed);
    }

    [TestMethod]
    public void MarkFailed_DoesNotCountTheFileAsConfirmed()
    {
        var tracker = new LocalSendSendProgressTracker();
        tracker.PrepareText("hello", "Text");
        tracker.UpdateProgress(new LocalSendSendProgressArgs("Text", 2, 5, 1, 1), "Waiting");

        tracker.MarkFailed(new LocalSendFileConfirmationArgs("file", "Text", 1, 1, LocalSendSendResult.Error, "HTTP 500"), "Connection error (HTTP 500)");

        Assert.AreEqual(0, tracker.ConfirmedCount);
        Assert.AreEqual(40d, tracker.Items[0].ProgressPercentage);
        Assert.AreEqual("Connection error (HTTP 500)", tracker.Items[0].StatusText);
    }

    [TestMethod]
    public void UpdateProgress_ChecksumStageCanReachOneHundredPercent()
    {
        var tracker = new LocalSendSendProgressTracker();
        tracker.PrepareText("hello", "Text");

        tracker.UpdateProgress(new LocalSendSendProgressArgs("Text", 5, 5, 1, 1,
            LocalSendTransferStage.CalculatingChecksum), "Waiting");

        Assert.AreEqual(100d, tracker.Items[0].ProgressPercentage);
        Assert.AreEqual("100%", tracker.Items[0].StatusText);
    }

    [TestMethod]
    public void PrepareFiles_DirectoryUsesPathsRelativeToItsParent()
    {
        var root = Path.Combine(Path.GetTempPath(), $"Lertaro.LocalSend.Tests.{Guid.NewGuid():N}");
        var folder = Path.Combine(root, "folder");
        Directory.CreateDirectory(Path.Combine(folder, "nested"));
        var file = Path.Combine(folder, "nested", "test.txt");
        try
        {
            File.WriteAllText(file, "test");
            var tracker = new LocalSendSendProgressTracker();

            tracker.PrepareFiles([folder]);

            Assert.HasCount(1, tracker.Items);
            Assert.AreEqual("folder/nested/test.txt", tracker.Items[0].DisplayName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
