using System.Text.Json;
using System.Text.Json.Serialization;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Core.Tests;

[TestClass]
public sealed class SearchHistoryStoreTests
{
    private string _dir = string.Empty;

    [TestInitialize]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "LertaroSearchHistoryTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void TearDown()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static readonly JsonSerializerOptions BucketJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string BucketJson(string keyword, string path) =>
        JsonSerializer.Serialize(
            new Dictionary<string, List<SearchHistoryStore.StoredEntry>>
            {
                [keyword] = [new(path, HistoryEntryKind.File, 123)]
            },
            BucketJsonOptions);

    private string MainPath => Path.Combine(_dir, "search-history.json");

    private string BackupPath => MainPath + ".bak";

    // One keyword bucket holding exactly these entries, so the score helpers can be fed without a file.
    private static Dictionary<string, List<SearchHistoryStore.StoredEntry>> Buckets(
        params SearchHistoryStore.StoredEntry[] entries)
        => SearchHistoryBucketSupport.BuildBuckets(entries.Select(e => ("kw", e)));

    private static SearchHistoryStore.StoredEntry Entry(
        string path = @"C:\path\file.txt", long time = 123, int count = 1, double score = 0) =>
        new(path, HistoryEntryKind.File, time, count, score);

    [TestMethod]
    public void ExistsForKind_File_OnlyUsesFileProbe()
    {
        var directoryProbed = false;

        var exists = SearchHistoryStore.ExistsForKind("item", HistoryEntryKind.File, _ => true, _ => directoryProbed = true);

        Assert.IsTrue(exists);
        Assert.IsFalse(directoryProbed);
    }

    [TestMethod]
    public void ExistsForKind_Folder_OnlyUsesDirectoryProbe()
    {
        var fileProbed = false;

        var exists = SearchHistoryStore.ExistsForKind("item", HistoryEntryKind.Folder, _ => fileProbed = true, _ => true);

        Assert.IsTrue(exists);
        Assert.IsFalse(fileProbed);
    }

    [TestMethod]
    public void ExistsForKind_Application_DoesNotProbeTheFilesystem()
    {
        var probed = false;

        var exists = SearchHistoryStore.ExistsForKind("item", HistoryEntryKind.Application, _ => probed = true, _ => probed = true);

        Assert.IsTrue(exists);
        Assert.IsFalse(probed);
    }

    [TestMethod]
    public void NormalizePath_WslPathUsesLexicalNormalization()
    {
        var path = @"\\wsl$\Ubuntu/home/testuser/~cache/";

        Assert.AreEqual(@"\\wsl$\Ubuntu\home\testuser\~cache", SearchHistoryStore.NormalizePath(path));
    }

    [TestMethod]
    public void TryLoadFile_ValidJson_ReturnsBuckets()
    {
        var path = MainPath;
        File.WriteAllText(path, BucketJson("kw", @"C:\path\file.txt"));

        var buckets = SearchHistoryStore.TryLoadFile(path);

        Assert.IsNotNull(buckets);
        Assert.HasCount(1, buckets);
        Assert.HasCount(1, buckets["kw"]);
        Assert.AreEqual(@"C:\path\file.txt", buckets["kw"][0].Path);
    }

    [TestMethod]
    public void TryLoadFile_EntryWithoutCountDefaultsToOne()
    {
        // A JSON written before the count field existed deserializes to the record's default of 1,
        // so an upgraded store never shows an entry as zero-use.
        var path = MainPath;
        File.WriteAllText(path, """{ "kw": [ { "path": "C:\\path\\file.txt", "type": "File", "time": 123 } ] }""");

        var buckets = SearchHistoryStore.TryLoadFile(path);

        Assert.IsNotNull(buckets);
        Assert.AreEqual(1, buckets["kw"][0].Count);
    }

    [TestMethod]
    public void TryLoadFile_EntryWithCountPreservesIt()
    {
        var path = MainPath;
        File.WriteAllText(path, """{ "kw": [ { "path": "C:\\path\\file.txt", "type": "File", "time": 123, "count": 5 } ] }""");

        var buckets = SearchHistoryStore.TryLoadFile(path);

        Assert.IsNotNull(buckets);
        Assert.AreEqual(5, buckets["kw"][0].Count);
    }

