namespace Lertaro.Core.Tests;

// KeywordHistoryStore's load helpers take explicit paths, so they can be exercised against an
// isolated temp directory (the real HistoryPath lives under Logger.UserDataDir).
[TestClass]
public sealed class KeywordHistoryStoreTests
{
    private string _dir = string.Empty;

    [TestInitialize]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "LertaroKeywordHistoryTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void TearDown()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string MainPath => Path.Combine(_dir, "keyword-history.txt");

    private string BackupPath => MainPath + ".bak";

    [TestMethod]
    public void TryReadFile_TrimsAndDedupesCaseInsensitively()
    {
        var path = MainPath;
        File.WriteAllLines(path, [" alpha ", "alpha", "Beta", " beta "]);

        var entries = KeywordHistoryStore.TryReadFile(path);

        Assert.IsNotNull(entries);
        CollectionAssert.AreEqual(new[] { "alpha", "Beta" }, entries);
    }

    [TestMethod]
    public void TryReadFile_MissingFile_ReturnsNull() => Assert.IsNull(KeywordHistoryStore.TryReadFile(MainPath));

    [TestMethod]
    public void LoadFromFiles_ValidMainWins()
    {
        File.WriteAllLines(MainPath, ["main-entry"]);
        File.WriteAllLines(BackupPath, ["backup-entry"]);

        var entries = KeywordHistoryStore.LoadFromFiles(MainPath, BackupPath);

        CollectionAssert.AreEqual(new[] { "main-entry" }, entries);
    }

    [TestMethod]
    public void LoadFromFiles_MissingMain_ReturnsEmpty()
    {
        File.WriteAllLines(BackupPath, ["backup-entry"]);

        // A missing main file is a deliberate delete (or a fresh install): the backup must not
        // resurrect the history.
        Assert.IsEmpty(KeywordHistoryStore.LoadFromFiles(MainPath, BackupPath));
    }

    [TestMethod]
    public void LoadFromFiles_UnreadableMain_FallsBackToBackup()
    {
        File.WriteAllLines(MainPath, ["main-entry"]);
        File.WriteAllLines(BackupPath, ["backup-entry"]);

        // An unreadable-by-content text file is impossible, so lock the main file exclusively: the
        // IOException path reads as "corrupt" and engages the backup fallback.
        FileStream? lockStream = null;
        try
        {
            lockStream = new FileStream(MainPath, FileMode.Open, FileAccess.Read, FileShare.None);
            CollectionAssert.AreEqual(new[] { "backup-entry" }, KeywordHistoryStore.LoadFromFiles(MainPath, BackupPath));
        }
        finally
        {
            lockStream?.Dispose();
        }
    }
}
