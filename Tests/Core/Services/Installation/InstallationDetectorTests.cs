using Lertaro.Core.Services.Installation;

namespace Lertaro.Core.Tests.Services.Installation;

[TestClass]
public sealed class InstallationDetectorTests
{
    [TestMethod]
    public void IsInstalledAt_MatchesAnExecutableInTheRegisteredDirectory() => Assert.IsTrue(InstallationDetector.IsInstalledAt(
            @"C:\Program Files\Lertaro\",
            @"c:\program files\lertaro\Lertaro.App.exe"));

    [TestMethod]
    public void IsInstalledAt_MatchesAnyRunningLertaroExecutableInTheRegisteredDirectory() => Assert.IsTrue(InstallationDetector.IsInstalledAt(
            @"C:\Program Files\Lertaro",
            @"C:\Program Files\Lertaro\Lertaro.Hook.exe"));

    [TestMethod]
    public void IsInstalledAt_RejectsACopiedExecutable() => Assert.IsFalse(InstallationDetector.IsInstalledAt(
            @"C:\Program Files\Lertaro",
            @"D:\Tools\Lertaro\Lertaro.App.exe"));

    [TestMethod]
    public void IsInstalledAt_RejectsAnInvalidRegisteredPath() => Assert.IsFalse(InstallationDetector.IsInstalledAt(
            "\0",
            @"C:\Program Files\Lertaro\Lertaro.App.exe"));
}
