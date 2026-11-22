using Lertaro.Plugins.ContentSearch.Indexing;
using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Tests.Indexing;

// [DoNotParallelize]: the process-wide PluginSdk.Logger.LogAction hook -- see IndexBatchProcessorTests' header.
[TestClass]
[DoNotParallelize]
public sealed class ExtractionJournalTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TestExtractionJournal_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void TearDown()
    {
        PluginSdk.Logger.LogAction = null;
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [TestMethod]
    public void BeginEnd_ListsOnlyFilesStillExtracting_AndRemovesTheSidecarWhenIdle()
    {
        var sidecar = Path.Combine(_tempDir, "index.db.extracting");
        var journal = new ExtractionJournal(sidecar);

        journal.Begin(@"C:\a.pdf");
        journal.Begin(@"C:\b.pdf");
        journal.End(@"C:\a.pdf");
        CollectionAssert.AreEqual(new[] { @"C:\b.pdf" }, File.ReadAllLines(sidecar));

        journal.End(@"C:\b.pdf");
        Assert.IsFalse(File.Exists(sidecar));
    }

    [TestMethod]
    public void TakeInterrupted_ReturnsWhatAPreviousProcessLeft_Once()
    {
        var sidecar = Path.Combine(_tempDir, "index.db.extracting");
        File.WriteAllLines(sidecar, [@"C:\crashed.pdf", @"C:\lane-mate.pdf"]);
        var journal = new ExtractionJournal(sidecar);

        CollectionAssert.AreEqual(new[] { @"C:\crashed.pdf", @"C:\lane-mate.pdf" }, journal.TakeInterrupted());
        Assert.IsFalse(File.Exists(sidecar));
        Assert.IsEmpty(journal.TakeInterrupted());
    }

    // A crash between writing the temp file and swapping it in leaves the previous list intact plus a stale
    // temp file: recovery must read the intact list, and the next write must not trip over the stale one.
    [TestMethod]
    public void CrashMidWrite_LeavesThePreviousListReadable_AndTheNextWriteReplacesTheStaleTemp()
    {
        var sidecar = Path.Combine(_tempDir, "index.db.extracting");
        File.WriteAllLines(sidecar, [@"C:\crashed.pdf"]);
        File.WriteAllText(sidecar + ".tmp", @"C:\half-wri");
        var journal = new ExtractionJournal(sidecar);

        CollectionAssert.AreEqual(new[] { @"C:\crashed.pdf" }, journal.TakeInterrupted());

        journal.Begin(@"C:\next.pdf");
        CollectionAssert.AreEqual(new[] { @"C:\next.pdf" }, File.ReadAllLines(sidecar));
        Assert.IsFalse(File.Exists(sidecar + ".tmp"));
    }

    // The crash loop this exists for: the file that took the process down must not be extracted again
    // until it changes, and the row recording that must look exactly like any other failed extraction.
    [TestMethod]
    public void RecordInterruptedExtractions_WritesAFailedRowWithTheFilesRealMetadata()
    {
        var dbPath = Path.Combine(_tempDir, "index.db");
        var crashed = Path.Combine(_tempDir, "crashed.pdf");
        File.WriteAllText(crashed, "%PDF-1.7 corrupt");
        File.WriteAllLines(dbPath + ".extracting", [crashed, Path.Combine(_tempDir, "gone.pdf")]);
        var logLines = new List<string>();
        PluginSdk.Logger.LogAction = (message, _) => logLines.Add(message);
        var database = new ContentSearchDatabase(dbPath);
        try
        {
            database.Initialize();

            IndexBatchProcessor.RecordInterruptedExtractions(database);

            var record = database.GetFileRecord(crashed);
            Assert.IsNotNull(record);
            Assert.IsNotNull(record.FailedAt);
            Assert.AreEqual(new FileInfo(crashed).Length, record.FileSize);
            Assert.AreEqual(1, database.GetStats().TotalFiles, "a vanished leftover is skipped, not recorded");
            Assert.AreEqual(0, database.CountIndexedFiles());
            Assert.IsTrue(logLines.Any(l => l.Contains(crashed)));
            Assert.IsFalse(File.Exists(dbPath + ".extracting"));
        }
        finally
        {
            database.Dispose();
        }
    }
}
