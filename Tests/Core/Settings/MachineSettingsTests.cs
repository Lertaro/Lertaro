namespace Lertaro.Core.Tests.Settings;

// The service's own log level, which is the only one it has: it runs as LocalSystem and cannot read the
// per-user setting the app and the hook both use, so before this it sat at a hardcoded default and
// every LogLevel.Debug line in the indexer was unreachable no matter what the settings page said.
[TestClass]
public sealed class MachineSettingsTests
{
    private static LogLevel Resolve(string? value) => new MachineSettings { ServiceLogLevel = value! }.ResolveServiceLogLevel();

    [TestMethod]
    public void EachLogLevelIsRecognised()
    {
        Assert.AreEqual(LogLevel.Error, Resolve("Error"));
        Assert.AreEqual(LogLevel.Warn, Resolve("Warn"));
        Assert.AreEqual(LogLevel.Info, Resolve("Info"));
        Assert.AreEqual(LogLevel.Debug, Resolve("Debug"));
    }

    [TestMethod]
    public void LogLevelCaseAndSurroundingSpaceDoNotMatter()
    {
        Assert.AreEqual(LogLevel.Debug, Resolve("debug"));
        Assert.AreEqual(LogLevel.Debug, Resolve("DEBUG"));
        Assert.AreEqual(LogLevel.Debug, Resolve("  Debug  "));
    }

    [TestMethod]
    public void MissingLogLevelRunsAtInfo() =>
        Assert.AreEqual(LogLevel.Info, new MachineSettings().ResolveServiceLogLevel());

    [TestMethod]
    public void UnrecognisedLogLevelRunsAtInfo()
    {
        Assert.AreEqual(LogLevel.Info, Resolve("verbose"));
        Assert.AreEqual(LogLevel.Info, Resolve(""));
        Assert.AreEqual(LogLevel.Info, Resolve(null));
    }

    [TestMethod]
    public void IsLocalDriveEnabled_EmptyExplicitSelectionReturnsFalse()
    {
        var settings = new MachineSettings { LocalDriveSelectionConfigured = true };

        Assert.IsFalse(settings.IsLocalDriveEnabled("volume-c"));
    }

    [TestMethod]
    public void IsLocalDriveEnabled_MatchesVolumeIdsCaseInsensitively()
    {
        var settings = new MachineSettings { LocalDrives = ["VOLUME-C"] };

        Assert.IsTrue(settings.IsLocalDriveEnabled("volume-c"));
        Assert.IsFalse(settings.IsLocalDriveEnabled("volume-d"));
    }

    [TestMethod]
    public void MigrateLegacyLocalDriveSelection_EmptyLegacySelectionUsesDetectedDrivesOnce()
    {
        var settings = new MachineSettings();

        settings.MigrateLegacyLocalDriveSelection(["volume-c", "VOLUME-C", "volume-d"]);
        settings.MigrateLegacyLocalDriveSelection(["volume-e"]);

        CollectionAssert.AreEqual(new[] { "volume-c", "volume-d" }, settings.LocalDrives);
        Assert.IsTrue(settings.LocalDriveSelectionConfigured);
    }

    [TestMethod]
    public void MigrateLegacyLocalDriveSelection_ExplicitEmptySelectionStaysEmpty()
    {
        var settings = new MachineSettings { LocalDriveSelectionConfigured = true };

        settings.MigrateLegacyLocalDriveSelection(["volume-c"]);

        Assert.IsEmpty(settings.LocalDrives);
    }

    [TestMethod]
    public void MigrateLegacyLocalDriveSelection_LegacySubsetStaysUnchanged()
    {
        var settings = new MachineSettings { LocalDrives = ["volume-d"] };

        settings.MigrateLegacyLocalDriveSelection(["volume-c", "volume-d"]);

        CollectionAssert.AreEqual(new[] { "volume-d" }, settings.LocalDrives);
        Assert.IsTrue(settings.LocalDriveSelectionConfigured);
    }
}
