using Lertaro.App.Views.LocalSend;

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
}
