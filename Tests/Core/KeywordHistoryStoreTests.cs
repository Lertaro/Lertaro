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
        CollectionAssert.AreEqual(new[] { "alpha", "Beta" }, entries.Select(e => e.Keyword).ToArray());
    }

    [TestMethod]
    public void TryReadFile_TabCountIsParsed()
    {
        var path = MainPath;
        File.WriteAllLines(path, ["alpha\t3", "beta\t7"]);

        var entries = KeywordHistoryStore.TryReadFile(path);

        Assert.IsNotNull(entries);
        Assert.AreEqual(3, entries[0].Count);
        Assert.AreEqual(7, entries[1].Count);
    }

    [TestMethod]
    public void TryReadFile_LineWithoutTabDefaultsToOne()
    {
        // Old-format files wrote a bare keyword per line; those read as one use so the count never
        // looks like zero and the entry stays clearable/visible.
        var path = MainPath;
        File.WriteAllLines(path, ["legacy-keyword"]);

        var entries = KeywordHistoryStore.TryReadFile(path);

        Assert.IsNotNull(entries);
        Assert.AreEqual(1, entries[0].Count);
    }

    [TestMethod]
    public void TryReadFile_InvalidOrZeroCount_CoercedToOne()
    {
        var path = MainPath;
        File.WriteAllLines(path, ["alpha\t0", "beta\tgarbage", "gamma\t-2"]);

        var entries = KeywordHistoryStore.TryReadFile(path);

        Assert.IsNotNull(entries);
        Assert.AreEqual(1, entries[0].Count);
        Assert.AreEqual(1, entries[1].Count);
        Assert.AreEqual(1, entries[2].Count);
    }

    [TestMethod]
    public void TryReadFile_MissingFile_ReturnsNull() => Assert.IsNull(KeywordHistoryStore.TryReadFile(MainPath));

    [TestMethod]
    public void LoadFromFiles_ValidMainWins()
    {
        File.WriteAllLines(MainPath, ["main-entry"]);
        File.WriteAllLines(BackupPath, ["backup-entry"]);

        var entries = KeywordHistoryStore.LoadFromFiles(MainPath, BackupPath);

        CollectionAssert.AreEqual(new[] { "main-entry" }, entries.Select(e => e.Keyword).ToArray());
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
            var entries = KeywordHistoryStore.LoadFromFiles(MainPath, BackupPath);
            CollectionAssert.AreEqual(new[] { "backup-entry" }, entries.Select(e => e.Keyword).ToArray());
        }
        finally
        {
            lockStream?.Dispose();
        }
    }
}
