namespace Lertaro.Core.Tests.Settings;

[TestClass]
public sealed class SettingsDataDirectoryMigratorTests
{
    private string _testDirectory = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "LertaroCoreTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testDirectory))
            Directory.Delete(_testDirectory, recursive: true);
    }

    // The JSON string-escaped form of a Windows path: every backslash doubled, as a path appears
    // inside a JSON string literal in the settings file.
    private static string JsonEscaped(string path) => path.Replace("\\", "\\\\");

    [TestMethod]
    public void Migrate_RenamesDirectoryAndUpdatesUserSettings()
    {
        var legacyDirectory = Path.Combine(_testDirectory, "SwiftList");
        var currentDirectory = Path.Combine(_testDirectory, "Lertaro");
        Directory.CreateDirectory(legacyDirectory);
        // The settings carry a path under the legacy directory (in its JSON string-escaped form),
        // which is the only thing the directory move invalidates, plus an unrelated bare "SwiftList"
        // that the rewrite must leave alone.
        File.WriteAllText(
            Path.Combine(legacyDirectory, "user-settings.json"),
            $"{{\"favorites\":[\"{JsonEscaped(Path.Combine(legacyDirectory, "plugins"))}\"],\"title\":\"SwiftList\"}}");

        SettingsDataDirectoryMigrator.Migrate(currentDirectory, updateUserSettings: true);

        Assert.IsFalse(Directory.Exists(legacyDirectory));
        Assert.AreEqual(
            $"{{\"favorites\":[\"{JsonEscaped(Path.Combine(currentDirectory, "plugins"))}\"],\"title\":\"SwiftList\"}}",
            File.ReadAllText(Path.Combine(currentDirectory, "user-settings.json")));
    }

    [TestMethod]
    public void Migrate_MergesIntoCurrentDirectoryCreatedBeforeSettingsLoad()
    {
        var legacyDirectory = Path.Combine(_testDirectory, "SwiftList");
        var currentDirectory = Path.Combine(_testDirectory, "Lertaro");
        Directory.CreateDirectory(Path.Combine(legacyDirectory, "plugins"));
        Directory.CreateDirectory(Path.Combine(currentDirectory, "logs"));
        File.WriteAllText(
            Path.Combine(legacyDirectory, "user-settings.json"),
            $"{{\"favorites\":[\"{JsonEscaped(Path.Combine(legacyDirectory, "plugins"))}\"],\"title\":\"SwiftList\"}}");
        File.WriteAllText(Path.Combine(legacyDirectory, "plugins", "plugin.json"), "{}");
        File.WriteAllText(Path.Combine(currentDirectory, "logs", "app.log"), "current log");

        SettingsDataDirectoryMigrator.Migrate(currentDirectory, updateUserSettings: true);

        Assert.IsFalse(Directory.Exists(legacyDirectory));
        Assert.AreEqual(
            $"{{\"favorites\":[\"{JsonEscaped(Path.Combine(currentDirectory, "plugins"))}\"],\"title\":\"SwiftList\"}}",
            File.ReadAllText(Path.Combine(currentDirectory, "user-settings.json")));
        Assert.AreEqual("{}", File.ReadAllText(Path.Combine(currentDirectory, "plugins", "plugin.json")));
        Assert.AreEqual("current log", File.ReadAllText(Path.Combine(currentDirectory, "logs", "app.log")));
    }

    [TestMethod]
    public void Migrate_DoesNotTouchUnrelatedSwiftListStrings()
    {
        var legacyDirectory = Path.Combine(_testDirectory, "SwiftList");
        var currentDirectory = Path.Combine(_testDirectory, "Lertaro");
        Directory.CreateDirectory(legacyDirectory);
        // "SwiftList" appears only outside the legacy data-directory path, so nothing in the file may
        // change at all.
        const string settingsJson = "{\"title\":\"SwiftList notes\",\"path\":\"C:\\\\other\\\\SwiftList-docs\"}";
        File.WriteAllText(Path.Combine(legacyDirectory, "user-settings.json"), settingsJson);

        SettingsDataDirectoryMigrator.Migrate(currentDirectory, updateUserSettings: true);

        Assert.IsFalse(Directory.Exists(legacyDirectory));
        Assert.AreEqual(settingsJson, File.ReadAllText(Path.Combine(currentDirectory, "user-settings.json")));
    }
}
