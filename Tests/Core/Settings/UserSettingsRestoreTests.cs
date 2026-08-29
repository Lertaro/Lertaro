using System.Text.Json;

namespace Lertaro.Core.Tests.Settings;

// UserSettings.WriteRestored() takes both file paths as parameters (not the real
// Logger.UserDataDir-derived SettingsPath) and touches no static state, so it's safe to exercise
// directly against an isolated temp directory. RestoreFrom() itself is deliberately NOT called
// here: it would overwrite the process-wide static settings cache (_cachedSettings/_lastJsonOnDisk)
// shared with every other test class in this assembly.
[TestClass]
public sealed class UserSettingsRestoreTests
{
    private string _dir = string.Empty;
    private string _mainPath = string.Empty;
    private string _sourcePath = string.Empty;

    [TestInitialize]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "LertaroRestoreTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _mainPath = Path.Combine(_dir, "user-settings.json");
        _sourcePath = Path.Combine(_dir, "exported-settings.json");
    }

    [TestCleanup]
    public void TearDown()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static string Serialize(string logLevel) =>
        JsonSerializer.Serialize(new UserSettings { LogLevel = logLevel }, new JsonSerializerOptions { WriteIndented = true });

    [TestMethod]
    public void WriteRestored_ValidSource_ReplacesMainFile()
    {
        File.WriteAllText(_mainPath, Serialize("Info"));
        File.WriteAllText(_sourcePath, Serialize("Debug"));

        UserSettings.WriteRestored(_sourcePath, _mainPath, backupCount: 5, out _);

        var settings = UserSettings.TryParse(File.ReadAllText(_mainPath));
        Assert.IsNotNull(settings);
        Assert.AreEqual("Debug", settings.LogLevel);
    }

    [TestMethod]
    public void WriteRestored_RotatesCurrentMainIntoBak1()
    {
        var oldMain = Serialize("Info");
        File.WriteAllText(_mainPath, oldMain);
        File.WriteAllText(_sourcePath, Serialize("Debug"));

        UserSettings.WriteRestored(_sourcePath, _mainPath, backupCount: 5, out _);

        Assert.AreEqual(oldMain, File.ReadAllText(_mainPath + ".bak.1"));
    }

    [TestMethod]
    public void WriteRestored_InvalidSource_ThrowsAndLeavesMainUntouched()
    {
        var oldMain = Serialize("Info");
        File.WriteAllText(_mainPath, oldMain);
        File.WriteAllText(_sourcePath, "{ truncated");

        Assert.ThrowsExactly<InvalidDataException>(
            () => UserSettings.WriteRestored(_sourcePath, _mainPath, backupCount: 5, out _));

        Assert.AreEqual(oldMain, File.ReadAllText(_mainPath));
        Assert.IsFalse(File.Exists(_mainPath + ".bak.1"), "the main file must not be rotated into the backup chain on a failed restore");
    }

    [TestMethod]
    public void WriteRestored_MissingSource_ThrowsFileNotFoundException()
    {
        var oldMain = Serialize("Info");
        File.WriteAllText(_mainPath, oldMain);

        Assert.ThrowsExactly<FileNotFoundException>(
            () => UserSettings.WriteRestored(_sourcePath, _mainPath, backupCount: 5, out _));

        Assert.AreEqual(oldMain, File.ReadAllText(_mainPath));
    }

    [TestMethod]
    public void WriteRestored_OutParamCarriesTheWrittenJson()
    {
        var sourceJson = Serialize("Debug");
        File.WriteAllText(_mainPath, Serialize("Info"));
        File.WriteAllText(_sourcePath, sourceJson);

        UserSettings.WriteRestored(_sourcePath, _mainPath, backupCount: 5, out var json);

        Assert.AreEqual(sourceJson, json);
        Assert.AreEqual(json, File.ReadAllText(_mainPath));
    }
}
