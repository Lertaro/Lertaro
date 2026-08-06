using System.Windows;
using Lertaro.Core.Indexer.Usn;
using Lertaro.Core.Services.Search;
using Lertaro.App.ViewModels.Settings;

namespace Lertaro.App.Tests.ViewModels.Settings;

[TestClass]
public sealed class ServiceSettingsViewModelTests
{
    // SearchService's constructor does no real I/O (its SearchPipeClient connects lazily, only on an
    // actual pipe call) -- safe to construct as long as nothing here calls InstallServiceCommand.Execute
    // or any async SearchService method.
    private static ServiceSettingsViewModel MakeVm(Action? onStatusChanged = null) => new(new SearchService(), onStatusChanged ?? (() => { }));

    [TestMethod]
    public void UpdateStatus_ErrorState_ShowsErrorIconAndInstallButton()
    {
        var vm = MakeVm();

        vm.UpdateStatus(new UsnIndexer.IndexerStatus { State = "error" });

        Assert.AreEqual(Visibility.Collapsed, vm.ProgressBarVisibility);
        Assert.AreEqual(Visibility.Visible, vm.ErrorIconVisibility);
        Assert.AreEqual(Visibility.Visible, vm.InstallButtonVisibility);
        Assert.AreEqual("[Service_ErrorTitle]", vm.LoadingTitle);
    }

    [TestMethod]
    public void UpdateStatus_IndexingState_ShowsProgressBarNoErrorOrInstall()
    {
        var vm = MakeVm();

        vm.UpdateStatus(new UsnIndexer.IndexerStatus { State = "indexing", Progress = 50, TotalFiles = 10, TotalDirs = 2 });

        Assert.AreEqual(Visibility.Visible, vm.ProgressBarVisibility);
        Assert.AreEqual(Visibility.Collapsed, vm.ErrorIconVisibility);
        Assert.AreEqual(Visibility.Collapsed, vm.InstallButtonVisibility);
    }

    [TestMethod]
    public void UpdateStatus_LoadingCacheState_ShowsProgressBarWithLoadingTitle()
    {
        var vm = MakeVm();

        vm.UpdateStatus(new UsnIndexer.IndexerStatus { State = "loading-cache" });

        Assert.AreEqual(Visibility.Visible, vm.ProgressBarVisibility);
        Assert.AreEqual("[Service_ProgressLoading]", vm.LoadingTitle);
    }

    [TestMethod]
    public void UpdateStatus_MaintenanceBusyEvenWhenReady_StillShowsProgressBar()
    {
        var vm = MakeVm();

        vm.UpdateStatus(new UsnIndexer.IndexerStatus { State = "ready", IsMaintenanceBusy = true });

        Assert.AreEqual(Visibility.Visible, vm.ProgressBarVisibility);
    }

    [TestMethod]
    public void UpdateStatus_ReadyState_ShowsReadyTitleAndNoProgressBar()
    {
        var vm = MakeVm();

        vm.UpdateStatus(new UsnIndexer.IndexerStatus { State = "ready", TotalFiles = 10, TotalDirs = 5 });

        Assert.AreEqual(Visibility.Collapsed, vm.ProgressBarVisibility);
        Assert.AreEqual(Visibility.Collapsed, vm.ErrorIconVisibility);
        Assert.AreEqual(Visibility.Collapsed, vm.InstallButtonVisibility);
        Assert.AreEqual("[Service_ReadyTitle]", vm.LoadingTitle);
    }

    [TestMethod]
    public void InstallServiceCommand_CanExecute_AlwaysTrue() =>
        Assert.IsTrue(MakeVm().InstallServiceCommand.CanExecute(null));
}
