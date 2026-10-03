using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Tests.Storage;

// Covers the batch write phase: intra-batch duplicate resolution, and the failure isolation that
// keeps one item the database refuses (in production, a document whose text trips
// SQLITE_TOOBIG) from discarding the healthy files written in the same batch.
// Captures the process-wide PluginSdk.Logger.LogAction hook, so it must not run concurrently with
// anything that reads or resets it.
[TestClass]
[DoNotParallelize]
public sealed class DatabaseBatchWriteHelperTests
{
    private const string PoisonPath = @"C:\Docs\too-big.txt";
    private readonly List<string> _logLines = new();

    [TestInitialize]
    public void SetUp()
    {
        _logLines.Clear();
        PluginSdk.Logger.LogAction = (message, level) => _logLines.Add($"{level}: {message}");
    }

    [TestCleanup]
    public void TearDown() => PluginSdk.Logger.LogAction = null;

    [TestMethod]
    public void Write_CleanBatch_HandsEveryItemToTheWriterOnce()
    {
        // All-or-nothing on the normal path: one call, one transaction, no halving.
        var items = new[] { Item(@"C:\Docs\a.txt"), Item(@"C:\Docs\b.txt") };
        var calls = new List<IReadOnlyList<FileIndexBatchItem>>();

        var failed = DatabaseBatchWriteHelper.Write(items, chunk =>
        {
            calls.Add(chunk);
            return chunk.ToDictionary(i => i.Path, _ => 1L);
        });

        Assert.IsEmpty(failed);
        Assert.IsEmpty(_logLines);
        Assert.HasCount(1, calls, "a batch that writes cleanly must not be split");
        Assert.HasCount(2, calls[0]);
    }

    [TestMethod]
    public void Write_OneItemRefused_ItsNeighboursStillLandAndItIsReportedAsFailed()
    {
        var items = new[] { Item(PoisonPath), Item(@"C:\Docs\healthy.txt") };
        var written = new List<string>();

        var failed = DatabaseBatchWriteHelper.Write(items, chunk =>
        {
            if (chunk.Any(i => i.Path == PoisonPath))
                throw new InvalidOperationException("string or blob too big");
            written.AddRange(chunk.Select(i => i.Path));
            return chunk.ToDictionary(i => i.Path, _ => 1L);
        });

        CollectionAssert.AreEqual(
            new[] { @"C:\Docs\healthy.txt" },
            written,
            "only the chunk holding the refused item may be abandoned");
        Assert.HasCount(1, failed);
        Assert.AreEqual(PoisonPath, failed[0].Path);
        // This is the shape the caller records as a failed row: no text, no duplicate reference.
        Assert.AreEqual(string.Empty, failed[0].Content);
        Assert.IsNull(failed[0].ContentHash);
        Assert.IsNull(failed[0].ContentRef);
        Assert.IsTrue(
            _logLines.Any(l => l.Contains(PoisonPath, StringComparison.Ordinal)),
            $"Expected a warning naming the refused file in: [{string.Join("; ", _logLines)}]");
    }

    [TestMethod]
    public void Write_EveryItemRefused_ReportsOneBatchLineAndStillReturnsThemAllAsFailed()
    {
        // A database refusing every write (out of disk space) is one condition, not one condition
        // per file: the batch is halved down and every item comes back failed, but the log says so
        // once. The failed-row write that follows is what surfaces a database that is still broken.
        var items = new[] { Item(@"C:\Docs\a.txt"), Item(@"C:\Docs\b.txt") };

        var failed = DatabaseBatchWriteHelper.Write(items, _ => throw new InvalidOperationException("disk I/O error"));

        Assert.HasCount(2, failed);
        Assert.HasCount(1, _logLines, $"one line for the batch, not one per file: [{string.Join("; ", _logLines)}]");
        Assert.IsTrue(_logLines[0].Contains("None of the 2 file(s)", StringComparison.Ordinal), _logLines[0]);
    }

    [TestMethod]
    public void Write_CancelledChunk_PropagatesInsteadOfRecordingTheItemsAsFailed()
    {
        // Cancellation voids a batch: turning it into failed rows would delete nothing (a failed
        // row keeps the file out of the index) exactly when the app is shutting down.
        var items = new[] { Item(@"C:\Docs\a.txt"), Item(@"C:\Docs\b.txt") };

        Assert.ThrowsExactly<OperationCanceledException>(
            () => DatabaseBatchWriteHelper.Write(items, _ => throw new OperationCanceledException()));

        Assert.IsEmpty(_logLines, "a cancelled batch is reported by the caller, not per file");
    }

    [TestMethod]
    public void Write_DuplicatesInOneBatch_ReferenceTheSourceWrittenInTheSameCall()
    {
        var source = new FileIndexBatchItem(@"C:\Docs\source.txt", DateTime.UtcNow, 40, "same payload", "hash-a");
        var copy = new FileIndexBatchItem(@"C:\Docs\copy.txt", DateTime.UtcNow, 40, "same payload", "hash-a");
        var calls = new List<IReadOnlyList<FileIndexBatchItem>>();

        var failed = DatabaseBatchWriteHelper.Write([source, copy], chunk =>
        {
            calls.Add(chunk);
            return chunk.ToDictionary(i => i.Path, i => i.Path == source.Path ? 7L : 8L);
        });

        Assert.IsEmpty(failed);
        Assert.HasCount(2, calls);
        Assert.HasCount(1, calls[0]);
        Assert.AreEqual(source.Path, calls[0][0].Path, "the first item for a content hash stays the source row");
        // The duplicate owns no text of its own: it points at the row id the same call just wrote.
        Assert.AreEqual(copy.Path, calls[1][0].Path);
        Assert.AreEqual(string.Empty, calls[1][0].Content);
        Assert.AreEqual(7L, calls[1][0].ContentRef);
    }

    private static FileIndexBatchItem Item(string path) => new(path, DateTime.UtcNow, 40, "payload " + path);
}
