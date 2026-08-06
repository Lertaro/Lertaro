using Lertaro.Core.Indexer.NetworkDrive;
using Lertaro.Core.Services.Search;
using Lertaro.App.ViewModels.Settings;
using Lertaro.App.ViewModels.Settings.NetworkDrive;

namespace Lertaro.App.Tests.ViewModels.Settings.NetworkDrive;

[TestClass]
public sealed class NetworkDrivePermissionsHelperTests
{
    // NetworkDriveSettingsViewModel's constructor is cheap (see NetworkDriveViewModelHelperTests for the
    // same reasoning) -- only its Rebuild*Command.Execute()/RefreshNetworkDrives() touch the real
    // SearchService, neither of which these tests ever call.
    private static NetworkDriveSettingsViewModel MakeVm() => new(new SearchService(), () => { });

    [TestMethod]
    public void UpdateRowPermissions_DriveBusy_DisablesEditingButKeepsStopClickable()
    {
        var vm = MakeVm();
        var drive = new NetworkDriveSettingsItem { IsPresent = true, RowAction = NetworkDriveRowAction.Stop, AppliedEnabled = true };
        vm.NetworkDrives.Add(drive);

        NetworkDrivePermissionsHelper.UpdateRowPermissions(vm, driveBusy: true, wslBusy: false, folderBusy: false);

        Assert.IsFalse(drive.CanEditEnabled);
        Assert.IsFalse(drive.CanEditRefreshMode);
        Assert.IsTrue(drive.CanRunRowAction);
    }

    [TestMethod]
    public void UpdateRowPermissions_NotPresent_DisablesEditing()
    {
        var vm = MakeVm();
        var drive = new NetworkDriveSettingsItem { IsPresent = false };
        vm.NetworkDrives.Add(drive);

        NetworkDrivePermissionsHelper.UpdateRowPermissions(vm, driveBusy: false, wslBusy: false, folderBusy: false);

        Assert.IsFalse(drive.CanEditEnabled);
        Assert.IsFalse(drive.CanEditRefreshMode);
    }

    [TestMethod]
    public void UpdateRowPermissions_RebuildActionOnlyEnabledWhenSomeDriveApplied()
    {
        var vm = MakeVm();
        var drive = new NetworkDriveSettingsItem { IsPresent = true, RowAction = NetworkDriveRowAction.Rebuild, AppliedEnabled = false };
        vm.NetworkDrives.Add(drive);

        NetworkDrivePermissionsHelper.UpdateRowPermissions(vm, driveBusy: false, wslBusy: false, folderBusy: false);

        Assert.IsFalse(drive.CanRunRowAction);
    }

    [TestMethod]
    public void UpdateRowPermissions_DeleteActionAlwaysEnabledWhenNotBusy()
    {
        var vm = MakeVm();
        var drive = new NetworkDriveSettingsItem { IsPresent = true, RowAction = NetworkDriveRowAction.Delete, AppliedEnabled = false };
        vm.NetworkDrives.Add(drive);

        NetworkDrivePermissionsHelper.UpdateRowPermissions(vm, driveBusy: false, wslBusy: false, folderBusy: false);

        Assert.IsTrue(drive.CanRunRowAction);
    }

    [TestMethod]
    public void UpdateRowPermissions_FolderBusy_DeleteStaysClickableUnlikeDrives()
    {
        var vm = MakeVm();
        var folder = new FolderIndexSettingsItem { IsPresent = true, RowAction = NetworkDriveRowAction.Delete };
        vm.FolderIndexes.Add(folder);

        NetworkDrivePermissionsHelper.UpdateRowPermissions(vm, driveBusy: false, wslBusy: false, folderBusy: true);

        Assert.IsTrue(folder.CanRunRowAction);
    }

    [TestMethod]
    public void UpdateRowPermissions_CategoriesAreIndependent_WslBusyDoesNotAffectDrives()
    {
        var vm = MakeVm();
        var drive = new NetworkDriveSettingsItem { IsPresent = true };
        vm.NetworkDrives.Add(drive);

        NetworkDrivePermissionsHelper.UpdateRowPermissions(vm, driveBusy: false, wslBusy: true, folderBusy: false);

        Assert.IsTrue(drive.CanEditEnabled);
    }

    [TestMethod]
    public void IsCategoryBusy_KeyInPendingRebuilds_ReturnsTrue()
    {
        var pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Z" };

        Assert.IsTrue(NetworkDrivePermissionsHelper.IsCategoryBusy(pending, new[] { "Z" }, null));
    }

    [TestMethod]
    public void IsCategoryBusy_KeyIndexingInStatuses_ReturnsTrue()
    {
        var statuses = new List<NetworkIndexStatus> { new() { Drive = "Z", State = "indexing" } };

        Assert.IsTrue(NetworkDrivePermissionsHelper.IsCategoryBusy(new HashSet<string>(), new[] { "Z" }, statuses));
    }

    [TestMethod]
    public void IsCategoryBusy_KeyReadyInStatuses_ReturnsFalse()
    {
        var statuses = new List<NetworkIndexStatus> { new() { Drive = "Z", State = "ready" } };

        Assert.IsFalse(NetworkDrivePermissionsHelper.IsCategoryBusy(new HashSet<string>(), new[] { "Z" }, statuses));
    }

    [TestMethod]
    public void IsCategoryBusy_KeyNotInEitherSet_ReturnsFalse() =>
        Assert.IsFalse(NetworkDrivePermissionsHelper.IsCategoryBusy(new HashSet<string>(), new[] { "Z" }, null));
}