    [TestMethod]
    public void TryLoadFile_CorruptJson_ReturnsNull()
    {
        var path = MainPath;
        File.WriteAllText(path, "{ truncated");

        Assert.IsNull(SearchHistoryStore.TryLoadFile(path));
    }

    [TestMethod]
    public void TryLoadFile_NullJson_ReturnsNull()
    {
        var path = MainPath;
        File.WriteAllText(path, "null");

        // A literal "null" file parses to a null dictionary and must read as "corrupt" (engaging the
        // backup fallback), not as a valid empty store.
        Assert.IsNull(SearchHistoryStore.TryLoadFile(path));
    }

    [TestMethod]
    public void LoadFromFiles_CorruptMain_FallsBackToBackup()
    {
        File.WriteAllText(MainPath, "{ truncated");
        File.WriteAllText(BackupPath, BucketJson("backup-kw", @"C:\backup\file.txt"));

        var buckets = SearchHistoryStore.LoadFromFiles(MainPath, BackupPath);

        Assert.IsTrue(buckets.ContainsKey("backup-kw"));
    }

    [TestMethod]
    public void LoadFromFiles_ValidMainWins()
    {
        File.WriteAllText(MainPath, BucketJson("main-kw", @"C:\main\file.txt"));
        File.WriteAllText(BackupPath, BucketJson("backup-kw", @"C:\backup\file.txt"));

        var buckets = SearchHistoryStore.LoadFromFiles(MainPath, BackupPath);

        Assert.IsTrue(buckets.ContainsKey("main-kw"));
        Assert.IsFalse(buckets.ContainsKey("backup-kw"));
    }

    [TestMethod]
    public void LoadFromFiles_MissingMain_ReturnsEmpty()
    {
        File.WriteAllText(BackupPath, BucketJson("backup-kw", @"C:\backup\file.txt"));

        // A missing main file is a deliberate delete: the backup must not resurrect history (canonical: KeywordHistoryStoreTests).
        Assert.IsEmpty(SearchHistoryStore.LoadFromFiles(MainPath, BackupPath));
    }

    [TestMethod]
    public void LoadFromFiles_BothCorrupt_ReturnsEmpty()
    {
        File.WriteAllText(MainPath, "{ truncated");
        File.WriteAllText(BackupPath, "{ also truncated");

        Assert.IsEmpty(SearchHistoryStore.LoadFromFiles(MainPath, BackupPath));
    }

    [TestMethod]
    public void TryLoadFile_LegacyEntryWithoutScore_MigratesFromCount()
    {
        var path = MainPath;
        File.WriteAllText(path, """{ "kw": [ { "path": "C:\\path\\file.txt", "type": "File", "time": 123, "count": 7 } ] }""");

        var buckets = SearchHistoryStore.TryLoadFile(path);

        Assert.IsNotNull(buckets);
        Assert.AreEqual(7d, buckets["kw"][0].Score);

        // Reading the same file again cannot drift, and neither can re-reading what the migration wrote.
        var again = SearchHistoryStore.TryLoadFile(path);
        Assert.IsNotNull(again);
        Assert.AreEqual(buckets["kw"][0].Score, again["kw"][0].Score);

        var migratedPath = BackupPath;
        File.WriteAllText(migratedPath, JsonSerializer.Serialize(buckets, BucketJsonOptions));
        var reloaded = SearchHistoryStore.TryLoadFile(migratedPath);
        Assert.IsNotNull(reloaded);
        Assert.AreEqual(7d, reloaded["kw"][0].Score);
    }

    [TestMethod]
    public void TryLoadFile_LegacyCountAboveScoreCap_ClampsToTen()
    {
        var path = MainPath;
        File.WriteAllText(path, """{ "kw": [ { "path": "C:\\path\\file.txt", "type": "File", "time": 123, "count": 25 } ] }""");

        var buckets = SearchHistoryStore.TryLoadFile(path);

        Assert.IsNotNull(buckets);
        Assert.AreEqual(10d, buckets["kw"][0].Score);
    }

    [TestMethod]
    public void TryLoadFile_StoredScore_IsPreserved()
    {
        var path = MainPath;
        File.WriteAllText(path,
            """{ "kw": [ { "path": "C:\\path\\file.txt", "type": "File", "time": 123, "count": 3, "score": 4.5 } ] }""");

        var buckets = SearchHistoryStore.TryLoadFile(path);

        Assert.IsNotNull(buckets);
        Assert.AreEqual(4.5d, buckets["kw"][0].Score, 1e-9);
        Assert.AreEqual(3, buckets["kw"][0].Count);
    }

