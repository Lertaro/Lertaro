namespace Lertaro.Core.Tests.Settings;

// The service's own log level, which is the only one it has: it runs as LocalSystem and cannot read the
// per-user setting the app and the hook both use, so before this it sat at a hardcoded default and
// every LogLevel.Debug line in the indexer was unreachable no matter what the settings page said.
[TestClass]
public sealed class MachineSettingsTests
{
    private string _dir = string.Empty;

    [TestInitialize]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "LertaroMachineSettingsTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void TearDown()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

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

    // TryLoadFromFile takes an explicit path, so it can be exercised against an isolated temp
    // directory. Save() itself is NOT tested: it targets the real static %ProgramData% path.
    [TestMethod]
    public void TryLoadFromFile_ValidJson_ReturnsSettings()
    {
        var path = Path.Combine(_dir, "machine-settings.json");
        File.WriteAllText(path, """{"LocalDrives":["volume-c"],"LocalDriveSelectionConfigured":true}""");

        var settings = MachineSettings.TryLoadFromFile(path);

        Assert.IsNotNull(settings);
        CollectionAssert.AreEqual(new[] { "volume-c" }, settings.LocalDrives);
        Assert.IsTrue(settings.LocalDriveSelectionConfigured);
    }

    [TestMethod]
    public void TryLoadFromFile_CorruptJson_ReturnsNull()
    {
        var path = Path.Combine(_dir, "machine-settings.json");
        File.WriteAllText(path, "{ truncated");

        Assert.IsNull(MachineSettings.TryLoadFromFile(path));
    }

    [TestMethod]
    public void TryLoadFromFile_MissingFile_ReturnsNull() => Assert.IsNull(MachineSettings.TryLoadFromFile(Path.Combine(_dir, "machine-settings.json")));

    [TestMethod]
    public void TryLoadFromFile_LockedFile_ReturnsNullInsteadOfThrowing()
    {
        var path = Path.Combine(_dir, "machine-settings.json");
        File.WriteAllText(path, """{"LocalDrives":["volume-c"]}""");

        // The retry budget (~150ms) must be exhausted before the load gives up with null rather
        // than propagating the sharing violation.
        FileStream? lockStream = null;
        try
        {
            lockStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            Assert.IsNull(MachineSettings.TryLoadFromFile(path));
        }
        finally
        {
            lockStream?.Dispose();
        }
    }

    // --- The mirror of the user's walk exclusion rules ---------------------------------------------
    // The local drive walk runs in the --service process, which cannot read the per-user settings file;
    // MachineSettings is the copy it can read, so these three tests pin the mirror the App writes there:
    // it carries the rules, it leaves every other field alone, and an older file without the fields --
    // which is every machine-settings.json in the field today -- still loads.

    [TestMethod]
    public void MirrorExclusionRules_WritesTheUsersRulesAndKeepsTheOtherFields()
    {
        var path = Path.Combine(_dir, "machine-settings.json");
        File.WriteAllText(path, """{"LocalDrives":["volume-c"],"LocalDriveSelectionConfigured":true,"ServiceLogLevel":"Debug"}""");
        var user = new UserSettings
        {
            ExcludedPaths = [@"C:\excluded"],
            IgnoredPathGlobs = ["node_modules", "*.tmp"],
            IgnoredPathRegexes = ["^secret-"],
        };

        MachineSettings.MirrorExclusionRules(user, path);

        var reloaded = MachineSettings.TryLoadFromFile(path);
        Assert.IsNotNull(reloaded);
        CollectionAssert.AreEqual(new[] { @"C:\excluded" }, reloaded.ExcludedPaths);
        CollectionAssert.AreEqual(new[] { "node_modules", "*.tmp" }, reloaded.IgnoredPathGlobs);
        CollectionAssert.AreEqual(new[] { "^secret-" }, reloaded.IgnoredPathRegexes);
        CollectionAssert.AreEqual(new[] { "volume-c" }, reloaded.LocalDrives);
        Assert.IsTrue(reloaded.LocalDriveSelectionConfigured);
        Assert.AreEqual("Debug", reloaded.ServiceLogLevel);
    }

    // Why the mirror reads the raw file instead of Load(): Load migrates an unconfigured drive list by
    // stamping the drives detected at that moment into it, which would turn "every drive" into this
    // launch's snapshot for every install whose machine file did not exist yet.
    [TestMethod]
    public void MirrorExclusionRules_FreshInstall_DoesNotStampADetectedDriveSelection()
    {
        var path = Path.Combine(_dir, "machine-settings.json");

        MachineSettings.MirrorExclusionRules(new UserSettings { ExcludedPaths = [@"C:\excluded"] }, path);

        var reloaded = MachineSettings.TryLoadFromFile(path);
        Assert.IsNotNull(reloaded);
        Assert.IsEmpty(reloaded.LocalDrives);
        Assert.IsFalse(reloaded.LocalDriveSelectionConfigured);
    }

    // The mirror runs on every user-settings save (window moves included), so it must be a no-op unless
    // the rules actually changed: a rewrite would churn the file, rotate its .bak, and race the service's
    // own drive-selection write for no reason.
    [TestMethod]
    public void MirrorExclusionRules_UnchangedRules_LeaveTheFileUntouched()
    {
        var path = Path.Combine(_dir, "machine-settings.json");
        var user = new UserSettings { ExcludedPaths = [@"C:\excluded"], IgnoredPathGlobs = [], IgnoredPathRegexes = [] };
        MachineSettings.MirrorExclusionRules(user, path);
        var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, stamp);

        MachineSettings.MirrorExclusionRules(user, path);

        Assert.AreEqual(stamp, File.GetLastWriteTimeUtc(path));
    }

    // Back-compat: every machine-settings.json written before the rules moved here lacks these fields,
    // and must load as "no rules" without throwing and without disturbing the fields it does carry.
    [TestMethod]
    public void TryLoadFromFile_OlderFileWithoutTheRuleFields_LoadsWithEmptyRules()
    {
        var path = Path.Combine(_dir, "machine-settings.json");
        File.WriteAllText(path, """{"LocalDrives":["volume-c"],"LocalDriveSelectionConfigured":true,"ServiceLogLevel":"Warn"}""");

        var settings = MachineSettings.TryLoadFromFile(path);

        Assert.IsNotNull(settings);
        Assert.IsEmpty(settings.ExcludedPaths);
        Assert.IsEmpty(settings.IgnoredPathGlobs);
        Assert.IsEmpty(settings.IgnoredPathRegexes);
        CollectionAssert.AreEqual(new[] { "volume-c" }, settings.LocalDrives);
        Assert.IsTrue(settings.LocalDriveSelectionConfigured);
        Assert.AreEqual("Warn", settings.ServiceLogLevel);
    }
}
