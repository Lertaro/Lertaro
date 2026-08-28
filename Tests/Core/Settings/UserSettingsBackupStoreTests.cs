using System.Text.Json;

namespace Lertaro.Core.Tests.Settings;

// UserSettingsBackupStore.TryLoadNewest operates on an arbitrary file path parameter (not the real
// Logger.UserDataDir-derived SettingsPath), so it's safe to exercise directly against an isolated
// temp directory (same reasoning as UserSettingsBackupRotationTests).
[TestClass]
public sealed class UserSettingsBackupStoreTests
{
    private string _dir = string.Empty;
    private string _filePath = string.Empty;

    [TestInitialize]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "LertaroBackupStoreTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _filePath = Path.Combine(_dir, "user-settings.json");
    }

    [TestCleanup]
    public void TearDown()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static string ValidJson(string logLevel) =>
        JsonSerializer.Serialize(new UserSettings { LogLevel = logLevel }, new JsonSerializerOptions { WriteIndented = true });

    private string BackupPath(int index) => $"{_filePath}.bak.{index}";

    [TestMethod]
    public void TryLoadNewest_PrefersTheNewestBackup()
    {
        File.WriteAllText(BackupPath(1), ValidJson("Debug"));
        File.WriteAllText(BackupPath(2), ValidJson("Error"));

        var restored = UserSettingsBackupStore.TryLoadNewest(_filePath, 5, UserSettings.TryParse);

        Assert.IsNotNull(restored);
        Assert.AreEqual("Debug", restored.LogLevel);
    }

    [TestMethod]
    public void TryLoadNewest_SkipsCorruptBackups()
    {
        File.WriteAllText(BackupPath(1), "{ truncated");
        File.WriteAllText(BackupPath(2), ValidJson("Error"));

        var restored = UserSettingsBackupStore.TryLoadNewest(_filePath, 5, UserSettings.TryParse);

        Assert.IsNotNull(restored);
        Assert.AreEqual("Error", restored.LogLevel);
    }

    [TestMethod]
    public void TryLoadNewest_WithNoBackups_ReturnsNull() => Assert.IsNull(UserSettingsBackupStore.TryLoadNewest(_filePath, 5, UserSettings.TryParse));

    [TestMethod]
    public void TryLoadNewest_WhenAParseCallbackRejectsEveryBackup_ReturnsNull()
    {
        File.WriteAllText(BackupPath(1), ValidJson("Debug"));
        File.WriteAllText(BackupPath(2), ValidJson("Error"));

        Assert.IsNull(UserSettingsBackupStore.TryLoadNewest(_filePath, 5, _ => null));
    }
}
