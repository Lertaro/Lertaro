namespace Lertaro.Core.Tests;

// Exercises AtomicFileStore.Write against an isolated temp directory -- the helper takes arbitrary
// paths, so nothing here touches the real settings or history locations.
[TestClass]
public sealed class AtomicFileStoreTests
{
    private string _dir = string.Empty;
    private string _path = string.Empty;

    [TestInitialize]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "LertaroAtomicFileStoreTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "store.json");
    }

    [TestCleanup]
    public void TearDown()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [TestMethod]
    public void Write_CreatesFileWithContent()
    {
        AtomicFileStore.Write(_path, "v1");

        Assert.AreEqual("v1", File.ReadAllText(_path));
    }

    [TestMethod]
    public void Write_ReplacesExistingContent_AndLeavesPreviousAtBackup()
    {
        AtomicFileStore.Write(_path, "v1");

        AtomicFileStore.Write(_path, "v2", _path + ".bak");

        Assert.AreEqual("v2", File.ReadAllText(_path));
        Assert.AreEqual("v1", File.ReadAllText(_path + ".bak"));
    }

    [TestMethod]
    public void Write_WithoutBackupPath_DiscardsPreviousContent()
    {
        AtomicFileStore.Write(_path, "v1");

        AtomicFileStore.Write(_path, "v2");

        Assert.AreEqual("v2", File.ReadAllText(_path));
        Assert.IsFalse(File.Exists(_path + ".bak"), "no backup must appear when no backup path is given");
    }

    [TestMethod]
    public void Write_WhenDestinationIsExclusivelyLocked_ThrowsIOException()
    {
        AtomicFileStore.Write(_path, "v1");

        // The destination is replace-locked, so every attempt fails; the retry loop (~250ms) must be
        // exhausted before the IOException reaches the caller, not swallowed.
        FileStream? lockStream = null;
        try
        {
            lockStream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.None);
            Assert.ThrowsExactly<IOException>(() => AtomicFileStore.Write(_path, "v2"));
        }
        finally
        {
            lockStream?.Dispose();
        }
    }
}
