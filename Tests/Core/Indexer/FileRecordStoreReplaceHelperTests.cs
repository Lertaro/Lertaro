namespace Lertaro.Core.Tests.Indexer;

[TestClass]
public sealed class FileRecordStoreReplaceHelperTests
{
    [TestMethod]
    public void ReplaceWithRetry_FinalPathDoesNotExist_MovesTempToFinal()
    {
        using var dir = new TempDirectory();
        var tempPath = dir.CreateFile("temp.dat", "content");
        var finalPath = Path.Combine(dir.Path, "final.dat");

        FileRecordStoreReplaceHelper.ReplaceWithRetry(tempPath, finalPath, _ => { });

        Assert.IsTrue(File.Exists(finalPath));
        Assert.IsFalse(File.Exists(tempPath));
        Assert.AreEqual("content", File.ReadAllText(finalPath));
    }

    [TestMethod]
    public void ReplaceWithRetry_FinalPathExists_ReplacesItAndInvokesBackupCleanup()
    {
        using var dir = new TempDirectory();
        var tempPath = dir.CreateFile("temp.dat", "new-content");
        var finalPath = dir.CreateFile("final.dat", "old-content");

        string? deletedBackupPath = null;
        FileRecordStoreReplaceHelper.ReplaceWithRetry(tempPath, finalPath, path => deletedBackupPath = path);

        Assert.AreEqual("new-content", File.ReadAllText(finalPath));
        Assert.AreEqual(finalPath + ".bak", deletedBackupPath);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;

        public string CreateFile(string name, string content)
        {
            var path = System.IO.Path.Combine(Path, name);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
