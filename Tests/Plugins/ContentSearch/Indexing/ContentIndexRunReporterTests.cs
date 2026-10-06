using Lertaro.PluginSdk.Abstractions;
using Lertaro.Plugins.ContentSearch.Indexing;
using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Tests.Indexing;

// Captures the process-wide SDK notification and translation hooks.
[TestClass]
[DoNotParallelize]
public sealed class ContentIndexRunReporterTests
{
    private Func<string, string> _previousLookup = null!;
    private string _dbPath = null!;
    private ContentSearchDatabase _database = null!;
    private readonly List<NotificationRequest> _requests = [];

    [TestInitialize]
    public void CaptureNotifications()
    {
        _previousLookup = PluginSdk.Services.TranslationService.LookupFunc;
        PluginSdk.Services.TranslationService.LookupFunc = key => key.EndsWith("Message") ? $"[{key}] {{0}}" : $"[{key}]";
        _dbPath = Path.Combine(Path.GetTempPath(), $"run_reporter_{Guid.NewGuid():N}.db");
        _database = new ContentSearchDatabase(_dbPath);
        _database.Initialize();
        PluginSdk.Services.PluginNotificationService.ShowRequestFunc = (request, _) =>
        {
            _requests.Add(request);
            return null;
        };
    }

    [TestCleanup]
    public void ReleaseNotifications()
    {
        PluginSdk.Services.TranslationService.LookupFunc = _previousLookup;
        PluginSdk.Services.PluginNotificationService.ShowRequestFunc = null;
        _database.Dispose();
        File.Delete(_dbPath);
        File.Delete(_dbPath + "-wal");
        File.Delete(_dbPath + "-shm");
    }

    [TestMethod]
    public void Observe_IdleWithoutAnyIndexing_SaysNothing()
    {
        var reporter = new ContentIndexRunReporter(_database);
        reporter.Observe(false, false, false, 0);
        reporter.Observe(false, false, true, 0);
        Assert.IsEmpty(_requests);
    }

    [TestMethod]
    public void Observe_QueueDrainsDuringDiscovery_WaitsForAllBatchesAndTheFullScan()
    {
        var reporter = new ContentIndexRunReporter(_database);
        reporter.Observe(true, false, false, 0);
        reporter.Observe(false, false, false, 25, scanCompleted: false);
        Assert.IsEmpty(_requests);
        reporter.Observe(true, false, false, 25);
        reporter.Observe(false, false, false, 50, scanCompleted: true);
        reporter.Observe(false, false, false, 50);
        var summary = Assert.ContainsSingle(_requests);
        Assert.AreEqual("[ContentSearch_NotificationIndexFinishedTitle]", summary.Title);
        Assert.Contains("50", summary.Message);
        Assert.AreEqual(NotificationLevel.Info, summary.Level);
    }

    [TestMethod]
    public void Observe_IncrementalCopiesAfterCompletion_StaySilent()
    {
        var reporter = new ContentIndexRunReporter(_database);
        for (var batch = 0; batch < 100; batch++)
        {
            reporter.Observe(true, false, false, batch);
            reporter.Observe(false, false, false, batch + 1);
        }
        Assert.HasCount(1, _requests);
    }

    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void Observe_CapOrCancellation_KeepsCompletionPendingUntilResume(bool cap, bool cancelled)
    {
        var reporter = new ContentIndexRunReporter(_database);
        reporter.Observe(true, false, false, 0);
        reporter.Observe(false, cap, cancelled, 2);
        Assert.IsEmpty(_requests);
        Assert.IsFalse(_database.InitialIndexCompleted);
        reporter.Observe(true, false, false, 2);
        reporter.Observe(false, false, false, 7);
        var summary = Assert.ContainsSingle(_requests);
        Assert.Contains("7", summary.Message);
    }

    [TestMethod]
    public void Observe_RestartBeforeFirstSummary_CompletesAfterDiscoveryWithoutAnotherBatch()
    {
        _database.InsertOrUpdateFile("resumed.txt", DateTime.UtcNow, 10, "indexed text");
        using var reopened = new ContentSearchDatabase(_dbPath);
        var reporter = new ContentIndexRunReporter(reopened);
        reporter.Observe(false, false, false, reopened.CountIndexedFiles(), scanCompleted: false);
        Assert.IsEmpty(_requests);
        reporter.Observe(false, false, false, reopened.CountIndexedFiles());
        Assert.HasCount(1, _requests);
        Assert.IsTrue(reopened.InitialIndexCompleted);
    }

    [TestMethod]
    public void Observe_ExistingIndexFromBeforeCompletionTracking_DoesNotNotifyAgain()
    {
        _database.InsertOrUpdateFile("existing.txt", DateTime.UtcNow, 10, "indexed text");
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE index_state;";
            command.ExecuteNonQuery();
        }
        using var reopened = new ContentSearchDatabase(_dbPath);
        var reporter = new ContentIndexRunReporter(reopened);
        reporter.Observe(true, false, false, 1);
        reporter.Observe(false, false, false, 2);
        Assert.IsEmpty(_requests);
        Assert.IsTrue(reopened.InitialIndexCompleted);
    }

    [TestMethod]
    public void Observe_RestartThenExplicitRebuild_OnlyRebuildEnablesAnotherNotification()
    {
        var reporter = new ContentIndexRunReporter(_database);
        reporter.Observe(true, false, false, 0);
        reporter.Observe(false, false, false, 5);
        using var reopened = new ContentSearchDatabase(_dbPath);
        var restarted = new ContentIndexRunReporter(reopened);
        restarted.Observe(true, false, false, 5);
        restarted.Observe(false, false, false, 10);
        Assert.HasCount(1, _requests, "completion survives reopening the database");

        reopened.ClearAll();
        Assert.IsTrue(reopened.InitialIndexCompleted, "clear alone does not rearm completion");
        reopened.InitialIndexCompleted = false;
        restarted.Observe(true, false, false, 0);
        restarted.Observe(false, false, false, 10);
        restarted.Observe(true, false, false, 10);
        restarted.Observe(false, false, false, 11);
        Assert.HasCount(2, _requests, "rebuild grants exactly one more completion");
    }
}
