using System.IO;
using Lertaro.App.ViewModels.LocalSend;

namespace Lertaro.App.Tests.ViewModels.LocalSend;

[TestClass]
public sealed class LocalSendSendViewModelTests
{
    [TestMethod]
    public void SetText_SwitchesExistingFileRequestToTextDeviceSelection()
    {
        var path = Path.GetTempFileName();
        try
        {
            using var viewModel = new LocalSendSendViewModel(new[] { path });

            viewModel.SetText("hello");

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
}
