using System.ComponentModel;

using Lertaro.Core.Indexer.NetworkDrive.Walk;

namespace Lertaro.Core.Tests.Indexer.NetworkDrive.Walk;

[TestClass]
public sealed class TreeBuilderEnumerationExtensionsTests
{
    // Regression coverage: a directory that fails WHILE being iterated (FindNextFile mid-listing, e.g. a
    // share that dropped) must cost exactly that one directory -- counted as an enumeration error, left
    // un-Listed so a later pass re-lists it, and never thrown out of the walker. Letting it escape faults
    // the worker, which used to end the drive's whole scan (TreeBuilderTests covers the channel half).
    [TestMethod]
    public void ConsumeChildren_MidIterationFailure_LeavesDirectoryUnlistedAndKeepsWalking()
    {
        using var dir = new TempDirectory();
        var sub = Path.Combine(dir.Path, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "kept.txt"), "x");
        var builder = CreateBuilder(dir.Path);
        builder._store.Records.Add(new FileRecord(1, 1, string.Empty, FileRecordFlags.Directory | FileRecordFlags.SourceRoot));
        builder._store.Records.Add(new FileRecord(2, 1, "sub", FileRecordFlags.Directory));
        builder._store.Records.Add(new FileRecord(3, 1, "broken", FileRecordFlags.Directory));
        builder.RegisterDirectoryIndices(0, builder._store.Records);

        builder.ConsumeChildren(
            new WorkItem(dir.Path, dir.Path, 3, 1, NetworkIgnoreRuleSet.Empty), NetworkIgnoreRuleSet.Empty, FailingMidway());

        Assert.AreEqual(1, builder._enumerateErrors);
        Assert.AreEqual(1, builder._errors);
        Assert.IsFalse(IsListed(builder, 3), "a directory whose listing failed must stay un-Listed so a future pass re-lists it");
        // The children captured before the failure are real and were already counted as indexed, so they
        // are kept -- the un-Listed directory above is what stops any later pass trusting them wholesale.
        CollectionAssert.Contains(builder._store.Records.Select(r => r.Name).ToList(), "captured.txt");

        // And the walk continues: the next work item is processed and marked Listed as usual.
        builder.ConsumeChildren(
            new WorkItem(sub, sub, 2, 1, NetworkIgnoreRuleSet.Empty), NetworkIgnoreRuleSet.Empty, NativeFileEnumerator.Enumerate(sub));

        Assert.IsTrue(IsListed(builder, 2), "the work item after the failing one must still be walked and marked Listed");
        Assert.AreEqual(1, builder._enumerateErrors);
    }

    // The other half of the same handling: a directory that fails to OPEN. The retry budget itself is not
    // asserted here (its whole point is a real multi-second backoff), only that a final failure is handed
    // back with the real exception rather than swallowed -- that is what CountEnumerationFailure turns into
    // the counted, logged, un-Listed directory.
    [TestMethod]
    public void TryEnumerateChildren_UnreachableDirectory_SurfacesTheRealFailure()
    {
        using var dir = new TempDirectory();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var builder = CreateBuilder(dir.Path, cancelled.Token);

        var enumerated = builder.TryEnumerateChildren(Path.Combine(dir.Path, "missing"), out _, out var failure);

        Assert.IsFalse(enumerated);
        Assert.IsInstanceOfType<Win32Exception>(failure);
    }

    // Cancellation is not an enumeration failure: it has to keep propagating, so the worker exits as
    // cancelled (DedicatedWorkerThread) instead of being recorded as one more bad directory.
    [TestMethod]
    public void ConsumeChildren_CancelledWalk_StillThrowsAndCountsNothing()
    {
        using var dir = new TempDirectory();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var builder = CreateBuilder(dir.Path, cancelled.Token);
        builder._store.Records.Add(new FileRecord(1, 1, string.Empty, FileRecordFlags.Directory | FileRecordFlags.SourceRoot));
        builder.RegisterDirectoryIndices(0, builder._store.Records);

        Assert.ThrowsExactly<OperationCanceledException>(() => builder.ConsumeChildren(
            new WorkItem(dir.Path, dir.Path, 1, 0, NetworkIgnoreRuleSet.Empty),
            NetworkIgnoreRuleSet.Empty,
            [new NativeFileEntry("a.txt", FileAttributes.Normal, 1, 0, 0, 0)]));

        Assert.AreEqual(0, builder._enumerateErrors);
    }

    private static TreeBuilder CreateBuilder(string root, CancellationToken token = default) => new(
        new FileRecordStore(), root, root,
        new WalkOptions([], [], [], MaxDepth: 0, WorkerCount: 1, UseIgnoreFiles: false),
        token, (_, _) => { });

    private static bool IsListed(TreeBuilder builder, UInt128 id) =>
        builder._store.Records[builder._indexById[id]].Flags.HasFlag(FileRecordFlags.Listed);

    private static IEnumerable<NativeFileEntry> FailingMidway()
    {
        yield return new NativeFileEntry("captured.txt", FileAttributes.Normal, 1, 0, 0, 0);
        // ERROR_UNEXPECTED_NETWORK_ERROR, the shape FindNextFile reports for a share that dropped mid-listing.
        throw new Win32Exception(59);
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
