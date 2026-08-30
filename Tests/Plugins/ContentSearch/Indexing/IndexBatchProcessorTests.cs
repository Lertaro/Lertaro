using Lertaro.Plugins.ContentSearch.Indexing;
using Lertaro.Plugins.ContentSearch.Storage;
using Lertaro.Plugins.ContentSearch.Tests.TestSupport;

namespace Lertaro.Plugins.ContentSearch.Tests.Indexing;

// Captures the process-wide PluginSdk.Logger.LogAction hook, so it must not run
// concurrently with anything that reads or resets it.
[TestClass]
[DoNotParallelize]
public sealed class IndexBatchProcessorTests
{
    private string _tempDir = null!;
    private string _tempDbPath = null!;
    private ContentSearchDatabase _database = null!;
    private readonly List<string> _logLines = new();

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TestBatchProcessor_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _tempDbPath = Path.Combine(Path.GetTempPath(), "TestBatchProcessor_" + Guid.NewGuid().ToString("N") + ".db");
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
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [TestMethod]
    public async Task ProcessBatchAsync_TextFile_IndexesContent()
    {
        var file = await WriteFileAsync("readable.txt", "indexable plain text content");
        var processor = new IndexBatchProcessor(_database);

        await processor.ProcessBatchAsync(new[] { file }, MakeConfig(), CancellationToken.None);

        var record = _database.GetFileRecord(file);
        Assert.IsNotNull(record);
        Assert.IsNull(record.FailedAt);
        Assert.HasCount(1, _database.SearchFts("indexable plain text", 10));
    }

    [TestMethod]
    public async Task ProcessBatchAsync_BinaryFileWithTextExtension_RecordsFailureWithWarning()
    {
        // OLE compound signature in a .txt: PlainTextExtractor skips it as binary,
        // and the scheduler must keep a failed row (not delete it) so the file is
        // not re-extracted on every scan.
        var file = await WriteBinaryFileAsync("diskstats.txt", [0xD0, 0xCF, 0x11, 0xE0, 0x00, 0x00, 0x00, 0x00]);
        var processor = new IndexBatchProcessor(_database);

        await processor.ProcessBatchAsync(new[] { file }, MakeConfig(), CancellationToken.None);

        var record = _database.GetFileRecord(file);
        Assert.IsNotNull(record);
        Assert.IsNotNull(record.FailedAt);
        Assert.AreEqual(new FileInfo(file).Length, record.FileSize);
        Assert.IsTrue(
            _logLines.Any(l => l.Contains("Skipped binary file", StringComparison.Ordinal) && l.Contains(file, StringComparison.Ordinal)),
            $"Expected a binary-skip warning in: [{string.Join("; ", _logLines)}]");
    }

    [TestMethod]
    public async Task ProcessBatchAsync_ImageOnlyPdf_WarnsNoExtractableText()
    {
        var file = Path.Combine(_tempDir, "scanned.pdf");
        await File.WriteAllBytesAsync(file, PdfTestDocument.SinglePage("0 0 1 rg 72 720 200 100 re f"));
        var processor = new IndexBatchProcessor(_database);

        await processor.ProcessBatchAsync(new[] { file }, MakeConfig(), CancellationToken.None);

        var record = _database.GetFileRecord(file);
        Assert.IsNotNull(record);
        Assert.IsNotNull(record.FailedAt);
        Assert.IsTrue(
            _logLines.Any(l => l.Contains("No extractable text (likely image-only PDF)", StringComparison.Ordinal) && l.Contains(file, StringComparison.Ordinal)),
            $"Expected a no-text warning in: [{string.Join("; ", _logLines)}]");
    }

    [TestMethod]
    public async Task ProcessBatchAsync_OversizedFile_KeepsFailedRowAndLogsInfo()
    {
        var file = await WriteFileAsync("huge.txt", new string('x', 2048));
        var config = MakeConfig(maxFileSizeBytes: 1024);
        var processor = new IndexBatchProcessor(_database);

        await processor.ProcessBatchAsync(new[] { file }, config, CancellationToken.None);

        var record = _database.GetFileRecord(file);
        Assert.IsNotNull(record);
        Assert.IsNotNull(record.FailedAt);
        Assert.IsFalse(
            _logLines.Any(l => l.Contains("No extractable text", StringComparison.Ordinal)),
            "An oversized skip is a policy decision, not a no-text case");
        Assert.IsTrue(
            _logLines.Any(l => l.Contains("exceeds the configured max file size", StringComparison.Ordinal)),
            $"Expected a size-skip info line in: [{string.Join("; ", _logLines)}]");
    }

    [TestMethod]
    public async Task ProcessBatchAsync_FileOutsideMonitoredFolders_DeletesRow()
    {
        var file = await WriteFileAsync("readable.txt", "indexable plain text content");
        _database.InsertOrUpdateFile(file, DateTime.UtcNow, 27, "stale content");
        var processor = new IndexBatchProcessor(_database);

        // The config points at an empty sibling folder, so the file is out of scope now.
        var otherDir = Path.Combine(_tempDir, "other");
        Directory.CreateDirectory(otherDir);
        await processor.ProcessBatchAsync(new[] { file }, MakeConfig(folder: otherDir), CancellationToken.None);

        Assert.IsNull(_database.GetFileRecord(file));
    }


    private ContentIndexConfig MakeConfig(string? folder = null, long maxFileSizeBytes = 1024 * 1024) => new()
    {
        MonitoredFolders = new List<string> { folder ?? _tempDir },
        AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt", ".pdf" },
        MaxFileSizeBytes = maxFileSizeBytes
    };

    private async Task<string> WriteFileAsync(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    private async Task<string> WriteBinaryFileAsync(string name, byte[] content)
    {
        var path = Path.Combine(_tempDir, name);
        await File.WriteAllBytesAsync(path, content);
        return path;
    }
}
