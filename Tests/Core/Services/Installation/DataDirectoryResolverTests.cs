using Lertaro.Core.Services.Installation;

namespace Lertaro.Core.Tests.Services.Installation;

[TestClass]
public sealed class DataDirectoryResolverTests
{
    [TestMethod]
    public void ResolveShared_PortableCopyPrefersItsOwnDataFolder() => Assert.AreEqual(
            @"D:\Tools\Lertaro\Data\Machine",
            DataDirectoryResolver.ResolveShared(
                InstallationMode.Portable,
                @"D:\Tools\Lertaro",
                @"C:\ProgramData",
                portableDataDirectoryExists: true,
                installedDataDirectoryExists: true));

    [TestMethod]
    public void ResolveUser_PortableCopyPrefersItsOwnDataFolder() => Assert.AreEqual(
            @"D:\Tools\Lertaro\Data\Users\20d80484069962670c7a67191a3734f41b2f1759e466d2e061e1d8220a3b0ee2",
            DataDirectoryResolver.ResolveUser(
                InstallationMode.Portable,
                @"D:\Tools\Lertaro",
                @"C:\Users\testuser\AppData\Local",
                "S-1-5-21-100",
                portableDataDirectoryExists: true,
                installedDataDirectoryExists: true));

    [TestMethod]
    public void ResolveShared_PortableCopyWithoutDataUsesExistingInstalledData() => Assert.AreEqual(
            @"C:\ProgramData\Lertaro",
            DataDirectoryResolver.ResolveShared(
                InstallationMode.Portable,
                @"D:\Tools\Lertaro",
                @"C:\ProgramData",
                portableDataDirectoryExists: false,
                installedDataDirectoryExists: true));

    [TestMethod]
    public void ResolveUser_PortableCopyWithoutDataUsesExistingInstalledData() => Assert.AreEqual(
            @"C:\Users\testuser\AppData\Local\Lertaro",
            DataDirectoryResolver.ResolveUser(
                InstallationMode.Portable,
                @"D:\Tools\Lertaro",
                @"C:\Users\testuser\AppData\Local",
                "S-1-5-21-100",
                portableDataDirectoryExists: false,
                installedDataDirectoryExists: true));

    [TestMethod]
    public void ResolveShared_PortableCopyWithoutAnyExistingDataUsesNewPortablePath() => Assert.AreEqual(
            @"D:\Tools\Lertaro\Data\Machine",
            DataDirectoryResolver.ResolveShared(
                InstallationMode.Portable,
                @"D:\Tools\Lertaro",
                @"C:\ProgramData",
                portableDataDirectoryExists: false,
                installedDataDirectoryExists: false));

    [TestMethod]
    public void ResolveUser_PortableCopyWithoutAnyExistingDataUsesNewPortablePath() => Assert.AreEqual(
            @"D:\Tools\Lertaro\Data\Users\20d80484069962670c7a67191a3734f41b2f1759e466d2e061e1d8220a3b0ee2",
            DataDirectoryResolver.ResolveUser(
                InstallationMode.Portable,
                @"D:\Tools\Lertaro",
                @"C:\Users\testuser\AppData\Local",
                "S-1-5-21-100",
                portableDataDirectoryExists: false,
                installedDataDirectoryExists: false));

    [TestMethod]
    public void ResolveShared_InstalledCopyUsesProgramData() => Assert.AreEqual(
            @"C:\ProgramData\Lertaro",
            DataDirectoryResolver.ResolveShared(
                InstallationMode.Installed,
                @"D:\Tools\Lertaro",
                @"C:\ProgramData"));

    [TestMethod]
    public void ResolveUser_InstalledCopyUsesLocalAppData() => Assert.AreEqual(
            @"C:\Users\testuser\AppData\Local\Lertaro",
            DataDirectoryResolver.ResolveUser(
                InstallationMode.Installed,
                @"D:\Tools\Lertaro",
                @"C:\Users\testuser\AppData\Local",
                "S-1-5-21-100"));
}
