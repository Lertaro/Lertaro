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
    public void EffectiveScore_NinetyDays_HalvesScore()
    {
        const long now = 2_000_000_000;
        var entry = Entry(time: now - 90 * 86_400, count: 8, score: 8d);

        Assert.AreEqual(4d, SearchHistoryBucketSupport.EffectiveScore(entry, now), 1e-9);
    }

    [TestMethod]
    public void EffectiveScore_ThreeHalfLives_IsEighth()
    {
        const long now = 2_000_000_000;
        var entry = Entry(time: now - 270 * 86_400, count: 10, score: 10d);

        Assert.AreEqual(1.25d, SearchHistoryBucketSupport.EffectiveScore(entry, now), 1e-9);
    }

    [TestMethod]
    public void BuildScoreCache_RoundsToHundredth()
    {
        const long now = 2_000_000_000;
        var cache = SearchHistoryBucketSupport.BuildScoreCache(
            Buckets(Entry(time: now - 30 * 86_400, count: 1, score: 1d)), now);

        // 1 * 0.5^(1/3) = 0.7937, kept to 0.01 precision.
        Assert.AreEqual(79, cache[@"C:\path\file.txt"]);
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

    [TestMethod]
    public void NormalizeAssociationQuery_JoinsP4EligibleTerms()
    {
        Assert.AreEqual("sousuojieguo", SearchHistoryStore.NormalizeAssociationQuery("  Sousuo   Jieguo "));
        Assert.AreEqual("sousuojieguo", SearchHistoryStore.NormalizeAssociationQuery("sousuojieguo"));

        // Not every spaced query is a pinyin alternative: punctuation, single-letter terms and non-ASCII
        // text keep their spacing (P4 only joins plain all-letter terms of 2..63 characters).
        Assert.AreEqual("foo-bar baz", SearchHistoryStore.NormalizeAssociationQuery("Foo-Bar Baz"));
        Assert.AreEqual("a b", SearchHistoryStore.NormalizeAssociationQuery("A B"));
        Assert.AreEqual("设置 界面", SearchHistoryStore.NormalizeAssociationQuery("设置 界面"));
    }

    [TestMethod]
    public void TryLoadAssociationsFile_MergesEquivalentCanonicalKeys()
    {
        var path = Path.Combine(_dir, "search-query-associations.json");
        var raw = new Dictionary<string, List<SearchHistoryStore.StoredAssociationEntry>>
        {
            ["sousuo jieguo"] =
            [
                new(@"X:\Fixture\a.txt", HistoryEntryKind.File, LastOrganicTime: 100, OrganicOpens: 1),
            ],
            ["sousuojieguo"] =
            [
                new(@"X:\Fixture\a.txt", HistoryEntryKind.File, LastOrganicTime: 200, OrganicOpens: 2),
                new(@"X:\Fixture\b.txt", HistoryEntryKind.File, LastOrganicTime: 150, OrganicOpens: 1),
            ],
        };
        File.WriteAllText(path, JsonSerializer.Serialize(raw, BucketJsonOptions));

        var loaded = SearchHistoryStore.TryLoadAssociationsFile(path);
        Assert.IsNotNull(loaded);

        var rows = SearchHistoryStore.FlattenAssociations(loaded, minOrganicOpens: 1);
        Assert.HasCount(2, rows);
        Assert.IsTrue(rows.All(row => row.Keyword == "sousuojieguo"));
        var a = rows.Single(row => row.Path == @"X:\Fixture\a.txt");
        Assert.AreEqual(3, a.Count, "both spellings were separate organic confirmations");
        Assert.AreEqual(200, a.Time);
    }

    [TestMethod]
    public void MigrateAssociationsFromLegacyBuckets_ClampsOrganicOpensToAtMostOne()
    {
        var buckets = Buckets(Entry(path: @"X:\Fixture\legacy.txt", time: 500, count: 9, score: 9d));

        var migrated = SearchHistoryStore.MigrateAssociationsFromLegacyBuckets(buckets);
        var recalled = SearchHistoryStore.FlattenAssociations(migrated, SearchHistoryStore.MinAssociationOrganicOpens);
        var visible = SearchHistoryStore.FlattenAssociations(migrated, minOrganicOpens: 1);

        Assert.IsEmpty(recalled, "legacy self-reinforced counts must not cross the organic recall threshold on upgrade");
        Assert.HasCount(1, visible);
        Assert.AreEqual(1, visible[0].Count);
    }

    [TestMethod]
    public void LoadAssociationsFromFiles_BothFilesUnreadable_RebuildsFromLegacyBuckets()
    {
        var main = Path.Combine(_dir, "search-query-associations.json");
        var backup = main + ".bak";
        File.WriteAllText(main, "{ truncated");
        File.WriteAllText(backup, "{ also truncated");

        var loaded = SearchHistoryStore.LoadAssociationsFromFiles(
            main, backup, Buckets(Entry(path: @"X:\Fixture\recovered.txt", time: 500, count: 4)));

        var visible = SearchHistoryStore.FlattenAssociations(loaded, minOrganicOpens: 1);
        Assert.HasCount(1, visible, "a corrupt associations file must not end the learned-alias feature");
        Assert.AreEqual(@"X:\Fixture\recovered.txt", visible[0].Path);
        Assert.AreEqual(
            1,
            visible[0].Count,
            "recovered rows arrive at the clamped organic count, so nothing is recalled until re-confirmed");
    }

    [TestMethod]
    public void MigrateAssociationsFromLegacyBuckets_KeepsTheNewestRowsWithinTheReadCap()
    {
        // Two legacy keywords that fold into one canonical query, 12 paths each: the reader keeps 20 rows per
        // key, so a migration that writes all 24 loses four of them the first time the file is read back.
        var buckets = new Dictionary<string, List<SearchHistoryStore.StoredEntry>>(StringComparer.OrdinalIgnoreCase)
        {
            ["alpha tool"] = [.. Enumerable.Range(0, 12).Select(i => Entry($@"X:\Fixture\old\{i}.txt", time: 100 + i))],
            ["ALPHA  TOOL"] = [.. Enumerable.Range(0, 12).Select(i => Entry($@"X:\Fixture\new\{i}.txt", time: 900 + i))],
        };

        var migrated = SearchHistoryStore.MigrateAssociationsFromLegacyBuckets(buckets);
        var rows = SearchHistoryStore.FlattenAssociations(migrated, minOrganicOpens: 1);
        var cap = SearchHistoryBucketSupport.MaxEntriesPerKeyword;
        var kept = rows.Select(row => row.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.HasCount(cap, rows);
        Assert.IsTrue(
            Enumerable.Range(0, 12).All(i => kept.Contains($@"X:\Fixture\new\{i}.txt")),
            "every row of the newest keyword survives the truncation");
        Assert.HasCount(
            cap - 12,
            Enumerable.Range(0, 12).Where(i => kept.Contains($@"X:\Fixture\old\{i}.txt")).ToList(),
            "the overflow is filled by the oldest rows that still fit");
        Assert.IsTrue(
            Enumerable.Range(0, 12 - (cap - 12)).All(i => !kept.Contains($@"X:\Fixture\old\{i}.txt")),
            "what the cap drops is the oldest of the oldest, never an arbitrary tail");
    }

    [TestMethod]
    public void RecordAssociationInMemory_RequiresOrganicOpensAndIgnoresLearnedFeedback()
    {
        var associations = new Dictionary<string, List<SearchHistoryStore.StoredAssociationEntry>>(StringComparer.OrdinalIgnoreCase);

        SearchHistoryStore.RecordAssociationInMemory(associations, "  Pix   Pin ", @"X:\Fixture\Target.exe", HistoryEntryKind.File, 100, fromLearnedRecall: false);
        SearchHistoryStore.RecordAssociationInMemory(associations, "pix pin", @"X:\Fixture\Target.exe", HistoryEntryKind.File, 200, fromLearnedRecall: false);
        // Self-feedback open from a recalled row must NOT push count from 2 -> 3.
        SearchHistoryStore.RecordAssociationInMemory(associations, "pix pin", @"X:\Fixture\Target.exe", HistoryEntryKind.File, 300, fromLearnedRecall: true);

        Assert.IsEmpty(SearchHistoryStore.FlattenAssociations(associations, SearchHistoryStore.MinAssociationOrganicOpens));

        SearchHistoryStore.RecordAssociationInMemory(associations, "PIX PIN", @"X:\Fixture\Target.exe", HistoryEntryKind.File, 400, fromLearnedRecall: false);
        var confirmed = SearchHistoryStore.FlattenAssociations(associations, SearchHistoryStore.MinAssociationOrganicOpens);
        Assert.HasCount(1, confirmed);
        Assert.AreEqual("pixpin", confirmed[0].Keyword);
        Assert.AreEqual(3, confirmed[0].Count);
    }
}
