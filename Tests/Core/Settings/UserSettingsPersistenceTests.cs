namespace Lertaro.Core.Tests.Settings;

// TryPersist takes its destination as a parameter, so unlike Save() (which is welded to the real
// per-user settings path) it can be pointed at an isolated temp directory.
[TestClass]
public sealed class UserSettingsPersistenceTests
{
    private string _dir = string.Empty;
    private string _path = string.Empty;

    [TestInitialize]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "LertaroUserSettingsPersistTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "user-settings.json");
    }

    [TestCleanup]
    public void TearDown()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [TestMethod]
    public void TryPersist_WritesNewContent_AndKeepsPreviousAsBackup()
    {
        File.WriteAllText(_path, "old");

        Assert.IsTrue(UserSettingsPersistence.TryPersist("new", _path));
        Assert.AreEqual("new", File.ReadAllText(_path));
        Assert.AreEqual("old", File.ReadAllText($"{_path}.bak.1"));
    }

    [TestMethod]
    public void TryPersist_WhenWriteFails_ReportsFailureAndLeavesFileUntouched()
    {
        File.WriteAllText(_path, "old");

        // Destination locked for replacement, so every retry of the atomic swap fails.
        using (new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.IsFalse(UserSettingsPersistence.TryPersist("new", _path));
        }

        Assert.AreEqual("old", File.ReadAllText(_path));
        Assert.IsEmpty(Directory.GetFiles(_dir, "*.tmp"), "a failed write leaves no temp file");
    }
}
