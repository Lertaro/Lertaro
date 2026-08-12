using System.IO;
using Lertaro.App.ViewModels.LocalSend;

namespace Lertaro.App.Tests.ViewModels.LocalSend;

[TestClass]
public sealed class LocalSendSendViewModelTests
{
    [TestMethod]
    public void SetMode_WithText_SwitchesExistingItemRequestToTextDeviceSelection()
    {
        var path = Path.GetTempFileName();
        try
        {
            using var viewModel = new LocalSendSendViewModel(new[] { path });

            viewModel.SetMode(LocalSendSendMode.Text, "hello", proceed: true);

            Assert.IsTrue(viewModel.IsTextMode);
            Assert.AreEqual("hello", viewModel.TextToSend);
            Assert.IsEmpty(viewModel.TargetFiles);
            Assert.AreEqual(1, viewModel.CurrentStep);
            Assert.IsTrue(viewModel.IsFromAction);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Constructor_WithEmptyTextMode_OpensTextCollectionStep()
    {
        using var viewModel = new LocalSendSendViewModel(initialMode: LocalSendSendMode.Text);

        Assert.IsTrue(viewModel.IsTextMode);
        Assert.AreEqual(0, viewModel.CurrentStep);
        Assert.IsFalse(viewModel.CanGoNextStep);
    }

    [TestMethod]
    public void SetMode_WithEmptyText_ClearsPreviousText()
    {
        using var viewModel = new LocalSendSendViewModel(initialText: "previous");

        viewModel.SetMode(LocalSendSendMode.Text);

        Assert.AreEqual(string.Empty, viewModel.TextToSend);
        Assert.AreEqual(0, viewModel.CurrentStep);
        Assert.IsFalse(viewModel.CanGoNextStep);
    }
}
