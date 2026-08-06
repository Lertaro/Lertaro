namespace Lertaro.Core.Tests.Settings;

// UserSettings.RotateBackups() operates on an arbitrary file path parameter (not the real
// Logger.UserDataDir-derived SettingsPath), so unlike Load()/Save()/ForceReload() (see
// UserSettingsPluginSettingTests' own comment on why those are untested) it's safe to exercise
// directly against an isolated temp directory.
[TestClass]
public sealed class UserSettingsBackupRotationTests
{
    private string _dir = string.Empty;
    private string _filePath = string.Empty;

    [TestInitialize]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "LertaroRotateBackupsTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _filePath = Path.Combine(_dir, "user-settings.json");
    }

    [TestCleanup]
    public void TearDown()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string BackupPath(int i) => $"{_filePath}.bak.{i}";

    [TestMethod]
    public void RotateBackups_FileDoesNotExist_DoesNothing()
    {
        UserSettings.RotateBackups(_filePath);

        Assert.IsFalse(File.Exists(BackupPath(1)));
    }

    [TestMethod]
    public void RotateBackups_FirstCall_CopiesCurrentFileToBak1()
    {
        File.WriteAllText(_filePath, "v1");

        UserSettings.RotateBackups(_filePath);

        Assert.AreEqual("v1", File.ReadAllText(BackupPath(1)));
        Assert.IsTrue(File.Exists(_filePath), "the original file must not be moved, only copied");
    }

    [TestMethod]
    public void RotateBackups_SecondCall_ShiftsPreviousBak1ToBak2()
    {
        File.WriteAllText(_filePath, "v1");
        UserSettings.RotateBackups(_filePath);

        File.WriteAllText(_filePath, "v2");
        UserSettings.RotateBackups(_filePath);

        Assert.AreEqual("v2", File.ReadAllText(BackupPath(1)));
        Assert.AreEqual("v1", File.ReadAllText(BackupPath(2)));
    }

    [TestMethod]
    public void RotateBackups_SixthCall_DropsTheOldestBackup()
    {
        for (var i = 1; i <= 6; i++)
        {
            File.WriteAllText(_filePath, $"v{i}");
            UserSettings.RotateBackups(_filePath);
        }

        // Each iteration's RotateBackups() runs AFTER that iteration's write, so it captures v{i}
        // itself into bak.1, shifting everything older down one slot. After 6 iterations: bak.1=v6,
        // bak.2=v5, ..., bak.5=v2 -- v1 (the original oldest content, would-be bak.6) is evicted.
        Assert.AreEqual("v6", File.ReadAllText(BackupPath(1)));
        Assert.AreEqual("v5", File.ReadAllText(BackupPath(2)));
        Assert.AreEqual("v4", File.ReadAllText(BackupPath(3)));
        Assert.AreEqual("v3", File.ReadAllText(BackupPath(4)));
        Assert.AreEqual("v2", File.ReadAllText(BackupPath(5)));
        Assert.IsFalse(File.Exists(BackupPath(6)), "only maxBackups (5) files should ever exist");
    }

    [TestMethod]
    public void RotateBackups_UsesJsonBakSuffixNaming()
    {
        File.WriteAllText(_filePath, "v1");

        UserSettings.RotateBackups(_filePath);

        Assert.IsTrue(File.Exists(Path.Combine(_dir, "user-settings.json.bak.1")));
    }
}
