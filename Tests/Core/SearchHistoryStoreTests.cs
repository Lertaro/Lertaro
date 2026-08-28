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

        // A missing main file is a deliberate delete (or a fresh install): the backup must not
        // resurrect the history.
        Assert.IsEmpty(SearchHistoryStore.LoadFromFiles(MainPath, BackupPath));
    }

    [TestMethod]
    public void LoadFromFiles_BothCorrupt_ReturnsEmpty()
    {
        File.WriteAllText(MainPath, "{ truncated");
        File.WriteAllText(BackupPath, "{ also truncated");

        Assert.IsEmpty(SearchHistoryStore.LoadFromFiles(MainPath, BackupPath));
    }
}
