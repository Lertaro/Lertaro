using Lertaro.App.ViewModels.Settings.LocalDrive;
using Lertaro.Core;
using Lertaro.Core.Indexer.Usn;
using Lertaro.Core.Services.Search;

namespace Lertaro.App.Tests.ViewModels.Settings.LocalDrive;

[TestClass]
public sealed class LocalDrivePendingEditsTests
{
    [TestMethod]
    public void ServiceUnavailable_PreservesEditedSelectionAndDisablesGlobalActions()
    {
        using var service = new SearchService();
        var viewModel = new LocalDriveSettingsViewModel(service, () => { });
        try
        {
            var status = new UsnIndexer.IndexerStatus { State = "ready", Drives = [new UsnIndexer.DriveIndexStatus { Drive = "C", State = "ready" }] };
            viewModel.UpdateStatus(status, new MachineSettings());
            Assert.IsFalse(viewModel.HasPendingEdits);
            var item = viewModel.LocalDrives.Single();
            item.IsEnabled = !item.IsEnabled;

            viewModel.UpdateStatus(new UsnIndexer.IndexerStatus { State = "error" }, new MachineSettings());

            Assert.AreSame(item, viewModel.LocalDrives.Single());
            Assert.IsTrue(item.IsEnabled);
            Assert.IsTrue(viewModel.HasPendingEdits);
            Assert.IsFalse(item.CanEditEnabled);
            Assert.IsFalse(item.CanRunRowAction);
        }
        finally { viewModel.Cleanup(); }
    }
}