    [TestMethod]
    public void TryLoadFile_ScoreAboveCap_ClampsToTen()
    {
        var path = MainPath;
        File.WriteAllText(path,
            """{ "kw": [ { "path": "C:\\path\\file.txt", "type": "File", "time": 123, "count": 3, "score": 12 } ] }""");

        var buckets = SearchHistoryStore.TryLoadFile(path);

        Assert.IsNotNull(buckets);
        Assert.AreEqual(10d, buckets["kw"][0].Score, 1e-9);
    }

    [TestMethod]
    public void EffectiveScore_CountLeads_RecencyOnlyTrims()
    {
        // The weight is the open count. Recency may only scale it, and only by RecencyWeight: an entry used
        // long ago must still outweigh one that was never used, which is the whole point of keeping history
        // for the things a user opens rarely.
        const long now = 2_000_000_000;
        var recent = Entry(time: now, count: 5, score: 5d);
        var longAgo = Entry(time: now - 3650 * 86_400, count: 5, score: 5d);

        var recentScore = SearchHistoryBucketSupport.EffectiveScore(recent, now);
        var longAgoScore = SearchHistoryBucketSupport.EffectiveScore(longAgo, now);

        Assert.IsTrue(recentScore > longAgoScore, "a recent entry must outweigh an old one at equal count");
        Assert.IsTrue(longAgoScore > 0d, "an old entry must never decay to zero");
        Assert.IsTrue(longAgoScore / recentScore >= 1d - SearchHistoryBucketSupport.RecencyWeight - 1e-9,
            "recency must not cost more than its configured share of the weight");
    }

    [TestMethod]
    public void EffectiveScore_ScalesWithCount()
    {
        const long now = 2_000_000_000;
        var once = Entry(time: now, count: 1, score: 1d);
        var tenTimes = Entry(time: now, count: 10, score: 10d);

        var onceScore = SearchHistoryBucketSupport.EffectiveScore(once, now);
        var tenTimesScore = SearchHistoryBucketSupport.EffectiveScore(tenTimes, now);

        Assert.AreEqual(10d * onceScore, tenTimesScore, 1e-9, "count must scale the weight linearly");
    }

    [TestMethod]
    public void EffectiveScore_NoCount_IsZero()
    {
        const long now = 2_000_000_000;
        var never = Entry(time: now, count: 0, score: 0d);

        Assert.AreEqual(0d, SearchHistoryBucketSupport.EffectiveScore(never, now), 1e-9,
            "a row the user never opened must carry no history weight at all");
    }

    [TestMethod]
    public void BuildScoreCache_RoundsToHundredth()
    {
        const long now = 2_000_000_000;
        var cache = SearchHistoryBucketSupport.BuildScoreCache(
            Buckets(Entry(time: now - 30 * 86_400, count: 1, score: 1d)), now);

        // One open, thirty days ago: 1 * (0.85 + 0.15 * 0.5^(30/365)) = 0.9917, kept to 0.01 precision.
        Assert.AreEqual(99, cache[@"C:\path\file.txt"]);
    }

    [TestMethod]
    public void BuildScoreCache_FutureTimestamp_DoesNotInflate()
    {
        const long now = 2_000_000_000;
        var cache = SearchHistoryBucketSupport.BuildScoreCache(
            Buckets(Entry(time: now + 90 * 86_400, count: 5, score: 5d)), now);

        Assert.AreEqual(500, cache[@"C:\path\file.txt"]);
    }

    [TestMethod]
    public void BuildScoreCache_KeysAreCaseInsensitive()
    {
        const long now = 2_000_000_000;
        var cache = SearchHistoryBucketSupport.BuildScoreCache(
            Buckets(Entry(path: @"C:\A\x.txt", time: now, count: 3, score: 2d)), now);

        Assert.IsTrue(cache.TryGetValue(@"c:\a\X.TXT", out var value));
        Assert.AreEqual(200, value);
    }

    [TestMethod]
    public void BuildScoreCache_EmptyBuckets_IsEmpty()
    {
        var cache = SearchHistoryBucketSupport.BuildScoreCache(
            new Dictionary<string, List<SearchHistoryStore.StoredEntry>>(StringComparer.OrdinalIgnoreCase),
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        Assert.IsEmpty(cache);
    }
}
