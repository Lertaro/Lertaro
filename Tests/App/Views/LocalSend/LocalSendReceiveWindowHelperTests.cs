using Lertaro.App.Views.LocalSend;
using Lertaro.Core.Services.LocalSend;

namespace Lertaro.App.Tests.Views.LocalSend;

[TestClass]
public sealed class LocalSendReceiveWindowHelperTests
{
    [TestMethod]
    public void FormatSummaryFileName_SingleFile_ReturnsFileName()
    {
        var res = LocalSendReceiveWindowHelper.FormatSummaryFileName("test.apk", 1);
        Assert.AreEqual("test.apk", res);
    }

    [TestMethod]
    public void FormatSummaryFileName_MultiFiles_ReturnsFileNameWithCount()
    {
        var res = LocalSendReceiveWindowHelper.FormatSummaryFileName("test.apk", 3);
        Assert.AreEqual("test.apk (3)", res);
    }

    [TestMethod]
    public void ResolveFolderTarget_InvalidPath_ReturnsEmpty()
    {
        var res = LocalSendReceiveWindowHelper.ResolveFolderTarget(@"C:\NonExistentDir12345\file.txt", null);
        Assert.AreEqual(string.Empty, res);
    }

    [TestMethod]
    public void MarkCanceledItems_OnlyMarksUnfinishedItems()
    {
        var completed = new LocalSendReceiveFileItem { FileId = "completed", FileName = "completed.txt", DisplayName = "completed.txt", Size = 1, SizeText = "1 B", IsFinished = true };
        var pending = new LocalSendReceiveFileItem { FileId = "pending", FileName = "pending.txt", DisplayName = "pending.txt", Size = 1, SizeText = "1 B", ShowProgress = true };

        LocalSendReceiveWindowHelper.MarkCanceledItems([completed, pending], "Canceled");

        Assert.IsFalse(completed.IsCanceled);
        Assert.IsTrue(pending.IsCanceled);
        Assert.AreEqual("Canceled", pending.StatusText);
        Assert.IsFalse(pending.ShowProgress);
    }

    [TestMethod]
    public void UpdateItems_FinalFailure_PreservesFailedItemAndCompletesOtherItems()
    {
        var completed = CreateItem("completed");
        var failed = CreateItem("failed");
        var args = new LocalSendProgressArgs("session", "sender", "failed", "failed.txt", 2, 4, 2, 2,
            isAllDone: true, isFailed: true);

        LocalSendReceiveWindowHelper.UpdateItems([completed, failed], args, "Completed", "Failed");

        Assert.IsTrue(completed.IsFinished);
        Assert.IsFalse(failed.IsFinished);
        Assert.IsTrue(failed.IsFailed);
        Assert.AreEqual(100.0, failed.ProgressPercentage);
        Assert.AreEqual("Failed", failed.StatusText);
    }

    [TestMethod]
    public void UpdateItems_RetryProgress_ClearsPreviousFailure()
    {
        var item = CreateItem("file");
        item.IsFailed = true;
        var args = new LocalSendProgressArgs("session", "sender", "file", "file.txt", 2, 4, 1, 1);

        LocalSendReceiveWindowHelper.UpdateItems([item], args, "Completed", "Failed");

        Assert.IsFalse(item.IsFailed);
        Assert.AreEqual("50%", item.StatusText);
    }

    private static LocalSendReceiveFileItem CreateItem(string id) => new()
    {
        FileId = id, FileName = $"{id}.txt", DisplayName = $"{id}.txt", Size = 4, SizeText = "4 B"
    };
}
