using System.Security.Cryptography;
using Lertaro.Plugins.ContentSearch.Indexing;
using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Tests.Indexing;

[TestClass]
public sealed class DuplicateContentResolverTests
{
    private string _tempDir = null!;
    private string _tempDbPath = null!;
    private ContentSearchDatabase _database = null!;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TestDedup_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _tempDbPath = Path.Combine(_tempDir, "test.db");
        _database = new ContentSearchDatabase(_tempDbPath);
        _database.Initialize();
    }

    [TestCleanup]
    public void TearDown()
    {
        _database.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [TestMethod]
    public async Task ComputeHashIfLarge_SmallFile_ReturnsNull()
    {
        var file = await WriteFileAsync("small.bin", 1024);
        Assert.IsNull(DuplicateContentResolver.ComputeHashIfLarge(file, 1024));
    }

    [TestMethod]
    public async Task ComputeHashIfLarge_LargeFile_ReturnsStableDigest()
    {
        var (fileA, fileB, fileC) = await WriteLargeFilesAsync();
        var hashA = DuplicateContentResolver.ComputeHashIfLarge(fileA, new FileInfo(fileA).Length);

        Assert.IsNotNull(hashA);
        Assert.AreEqual(32, hashA.Length); // XxHash128 = 16 bytes = 32 hex chars

        // Identical content under a different path hashes identically; a one-byte
        // difference must not collide.
        Assert.AreEqual(hashA, DuplicateContentResolver.ComputeHashIfLarge(fileB, new FileInfo(fileB).Length));
        Assert.AreNotEqual(hashA, DuplicateContentResolver.ComputeHashIfLarge(fileC, new FileInfo(fileC).Length));
    }

    [TestMethod]
    public void FindDuplicateSource_NullHash_ReturnsNull()
    {
        var resolver = new DuplicateContentResolver(_database);
        Assert.IsNull(resolver.FindDuplicateSource(null, @"D:\any.txt"));
    }

    [TestMethod]
    public async Task FindDuplicateSource_IndexedSourceOtherPath_ReturnsSourceId()
    {
        var (fileA, _, _) = await WriteLargeFilesAsync();
        var hash = DuplicateContentResolver.ComputeHashIfLarge(fileA, new FileInfo(fileA).Length)!;

        _database.InsertOrUpdateBatch(new[] { new FileIndexBatchItem(fileA, DateTime.UtcNow, DuplicateContentResolver.HashThresholdBytes, "duplicate text body", hash) });
        var sourceId = _database.GetFileRecord(fileA)!.Id;

        var resolver = new DuplicateContentResolver(_database);
        Assert.AreEqual(sourceId, resolver.FindDuplicateSource(hash, @"D:\other\copy.bin"));
    }

    [TestMethod]
    public async Task FindDuplicateSource_FailedOrSelfOrDuplicateRows_NeverUsedAsSource()
    {
        var (fileA, _, _) = await WriteLargeFilesAsync();
        var hash = DuplicateContentResolver.ComputeHashIfLarge(fileA, new FileInfo(fileA).Length)!;
        var resolver = new DuplicateContentResolver(_database);

        // A failed row has no text to reuse.
        _database.InsertOrUpdateBatch(new[] { new FileIndexBatchItem(fileA, DateTime.UtcNow, 1, string.Empty, hash) });
        Assert.IsNull(resolver.FindDuplicateSource(hash, @"D:\other\copy.bin"));

        // A row acting as a duplicate (content_ref set) cannot serve as the source
        // for a third copy: only the source row itself is a valid reuse target. While
        // the source exists it stays the match, and once it is deleted (with its
        // duplicates cascaded away) the hash no longer resolves to anything.
        _database.InsertOrUpdateBatch(new[] { new FileIndexBatchItem(fileA, DateTime.UtcNow, 1, "real text", hash) });
        var sourceId = _database.GetFileRecord(fileA)!.Id;
        _database.InsertOrUpdateBatch(new[] { new FileIndexBatchItem(@"D:\first\copy.bin", DateTime.UtcNow, 1, string.Empty, hash, sourceId) });
        Assert.AreEqual(sourceId, resolver.FindDuplicateSource(hash, @"D:\other\copy.bin"));

        // The source itself must not match under its own path.
        Assert.IsNull(resolver.FindDuplicateSource(hash, fileA));

        _database.DeleteFilesBatch(new[] { fileA, @"D:\first\copy.bin" });
        Assert.IsNull(resolver.FindDuplicateSource(hash, @"D:\other\copy.bin"));
    }

    private async Task<string> WriteFileAsync(string name, int length)
    {
        var path = Path.Combine(_tempDir, name);
        var bytes = RandomNumberGenerator.GetBytes(length);
        await File.WriteAllBytesAsync(path, bytes);
        return path;
    }

    /// <summary>Two identical large files and one differing by a single byte.</summary>
    private async Task<(string A, string B, string C)> WriteLargeFilesAsync()
    {
        var payload = RandomNumberGenerator.GetBytes((int)DuplicateContentResolver.HashThresholdBytes + 1024);

        var fileA = Path.Combine(_tempDir, "a.bin");
        var fileB = Path.Combine(_tempDir, "b.bin");
        var fileC = Path.Combine(_tempDir, "c.bin");
        await File.WriteAllBytesAsync(fileA, payload);
        await File.WriteAllBytesAsync(fileB, payload);
        payload[0] ^= 0xFF;
        await File.WriteAllBytesAsync(fileC, payload);
        return (fileA, fileB, fileC);
    }
}
