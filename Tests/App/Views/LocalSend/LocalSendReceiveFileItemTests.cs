using Lertaro.App.Views.LocalSend;

namespace Lertaro.App.Tests.Views.LocalSend;

[TestClass]
public sealed class LocalSendReceiveFileItemTests
{
    [TestMethod]
    public void Properties_InitializedAndProgressUpdated()
    {
        var item = new LocalSendReceiveFileItem
        {
            FileId = "f1",
            FileName = "test.txt",
            DisplayName = "test.txt",
            Size = 1024,
            SizeText = "1 KB"
        };

        Assert.AreEqual("f1", item.FileId);
        Assert.AreEqual("test.txt", item.FileName);
        Assert.AreEqual("test.txt", item.DisplayName);
        Assert.AreEqual(1024, item.Size);
        Assert.AreEqual("1 KB", item.SizeText);

        var propertyFired = false;
        item.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(LocalSendReceiveFileItem.ProgressPercentage))
                propertyFired = true;
        };

        item.ShowProgress = true;
        item.ProgressPercentage = 50.0;
        Assert.IsTrue(item.ShowProgress);
        Assert.AreEqual(50.0, item.ProgressPercentage);
        Assert.IsTrue(propertyFired);
    }
}
