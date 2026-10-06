using System.Diagnostics;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.ContentSearch.Indexing;
using Lertaro.Plugins.ContentSearch.Storage;
using Lertaro.Plugins.ContentSearch.Tests.TestSupport;

namespace Lertaro.Plugins.ContentSearch.Tests.Indexing;

// End-to-end for the one summary a finished indexing run sends: the run spans as many batches as the
// queue needs, and the user must see one card at the end of it rather than one per batch or per file.
// Shares the process-wide Logger, notification and host-enumeration hooks, so it must not run
// concurrently with anything that reads or resets them.
[TestClass]
[DoNotParallelize]
public sealed class ContentIndexSchedulerRunNotificationTests
{
    private Func<string, string> _previousLookup = null!;

    private string _tempDir = null!;
    private string _tempDbPath = null!;
    private ContentSearchDatabase _database = null!;
    private readonly List<NotificationRequest> _notifications = new();

    [TestInitialize]
    public void SetUp()
    {
        _previousLookup = TranslationService.LookupFunc;
        TranslationService.LookupFunc = key => key is
            "ContentSearch_NotificationIndexFinishedMessage" or "ContentSearch_NotificationIndexStoppedMessage" or "ContentSearch_NotificationIndexPausedMessage"
            ? $"[{key}] {{0}}" : $"[{key}]";
        _tempDir = Path.Combine(Path.GetTempPath(), "TestRunNotify_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _tempDbPath = Path.Combine(Path.GetTempPath(), "TestRunNotify_" + Guid.NewGuid().ToString("N") + ".db");
        _database = new ContentSearchDatabase(_tempDbPath);
        _database.Initialize();
        _notifications.Clear();
        PluginNotificationService.ShowRequestFunc = (request, _) =>
        {
            lock (_notifications) _notifications.Add(request);
            return null;
        };
        DirectoryIndexerService.EnumerateDirectoryFunc = LiveDirectoryEnumerator.EnumerateAsync;
    }

    [TestCleanup]
    public void TearDown()
    {
        TranslationService.LookupFunc = _previousLookup;
        PluginNotificationService.ShowRequestFunc = null;
        DirectoryIndexerService.EnumerateDirectoryFunc = null;
        _database.Dispose();
        if (File.Exists(_tempDbPath))
        {
            try { File.Delete(_tempDbPath); } catch { }
        }
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [TestMethod]
    public async Task Start_QueueDrains_SendsOneInfoSummaryForTheWholeRun()
    {
        // Seven files fit into a single batch: even a run with no second batch must open and close.
        for (var i = 0; i < 7; i++)
            await File.WriteAllTextAsync(Path.Combine(_tempDir, $"note{i}.txt"), $"plain readable text {i}");

        using var scheduler = new ContentIndexScheduler(_database);
        scheduler.Start(MakeConfig());

        await WaitUntilAsync(() => Snapshot().Count > 0, timeoutMs: 15000);
        scheduler.Stop();

        var summary = Snapshot();
        Assert.HasCount(1, summary, $"one summary per drained run: [{Describe(summary)}]");
        Assert.AreEqual(NotificationLevel.Info, summary[0].Level, "a completed run is informational");
        Assert.Contains("ContentSearch_NotificationIndexFinishedMessage", summary[0].Message);
        Assert.Contains("7", summary[0].Message, "the summary counts the files that became searchable");
    }

    [TestMethod]
    public async Task Start_IncrementalCopiesAndRestartStaySilent_RebuildNotifiesOnce()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "initial.txt"), "initial content");
        using (var scheduler = new ContentIndexScheduler(_database))
        {
            scheduler.Start(MakeConfig());
            await WaitUntilAsync(() => Snapshot().Count == 1);
            for (var batch = 0; batch < 3; batch++)
            {
                await File.WriteAllTextAsync(Path.Combine(_tempDir, $"copy{batch}.txt"), $"copied content {batch}");
                await scheduler.TriggerFullScan();
                await WaitUntilAsync(() => _database.CountIndexedFiles() == batch + 2);
            }
            scheduler.Stop();
            Assert.HasCount(1, Snapshot());
        }

        using var reopened = new ContentSearchDatabase(_tempDbPath);
        using var restarted = new ContentIndexScheduler(reopened);
        restarted.Start(MakeConfig());
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "after-restart.txt"), "new content after restart");
        await restarted.TriggerFullScan();
        await WaitUntilAsync(() => reopened.CountIndexedFiles() == 5);
        Assert.HasCount(1, Snapshot());
        restarted.RebuildIndex();
        await WaitUntilAsync(() => Snapshot().Count == 2);
        restarted.Stop();
        var notifications = Snapshot();
        Assert.HasCount(2, notifications);
        Assert.Contains("5", notifications[1].Message);
        Assert.Contains("ContentSearch_NotificationIndexFinishedMessage", notifications[1].Message);
    }

    private List<NotificationRequest> Snapshot()
    {
        lock (_notifications) return _notifications.ToList();
    }

    private static string Describe(IEnumerable<NotificationRequest> requests) =>
        string.Join("; ", requests.Select(r => $"{r.Level}:{r.Title}|{r.Message}"));

    private ContentIndexConfig MakeConfig() => new()
    {
        MonitoredFolders = new List<string> { _tempDir },
        AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt" },
        MaxFileSizeBytes = 1024 * 1024,
        MaxIndexSizeBytes = long.MaxValue
    };

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return;
            await Task.Delay(50);
        }
        Assert.Fail("The scheduler did not publish its completion within the test deadline.");
    }
}
