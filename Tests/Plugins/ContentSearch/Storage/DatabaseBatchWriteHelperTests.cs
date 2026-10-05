using Microsoft.Data.Sqlite;
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
                throw new SqliteException("string or blob too big", 18);
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
    [DataRow(5)] // busy
    [DataRow(8)] // readonly
    [DataRow(10)] // I/O
    [DataRow(13)] // disk full
    public void Write_DatabaseUnavailable_PropagatesWithoutSplittingOrFailingFiles(int errorCode)
    {
        var calls = 0;
        var items = new[] { Item(@"C:\Docs\a.txt"), Item(@"C:\Docs\b.txt") };
        Assert.ThrowsExactly<SqliteException>(() => DatabaseBatchWriteHelper.Write(items, _ =>
        {
            calls++;
            throw new SqliteException("database unavailable", errorCode);
        }));
        Assert.AreEqual(1, calls);
        Assert.IsEmpty(_logLines);
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
