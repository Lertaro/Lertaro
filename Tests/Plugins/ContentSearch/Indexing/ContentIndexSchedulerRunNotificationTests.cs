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
    private string _tempDir = null!;
    private string _tempDbPath = null!;
    private ContentSearchDatabase _database = null!;
    private readonly List<NotificationRequest> _notifications = new();

    [TestInitialize]
    public void SetUp()
    {
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
        // Several files, so the run really does span more than one batch of extraction work; the
        // summary still arrives once, and never as a warning: a healthy run is not a problem report.
        for (var i = 0; i < 7; i++)
            await File.WriteAllTextAsync(Path.Combine(_tempDir, $"note{i}.txt"), $"plain readable text {i}");

        using var scheduler = new ContentIndexScheduler(_database);
        scheduler.Start(MakeConfig());

        await WaitUntilAsync(() => _notifications.Count > 0, timeoutMs: 15000);
        // A second card would land right after the first, so give the worker a beat to prove it does not.
        await Task.Delay(500);

        var summary = Snapshot();
        Assert.HasCount(1, summary, $"one summary per drained run: [{Describe(summary)}]");
        Assert.AreEqual(NotificationLevel.Info, summary[0].Level, "a completed run is informational");
        Assert.Contains("ContentSearch_NotificationIndexFinishedMessage", summary[0].Message);
        Assert.Contains("7", summary[0].Message, "the summary counts the files that became searchable");
    }

    [TestMethod]
    public async Task Stop_MidRun_SendsOneInterruptedSummary()
    {
        // Disabling the plugin or closing the launcher cancels the run under the user; without this
        // the queue would simply stop moving with nothing said about it.
        for (var i = 0; i < 60; i++)
            await File.WriteAllTextAsync(Path.Combine(_tempDir, $"note{i}.txt"), $"plain readable text {i}");

        var scheduler = new ContentIndexScheduler(_database);
        scheduler.Start(MakeConfig());
        await WaitUntilAsync(() => scheduler.IsIndexing, timeoutMs: 15000);

        scheduler.Dispose();

        var summary = Snapshot();
        Assert.HasCount(1, summary, $"one summary for the interrupted run: [{Describe(summary)}]");
        Assert.AreEqual(NotificationLevel.Info, summary[0].Level, "an interruption is not an error to shout about");
        Assert.Contains("ContentSearch_NotificationIndexStoppedMessage", summary[0].Message);
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
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(50);
        }
    }
}
