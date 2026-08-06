using Lertaro.Core.Services.Search;
using Lertaro.App.ViewModels.Settings.NetworkDrive;

namespace Lertaro.App.Tests.ViewModels.Settings.NetworkDrive;

[TestClass]
public sealed class NetworkDriveSummaryHelperTests
{
    private static NetworkDriveSettingsViewModel MakeVm() => new(new SearchService(), () => { });

    [TestMethod]
    public void UpdateSummaries_NoDrives_UsesEmptyTemplate()
    {
        var vm = MakeVm();

        NetworkDriveSummaryHelper.UpdateSummaries(vm, null, false, false, false);

        Assert.AreEqual("[Network_DrivesEmpty]", vm.NetworkIndexSummary);
    }

    [TestMethod]
    public void UpdateSummaries_HasDrives_UsesSummaryTemplateNotEmptyTemplate()
    {
        var vm = MakeVm();
        vm.NetworkDrives.Add(new NetworkDriveSettingsItem { Drive = "Z", AppliedEnabled = true });

        NetworkDriveSummaryHelper.UpdateSummaries(vm, null, false, false, false);

        Assert.AreEqual("[Network_SummaryTemplate]", vm.NetworkIndexSummary);
    }

    [TestMethod]
    public void UpdateSummaries_FolderIndexesEmpty_UsesFolderEmptyKeyNotNetworkEmptyKey()
    {
        var vm = MakeVm();

        NetworkDriveSummaryHelper.UpdateSummaries(vm, null, false, false, false);

        Assert.AreEqual("[Folder_IndexEmpty]", vm.FolderIndexSummary);
    }

    [TestMethod]
    public void UpdateSummaries_CategoriesAreComputedIndependently()
    {
        var vm = MakeVm();
        vm.NetworkDrives.Add(new NetworkDriveSettingsItem { Drive = "Z", AppliedEnabled = true });
        // WslDrives and FolderIndexes stay empty -- their summaries must not pick up the drive's content.

        NetworkDriveSummaryHelper.UpdateSummaries(vm, null, false, false, false);

        Assert.AreEqual("[Network_SummaryTemplate]", vm.NetworkIndexSummary);
        Assert.AreEqual("[Network_DrivesEmpty]", vm.WslIndexSummary);
        Assert.AreEqual("[Folder_IndexEmpty]", vm.FolderIndexSummary);
    }
}
