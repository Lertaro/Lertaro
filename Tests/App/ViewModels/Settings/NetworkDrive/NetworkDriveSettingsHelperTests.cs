using Lertaro.Core.Indexer.NetworkDrive;
using Lertaro.Core.Services.Network;
using Lertaro.App.ViewModels.Settings.NetworkDrive;

namespace Lertaro.App.Tests.ViewModels.Settings.NetworkDrive;

[TestClass]
public sealed class NetworkDriveSettingsHelperTests
{
    [TestMethod]
    public void GetStateText_DriveNotReady_ReturnsUnavailable()
    {
        var drive = new ResolvedNetworkDrive { IsReady = false };

        Assert.AreEqual("[Network_StatusUnavailable]", NetworkDriveSettingsHelper.GetStateText(drive, null));
    }

    [TestMethod]
    [DataRow("indexing", "[Network_StatusIndexing]")]
    [DataRow("ready", "[Network_StatusReady]")]
    [DataRow("cached", "[Network_StatusCached]")]
    [DataRow("error", "[Network_StatusError]")]
    [DataRow("pending", "[Network_StatusPending]")]
    [DataRow("something-else", "[Network_StatusConnected]")]
    public void GetStateText_ReadyDrive_MapsIndexStatusState(string state, string expectedKey)
    {
        var drive = new ResolvedNetworkDrive { IsReady = true };
        var status = new NetworkIndexStatus { State = state };

        Assert.AreEqual(expectedKey, NetworkDriveSettingsHelper.GetStateText(drive, status));
    }

    [TestMethod]
    public void GetStateText_NullDriveAndNullStatus_ReturnsConnected() =>
        Assert.AreEqual("[Network_StatusConnected]", NetworkDriveSettingsHelper.GetStateText(null, null));

    [TestMethod]
    [DataRow("15Minutes")]
    [DataRow("Hourly")]
    [DataRow("Daily")]
    public void NormalizeRefreshMode_KnownValue_ReturnsAsIs(string value) =>
        Assert.AreEqual(value, NetworkDriveSettingsHelper.NormalizeRefreshMode(value));

    [TestMethod]
    public void NormalizeRefreshMode_UnknownOrNullValue_DefaultsToManual()
    {
        Assert.AreEqual("Manual", NetworkDriveSettingsHelper.NormalizeRefreshMode("garbage"));
        Assert.AreEqual("Manual", NetworkDriveSettingsHelper.NormalizeRefreshMode(null));
    }

    // Regression coverage: System.IO.Path.GetFileName(@"\\wsl$\Ubuntu") returns "" -- a bare two-segment
    // UNC path has no path component past its root by .NET's own rules (same as Path.GetFileName(@"C:\")
    // == "") -- which used to collapse every cached-but-no-longer-listed WSL distro into one blank row
    // instead of showing its real name (both here and in NetworkDriveRefreshCoordinator, which shares
    // this helper).
    [TestMethod]
    public void GetWslDistroName_WslDollarPrefix_ReturnsDistroName() =>
        Assert.AreEqual("Ubuntu", NetworkDriveSettingsHelper.GetWslDistroName(@"\\wsl$\Ubuntu"));

    [TestMethod]
    public void GetWslDistroName_WslLocalhostPrefix_ReturnsDistroName() =>
        Assert.AreEqual("Ubuntu", NetworkDriveSettingsHelper.GetWslDistroName(@"\\wsl.localhost\Ubuntu"));

    [TestMethod]
    public void GetWslDistroName_TrailingBackslash_IsTrimmed() =>
        Assert.AreEqual("Ubuntu", NetworkDriveSettingsHelper.GetWslDistroName(@"\\wsl$\Ubuntu\"));

    [TestMethod]
    public void GetWslDistroName_MultiWordDistroName_ReturnsWholeName() =>
        Assert.AreEqual("Debian-Legacy", NetworkDriveSettingsHelper.GetWslDistroName(@"\\wsl$\Debian-Legacy"));
}
