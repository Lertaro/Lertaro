using Lertaro.Core.Indexer.NetworkDrive.Walk;

namespace Lertaro.Core.Tests.Indexer.NetworkDrive.Walk;

// Exercises TryCreateRecord/CountCreateFailure/CountError directly against a TreeBuilder instance that
// is constructed but never Run() -- the constructor only sets up a WalkFilter and an (unstarted) Channel,
// no worker threads, so calling these extension methods synchronously against real temp files is safe and
// deterministic. TreeBuilder.Run()'s own multithreaded orchestration is out of scope for unit testing.
[TestClass]
public sealed class TreeBuilderRecordExtensionsTests
{
    private static TreeBuilder CreateBuilder(string root) => new(
        new FileRecordStore(), root, root,
        new WalkOptions([], [], [], MaxDepth: 0, WorkerCount: 1, UseIgnoreFiles: false),
        CancellationToken.None, (_, _) => { });

    [TestMethod]
    public void TryCreateRecord_RegularFile_ReturnsSuccessWithFileMetadata()
    {
        using var dir = new TempDirectory();
        var filePath = Path.Combine(dir.Path, "a.txt");
        File.WriteAllText(filePath, "hi");
        var builder = CreateBuilder(dir.Path);

        var result = builder.TryCreateRecord(filePath, "", 1, out var record, out var isDirectory, out var fullPath);

        Assert.AreEqual(WalkRecordResult.Success, result);
        Assert.IsFalse(isDirectory);
        Assert.AreEqual("a.txt", record.Name);
        Assert.AreEqual(2, ((FileRecord)record).Size);
        Assert.IsFalse(record.Flags.HasFlag(FileRecordFlags.Directory));
        Assert.AreEqual("a.txt", fullPath);
    }

    [TestMethod]
    public void TryCreateRecord_Directory_ReturnsSuccessWithDirectoryFlagAndZeroSize()
    {
        using var dir = new TempDirectory();
        var subPath = Path.Combine(dir.Path, "sub");
        Directory.CreateDirectory(subPath);
        var builder = CreateBuilder(dir.Path);

        var result = builder.TryCreateRecord(subPath, "", 1, out var record, out var isDirectory, out _);

        Assert.AreEqual(WalkRecordResult.Success, result);
        Assert.IsTrue(isDirectory);
        Assert.AreEqual("sub", record.Name);
        Assert.AreEqual(0, ((FileRecord)record).Size);
        Assert.IsTrue(record.Flags.HasFlag(FileRecordFlags.Directory));
    }

    [TestMethod]
    public void TryCreateRecord_NonExistentPath_ReturnsReparsePoint()
    {
        // FileInfo.Attributes returns -1 (all bits set, including ReparsePoint) for a path that doesn't
        // exist -- so a missing file is classified the same as a real reparse point, not AttributeError.
        using var dir = new TempDirectory();
        var builder = CreateBuilder(dir.Path);

        var result = builder.TryCreateRecord(Path.Combine(dir.Path, "missing.txt"), "", 1, out _, out _, out _);

        Assert.AreEqual(WalkRecordResult.ReparsePoint, result);
    }

    [TestMethod]
    public void CountCreateFailure_AttributeError_IncrementsAttributeAndTotalErrorCounters()
    {
        using var dir = new TempDirectory();
        var builder = CreateBuilder(dir.Path);

        builder.CountCreateFailure(WalkRecordResult.AttributeError);

        Assert.AreEqual(1, builder._attributeErrors);
        Assert.AreEqual(1, builder._errors);
        Assert.AreEqual(0, builder._skippedItems);
    }

    [TestMethod]
    public void CountCreateFailure_ReparsePoint_IncrementsReparseAndSkippedCounters()
    {
        using var dir = new TempDirectory();
        var builder = CreateBuilder(dir.Path);

        builder.CountCreateFailure(WalkRecordResult.ReparsePoint);

        Assert.AreEqual(1, builder._reparseSkipped);
        Assert.AreEqual(1, builder._skippedItems);
        Assert.AreEqual(0, builder._errors);
    }

    [TestMethod]
    public void CountCreateFailure_InvalidName_OnlyIncrementsSkippedCounter()
    {
        using var dir = new TempDirectory();
        var builder = CreateBuilder(dir.Path);

        builder.CountCreateFailure(WalkRecordResult.InvalidName);

        Assert.AreEqual(1, builder._skippedItems);
        Assert.AreEqual(0, builder._errors);
        Assert.AreEqual(0, builder._reparseSkipped);
    }

    [TestMethod]
    public void CountError_IncrementsBothThePassedCounterAndTotalErrors()
    {
        using var dir = new TempDirectory();
        var builder = CreateBuilder(dir.Path);

        builder.CountError(ref builder._enumerateErrors);
        builder.CountError(ref builder._enumerateErrors);

        Assert.AreEqual(2, builder._enumerateErrors);
        Assert.AreEqual(2, builder._errors);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
