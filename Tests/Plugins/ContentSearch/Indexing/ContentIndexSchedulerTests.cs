using Lertaro.Plugins.ContentSearch.Indexing;
using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Tests.Indexing;

// Shares the process-wide PluginSdk.Logger.LogAction hook and a temp database, so it
// must not run concurrently with anything that reads or resets them.
[TestClass]
[DoNotParallelize]
public sealed class ContentIndexSchedulerTests
{
    private string _tempDbPath = null!;
    private ContentSearchDatabase _database = null!;
    private readonly List<string> _logLines = new();

    [TestInitialize]
    public void SetUp()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), "TestIndexScheduler_" + Guid.NewGuid().ToString("N") + ".db");
        _database = new ContentSearchDatabase(_tempDbPath);
        _database.Initialize();
        _logLines.Clear();
        PluginSdk.Logger.LogAction = (message, level) => _logLines.Add($"{level}: {message}");
    }

    [TestCleanup]
    public void TearDown()
    {
        PluginSdk.Logger.LogAction = null;
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

    [TestMethod]
    public async Task TriggerFullScan_FailedFile_IsNotReExtractedOnSecondScan()
    {
        // Regression for the log-spam loop: a file that fails extraction (here: OLE bytes in
        // a whitelisted .txt) used to be deleted from the index, re-discovered, and
        // re-extracted on every watcher-triggered scan, emitting the same warning forever.
        // The failed row must now stick, so the second scan produces no new warnings.
        var tempDir = Path.Combine(Path.GetTempPath(), "TestIndexScheduler_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var binaryTxt = Path.Combine(tempDir, "diskstats.txt");
        var oleBytes = new byte[512];
        oleBytes[0] = 0xD0; oleBytes[1] = 0xCF; oleBytes[2] = 0x11; oleBytes[3] = 0xE0;
        await File.WriteAllBytesAsync(binaryTxt, oleBytes);

        try
        {
            _scheduler = new ContentIndexScheduler(_database);
            var config = new ContentIndexConfig
            {
                MonitoredFolders = new List<string> { tempDir },
                AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt" }
            };
            _scheduler.Start(config);

            await WaitUntilAsync(() => _database.GetFileRecord(binaryTxt) != null);
            var warningsAfterFirstScan = CountLogLines("Skipped binary file");
            Assert.AreEqual(1, warningsAfterFirstScan,
                $"Expected exactly one skip warning after the first scan: [{string.Join("; ", _logLines)}]");

            _scheduler.TriggerFullScan();
            await WaitUntilAsync(() => _scheduler.PendingCount == 0, timeoutMs: 1500);
            await Task.Delay(400); // give the worker a beat to finish the drained batch

            Assert.AreEqual(1, CountLogLines("Skipped binary file"),
                $"Second scan must not re-extract the unchanged failed file: [{string.Join("; ", _logLines)}]");

            // Changing the file (new mtime/size) must clear the failure and retry.
            await File.WriteAllTextAsync(binaryTxt, "now it is plain readable text");
            _scheduler.TriggerFullScan();
            await WaitUntilAsync(() =>
                _database.GetFileRecord(binaryTxt) is { FailedAt: null });

            Assert.AreEqual(1, CountLogLines("Skipped binary file"),
                "The retried file now indexes as text, so no new binary-skip warning may appear");
        }
        finally
        {
            _scheduler?.Dispose();
            _scheduler = null;
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private ContentIndexScheduler? _scheduler;

    private int CountLogLines(string fragment) =>
        _logLines.Count(l => l.Contains(fragment, StringComparison.Ordinal));

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(50);
        }
    }
}
