using Lertaro.Core;
using Lertaro.App.ViewModels.Settings;

namespace Lertaro.App.Tests.ViewModels.Settings;

[TestClass]
public sealed class SettingsApplyHelpersTests
{
    [TestMethod]
    public void NetworkSettingsChanged_IdenticalLists_ReturnsFalse()
    {
        var oldList = new List<NetworkDriveSetting> { new() { Id = "Z", RefreshMode = "Manual" } };
        var newList = new List<NetworkDriveSetting> { new() { Id = "Z", RefreshMode = "Manual" } };

        Assert.IsFalse(SettingsApplyHelpers.NetworkSettingsChanged(oldList, newList));
    }

    [TestMethod]
    public void NetworkSettingsChanged_DifferentOrderSameContent_ReturnsFalse()
    {
        var oldList = new List<NetworkDriveSetting> { new() { Id = "A" }, new() { Id = "B" } };
        var newList = new List<NetworkDriveSetting> { new() { Id = "B" }, new() { Id = "A" } };

        Assert.IsFalse(SettingsApplyHelpers.NetworkSettingsChanged(oldList, newList));
    }

    [TestMethod]
    public void NetworkSettingsChanged_DifferentRefreshMode_ReturnsTrue()
    {
        var oldList = new List<NetworkDriveSetting> { new() { Id = "Z", RefreshMode = "Manual" } };
        var newList = new List<NetworkDriveSetting> { new() { Id = "Z", RefreshMode = "Hourly" } };

        Assert.IsTrue(SettingsApplyHelpers.NetworkSettingsChanged(oldList, newList));
    }

    [TestMethod]
    public void NetworkSettingsChanged_DifferentCount_ReturnsTrue()
    {
        var oldList = new List<NetworkDriveSetting> { new() { Id = "Z" } };
        var newList = new List<NetworkDriveSetting>();

        Assert.IsTrue(SettingsApplyHelpers.NetworkSettingsChanged(oldList, newList));
    }

    [TestMethod]
    public void WslSettingsChanged_IdenticalLists_ReturnsFalse()
    {
        var oldList = new List<WslSetting> { new() { Id = "Ubuntu", RefreshMode = "Daily" } };
        var newList = new List<WslSetting> { new() { Id = "Ubuntu", RefreshMode = "Daily" } };

        Assert.IsFalse(SettingsApplyHelpers.WslSettingsChanged(oldList, newList));
    }

    [TestMethod]
    public void WslSettingsChanged_DifferentId_ReturnsTrue()
    {
        var oldList = new List<WslSetting> { new() { Id = "Ubuntu" } };
        var newList = new List<WslSetting> { new() { Id = "Debian" } };

        Assert.IsTrue(SettingsApplyHelpers.WslSettingsChanged(oldList, newList));
    }

    [TestMethod]
    public void FolderIndexesChanged_IdenticalLists_ReturnsFalse()
    {
        var oldList = new List<FolderIndexSetting> { new() { Path = @"C:\a", RefreshMode = "Manual" } };
        var newList = new List<FolderIndexSetting> { new() { Path = @"C:\a", RefreshMode = "Manual" } };

        Assert.IsFalse(SettingsApplyHelpers.FolderIndexesChanged(oldList, newList));
    }

    [TestMethod]
    public void FolderIndexesChanged_PathCaseDifferenceOnly_ReturnsFalse()
    {
        var oldList = new List<FolderIndexSetting> { new() { Path = @"C:\a" } };
        var newList = new List<FolderIndexSetting> { new() { Path = @"C:\A" } };

        Assert.IsFalse(SettingsApplyHelpers.FolderIndexesChanged(oldList, newList));
    }

    [TestMethod]
    public void FolderIndexesChanged_DifferentPath_ReturnsTrue()
    {
        var oldList = new List<FolderIndexSetting> { new() { Path = @"C:\a" } };
        var newList = new List<FolderIndexSetting> { new() { Path = @"C:\b" } };

        Assert.IsTrue(SettingsApplyHelpers.FolderIndexesChanged(oldList, newList));
    }
}
