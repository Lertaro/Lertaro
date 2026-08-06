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

    [TestMethod]
    public void Migrate_RenamesDirectoryAndUpdatesUserSettings()
    {
        var legacyDirectory = Path.Combine(_testDirectory, "SwiftList");
        var currentDirectory = Path.Combine(_testDirectory, "Lertaro");
        Directory.CreateDirectory(legacyDirectory);
        File.WriteAllText(Path.Combine(legacyDirectory, "user-settings.json"), "{\"title\":\"SwiftList\"}");

        SettingsDataDirectoryMigrator.Migrate(currentDirectory, updateUserSettings: true);

        Assert.IsFalse(Directory.Exists(legacyDirectory));
        Assert.AreEqual("{\"title\":\"Lertaro\"}", File.ReadAllText(Path.Combine(currentDirectory, "user-settings.json")));
    }

    [TestMethod]
    public void Migrate_MergesIntoCurrentDirectoryCreatedBeforeSettingsLoad()
    {
        var legacyDirectory = Path.Combine(_testDirectory, "SwiftList");
        var currentDirectory = Path.Combine(_testDirectory, "Lertaro");
        Directory.CreateDirectory(Path.Combine(legacyDirectory, "plugins"));
        Directory.CreateDirectory(Path.Combine(currentDirectory, "logs"));
        File.WriteAllText(Path.Combine(legacyDirectory, "user-settings.json"), "{\"title\":\"SwiftList\"}");
        File.WriteAllText(Path.Combine(legacyDirectory, "plugins", "plugin.json"), "{}");
        File.WriteAllText(Path.Combine(currentDirectory, "logs", "app.log"), "current log");

        SettingsDataDirectoryMigrator.Migrate(currentDirectory, updateUserSettings: true);

        Assert.IsFalse(Directory.Exists(legacyDirectory));
        Assert.AreEqual("{\"title\":\"Lertaro\"}", File.ReadAllText(Path.Combine(currentDirectory, "user-settings.json")));
        Assert.AreEqual("{}", File.ReadAllText(Path.Combine(currentDirectory, "plugins", "plugin.json")));
        Assert.AreEqual("current log", File.ReadAllText(Path.Combine(currentDirectory, "logs", "app.log")));
    }
}
