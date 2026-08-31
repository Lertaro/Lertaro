using Lertaro.Plugins.ContentSearch.Indexing;
using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Tests.Indexing;

// Shares the process-wide PluginSdk.Logger.LogAction hook and a temp database, so it
// must not run concurrently with anything that reads or resets them.
[TestClass]
[DoNotParallelize]
public sealed class ContentIndexSchedulerMissingObservationTests
{
    private string _tempDbPath = null!;
    private ContentSearchDatabase _database = null!;
    private readonly List<string> _logLines = new();

    [TestInitialize]
    public void SetUp()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), "TestIndexSchedulerMissing_" + Guid.NewGuid().ToString("N") + ".db");
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
    public async Task TriggerFullScan_MissingFile_IsKeptOnFirstAndSecondScans()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TestIndexSchedulerMissing_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var file = Path.Combine(tempDir, "note.txt");

        try
        {
            await File.WriteAllTextAsync(file, "indexed text");
            var originalWriteTime = DateTime.UtcNow.AddMinutes(-5);
            File.SetLastWriteTimeUtc(file, originalWriteTime);
            _database.InsertOrUpdateFile(file, originalWriteTime, new FileInfo(file).Length, "indexed text");
            File.Delete(file);

            using var scheduler = CreateScheduler(tempDir);
            scheduler.TriggerFullScan();
            await WaitUntilAsync(() => _database.GetFileRecord(file) is { MissingCount: 1 });
            Assert.AreEqual(1, _database.GetFileRecord(file)!.MissingCount);

            scheduler.TriggerFullScan();
            await WaitUntilAsync(() => _database.GetFileRecord(file) is { MissingCount: 2 });
            Assert.AreEqual(2, _database.GetFileRecord(file)!.MissingCount);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task TriggerFullScan_MissingFile_IsDeletedOnThirdScan()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TestIndexSchedulerMissing_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var file = Path.Combine(tempDir, "note.txt");

        try
        {
            await File.WriteAllTextAsync(file, "indexed text");
            _database.InsertOrUpdateFile(file, DateTime.UtcNow.AddMinutes(-5), new FileInfo(file).Length, "indexed text");
            _database.UpdateMissingCounts(new Dictionary<string, int> { [file] = 2 });
            File.Delete(file);

            using var scheduler = CreateScheduler(tempDir);
            scheduler.TriggerFullScan();

            await WaitUntilAsync(() => _database.GetFileRecord(file) == null);
            Assert.IsNull(_database.GetFileRecord(file));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task TriggerFullScan_UnreachableMonitoredFolder_DoesNotAdvanceMissingCount()
    {
        var offlineRoot = Path.Combine(Path.GetTempPath(), "TestIndexSchedulerMissing_Offline_" + Guid.NewGuid().ToString("N"));
        var offlineFile = Path.Combine(offlineRoot, "manual.pdf");
        _database.InsertOrUpdateFile(offlineFile, DateTime.UtcNow, 1024, "offline searchable text");

        using var scheduler = CreateScheduler(offlineRoot);
        scheduler.TriggerFullScan();
        await WaitUntilAsync(() => CountLogLines("Full scan completed") >= 1);

        scheduler.TriggerFullScan();
        await WaitUntilAsync(() => CountLogLines("Full scan completed") >= 2);

        var record = _database.GetFileRecord(offlineFile);
        Assert.IsNotNull(record);
        Assert.AreEqual(0, record!.MissingCount);
    }

    [TestMethod]
    public async Task TriggerFullScan_ReappearedUnchangedFile_ResetsMissingCountToZero()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TestIndexSchedulerMissing_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var file = Path.Combine(tempDir, "note.txt");

        try
        {
            await File.WriteAllTextAsync(file, "indexed text");
            var originalWriteTime = DateTime.UtcNow.AddMinutes(-5);
            File.SetLastWriteTimeUtc(file, originalWriteTime);
            var fileSize = new FileInfo(file).Length;
            _database.InsertOrUpdateFile(file, originalWriteTime, fileSize, "indexed text");
            File.Delete(file);

            using var scheduler = CreateScheduler(tempDir);
            scheduler.TriggerFullScan();
            await WaitUntilAsync(() => _database.GetFileRecord(file) is { MissingCount: 1 });

            scheduler.TriggerFullScan();
            await WaitUntilAsync(() => _database.GetFileRecord(file) is { MissingCount: 2 });

            await File.WriteAllTextAsync(file, "indexed text");
            File.SetLastWriteTimeUtc(file, originalWriteTime);

            scheduler.TriggerFullScan();
            await WaitUntilAsync(() => _database.GetFileRecord(file) is { MissingCount: 0 });

            Assert.AreEqual(0, _database.GetFileRecord(file)!.MissingCount);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private ContentIndexScheduler CreateScheduler(string monitoredFolder)
    {
        var scheduler = new ContentIndexScheduler(_database);
        scheduler.UpdateConfig(new ContentIndexConfig
        {
            MonitoredFolders = new List<string> { monitoredFolder },
            AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt", ".pdf" }
        });
        return scheduler;
    }

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
