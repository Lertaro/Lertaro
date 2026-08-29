using System.IO;
using Lertaro.App.Services;

namespace Lertaro.App.Tests.Services;

[TestClass]
public sealed class UserConfigBackupsTests
{
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;
        public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
    }

    [TestMethod]
    public void Enumerate_NoBackups_ReturnsEmpty()
    {
        using var dir = new TempDirectory();

        var backups = UserConfigBackups.Enumerate(dir.Path);

        Assert.HasCount(0, backups);
    }

    [TestMethod]
    public void Enumerate_MultipleBackups_NewestFirst()
    {
        using var dir = new TempDirectory();
        var first = WriteBackup(dir.Path, "user-settings.json.bak.1", new DateTime(2026, 1, 1, 12, 0, 0));
        var second = WriteBackup(dir.Path, "user-settings.json.bak.2", new DateTime(2026, 3, 1, 8, 30, 0));
        var third = WriteBackup(dir.Path, "user-settings.json.bak.3", new DateTime(2026, 2, 1, 9, 0, 0));

        var backups = UserConfigBackups.Enumerate(dir.Path);

        Assert.HasCount(3, backups);
        // Newest first, regardless of the rotation index in the file name.
        Assert.AreEqual(second, backups[0].Path);
        Assert.AreEqual(third, backups[1].Path);
        Assert.AreEqual(first, backups[2].Path);
        Assert.AreEqual(new DateTime(2026, 3, 1, 8, 30, 0), backups[0].ModifiedTime);
        Assert.AreEqual(new DateTime(2026, 2, 1, 9, 0, 0), backups[1].ModifiedTime);
        Assert.AreEqual(new DateTime(2026, 1, 1, 12, 0, 0), backups[2].ModifiedTime);
    }

    [TestMethod]
    public void Enumerate_IgnoresNonBackupFiles()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "user-settings.json"), "{}");
        File.WriteAllText(Path.Combine(dir.Path, "user-settings.json.tmp"), "{}");
        File.WriteAllText(Path.Combine(dir.Path, "something-else.bak.1"), "{}");

        var backups = UserConfigBackups.Enumerate(dir.Path);

        Assert.HasCount(0, backups);
    }

    [TestMethod]
    public void Enumerate_MissingDirectory_ReturnsEmpty()
    {
        using var dir = new TempDirectory();
        var missing = Path.Combine(dir.Path, "does-not-exist");

        var backups = UserConfigBackups.Enumerate(missing);

        Assert.HasCount(0, backups);
    }

    [TestMethod]
    public void BuildRestoreChoices_FormatsTimestampAndDisambiguatesDuplicates()
    {
        var modified = new DateTime(2026, 3, 1, 8, 30, 0);
        var backups = new[]
        {
            (Path: @"C:\backups\user-settings.json.bak.1", ModifiedTime: modified),
            (Path: @"C:\backups\user-settings.json.bak.2", ModifiedTime: modified)
        };

        var choices = UserConfigBackups.BuildRestoreChoices(backups);

        Assert.HasCount(2, choices);
        Assert.AreEqual("2026-03-01 08:30:00", choices[0].Display);
        Assert.AreEqual("2026-03-01 08:30:00 (2)", choices[1].Display);
        Assert.AreEqual(backups[0].Path, choices[0].Path);
        Assert.AreEqual(backups[1].Path, choices[1].Path);
    }

    [TestMethod]
    public void Export_CopiesSettingsFile()
    {
        using var source = new TempDirectory();
        using var target = new TempDirectory();
        var settingsPath = Path.Combine(source.Path, "user-settings.json");
        File.WriteAllText(settingsPath, """{"Theme":"Dark"}""");

        var exported = UserConfigBackups.Export(settingsPath, target.Path);

        Assert.IsNotNull(exported);
        Assert.AreEqual(Path.Combine(target.Path, "user-settings.json"), exported);
        Assert.AreEqual(File.ReadAllText(settingsPath), File.ReadAllText(exported));
    }

    [TestMethod]
    public void Export_MissingSource_ReturnsNull()
    {
        using var source = new TempDirectory();
        using var target = new TempDirectory();

        var exported = UserConfigBackups.Export(Path.Combine(source.Path, "user-settings.json"), target.Path);

        Assert.IsNull(exported);
    }

    // Creates a .bak file with an exact last-write time; returns the path for order assertions.
    private static string WriteBackup(string directory, string fileName, DateTime modifiedTime)
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, "{}");
        File.SetLastWriteTime(path, modifiedTime);
        return path;
    }
}
