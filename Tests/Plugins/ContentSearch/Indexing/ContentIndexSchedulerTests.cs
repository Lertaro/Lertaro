using Lertaro.Plugins.ContentSearch.Indexing;
using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Tests.Indexing;

[TestClass]
public sealed class ContentIndexSchedulerTests
{
    private string _tempDbPath = null!;
    private ContentSearchDatabase _database = null!;

    [TestInitialize]
    public void SetUp()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), "TestIndexScheduler_" + Guid.NewGuid().ToString("N") + ".db");
        _database = new ContentSearchDatabase(_tempDbPath);
        _database.Initialize();
    }

    [TestCleanup]
    public void TearDown()
    {
        _database.Dispose();
        if (File.Exists(_tempDbPath))
        {
            try { File.Delete(_tempDbPath); } catch { }
        }
    }

    [TestMethod]
    public void IsFileInMonitoredFolders_CorrectlyValidatesPaths()
    {
        using var scheduler = new ContentIndexScheduler(_database);
        var config = new ContentIndexConfig
        {
            MonitoredFolders = new List<string> { @"C:\MyDocs", @"D:\Workspace\Projects" }
        };
        scheduler.UpdateConfig(config);

        Assert.IsTrue(scheduler.IsFileInMonitoredFolders(@"C:\MyDocs\test.txt"));
        Assert.IsTrue(scheduler.IsFileInMonitoredFolders(@"C:\MyDocs\SubDir\document.docx"));
        Assert.IsTrue(scheduler.IsFileInMonitoredFolders(@"D:\Workspace\Projects\src\App.cs"));

        Assert.IsFalse(scheduler.IsFileInMonitoredFolders(@"C:\OtherFolder\file.txt"));
        Assert.IsFalse(scheduler.IsFileInMonitoredFolders(@"C:\MyDocsOther\file.txt"));
        Assert.IsFalse(scheduler.IsFileInMonitoredFolders(@"Z:\Data\test.md"));
    }

    [TestMethod]
    public void IsFileInMonitoredFolders_DriveRootPaths_CorrectlyNormalized()
    {
        using var scheduler = new ContentIndexScheduler(_database);
        var config = new ContentIndexConfig
        {
            MonitoredFolders = new List<string> { @"c:\", @"D:" }
        };
        scheduler.UpdateConfig(config);

        Assert.IsTrue(scheduler.IsFileInMonitoredFolders(@"C:\Windows\System32\drivers\etc\hosts"));
        Assert.IsTrue(scheduler.IsFileInMonitoredFolders(@"D:\Projects\App.cs"));
        Assert.IsFalse(scheduler.IsFileInMonitoredFolders(@"Z:\Data\test.txt"));
    }

    [TestMethod]
    public void NormalizeFolderPath_ShellVirtualPath_ResolvesToPhysicalFolder()
    {
        var resolved = ContentIndexScheduler.NormalizeFolderPath("shell:Personal");

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        Assert.IsFalse(string.IsNullOrEmpty(documents));
        Assert.AreEqual(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(documents)),
            resolved,
            ignoreCase: true);
    }

    [TestMethod]
    public void IsFileInMonitoredFolders_ShellVirtualPathEntry_MatchesPhysicalFiles()
    {
        using var scheduler = new ContentIndexScheduler(_database);
        var config = new ContentIndexConfig
        {
            MonitoredFolders = new List<string> { "shell:Personal" }
        };
        scheduler.UpdateConfig(config);

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        Assert.IsTrue(scheduler.IsFileInMonitoredFolders(Path.Combine(documents, "note.txt")));
        Assert.IsFalse(scheduler.IsFileInMonitoredFolders(@"C:\OtherFolder\file.txt"));
    }

    [TestMethod]
    public void GetExtractorParallelism_ScalesWithCoresAndCapsAtFour()
    {
        // Half the cores, minimum one lane so indexing still progresses on 1-2 core machines,
        // capped at four so high-core machines do not over-parallelize the CPU-bound parsing.
        Assert.AreEqual(1, ContentIndexScheduler.GetExtractorParallelism(1));
        Assert.AreEqual(1, ContentIndexScheduler.GetExtractorParallelism(2));
        Assert.AreEqual(2, ContentIndexScheduler.GetExtractorParallelism(4));
        Assert.AreEqual(3, ContentIndexScheduler.GetExtractorParallelism(6));
        Assert.AreEqual(4, ContentIndexScheduler.GetExtractorParallelism(8));
        Assert.AreEqual(4, ContentIndexScheduler.GetExtractorParallelism(32));
    }

    [TestMethod]
    public void TriggerFullScan_DisallowedExtensions_PrunedFromDatabaseImmediately()
    {
        _database.InsertOrUpdateFile(@"C:\MyDocs\doc1.pdf", DateTime.UtcNow, 1024, "PDF text");
        _database.InsertOrUpdateFile(@"C:\MyDocs\doc2.txt", DateTime.UtcNow, 512, "TXT text");

        Assert.AreEqual(2, _database.GetStats().TotalFiles);

        using var scheduler = new ContentIndexScheduler(_database);
        var config = new ContentIndexConfig
        {
            MonitoredFolders = new List<string> { @"C:\MyDocs" },
            AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt" } // PDF disallowed
        };
        scheduler.UpdateConfig(config);
        scheduler.TriggerFullScan();

        Thread.Sleep(300);

        Assert.IsNull(_database.GetFileRecord(@"C:\MyDocs\doc1.pdf"));
    }
}
