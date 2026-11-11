using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Channels;

using Lertaro.Core.Indexer.NetworkDrive.Walk;

namespace Lertaro.Core.Tests.Indexer.NetworkDrive.Walk;

[TestClass]
public sealed class TreeBuilderTests
{
    // Regression coverage: _onProgress only ever received a single cumulative count before, with no way
    // to tell files and directories apart. _indexedFiles/_indexedDirs must each land on the record kind
    // that actually produced them, not just split the total arbitrarily.
    [TestMethod]
    public void Run_MixedFilesAndDirectories_TracksFilesAndDirsSeparately()
    {
        using var dir = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(dir.Path, "sub1"));
        Directory.CreateDirectory(Path.Combine(dir.Path, "sub2"));
        File.WriteAllText(Path.Combine(dir.Path, "a.txt"), "x");
        File.WriteAllText(Path.Combine(dir.Path, "b.txt"), "y");
        File.WriteAllText(Path.Combine(dir.Path, "c.txt"), "z");

        var builder = new TreeBuilder(
            new FileRecordStore(), dir.Path, dir.Path,
            new WalkOptions([], [], [], MaxDepth: 0, WorkerCount: 1, UseIgnoreFiles: false),
            CancellationToken.None, (_, _) => { });
        builder._store.Records.Add(new FileRecord(1, 1, "", FileRecordFlags.Directory | FileRecordFlags.SourceRoot));
        builder.RegisterDirectoryIndices(0, builder._store.Records);

        builder.Run();

        Assert.AreEqual(3, builder._indexedFiles);
        Assert.AreEqual(2, builder._indexedDirs);
    }

    // Regression coverage for a work item that faults its worker: the pending-directory decrement and the
    // channel completion used to sit AFTER WalkDirectory, so the faulting worker skipped both and every
    // other worker stayed blocked in WaitToReadAsync forever -- a drive stuck on "indexing" with no
    // watchdog to notice. Two workers, so the second one is genuinely parked on the channel when the first
    // one throws.
    [TestMethod]
    public void Run_WorkItemThrows_WorkersStillFinishInsteadOfBlockingOnTheChannel()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "a.txt"), "x");
        // Only the fallback that releases a worker nobody completed -- a wedge must fail the assertions
        // below rather than hang the whole test run.
        using var fallback = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var builder = new TreeBuilder(
            new FileRecordStore(), dir.Path, dir.Path,
            new WalkOptions([], [], [], MaxDepth: 0, WorkerCount: 2, UseIgnoreFiles: false),
            fallback.Token, (_, _) => throw new InvalidOperationException("progress callback blew up"));
        builder._store.Records.Add(new FileRecord(1, 1, string.Empty, FileRecordFlags.Directory | FileRecordFlags.SourceRoot));
        builder.RegisterDirectoryIndices(0, builder._store.Records);
        // The single file below is enough to cross the progress threshold and invoke that callback.
        builder._countSinceProgress = TreeBuilder.ProgressBatchSize - 1;

        var stopwatch = Stopwatch.StartNew();
        // WaitAll observes the faulted worker, exactly as it already did -- what must not happen is the
        // walk surviving on the fallback token above.
        Assert.ThrowsExactly<AggregateException>(() => builder.Run());
        stopwatch.Stop();

        Assert.IsLessThan(5_000L, stopwatch.ElapsedMilliseconds,
            "the surviving worker must be released by the channel completing, not by the test's own cancellation");
        // The channel really was completed: a further enqueue is refused instead of being accepted by a
        // channel whose readers have all given up on it.
        Assert.ThrowsExactly<ChannelClosedException>(() =>
            builder.EnqueueDirectory(dir.Path, dir.Path, parentId: 77, depth: 1, NetworkIgnoreRuleSet.Empty));
    }

    [TestMethod]
    public void EnqueueDirectory_AncestorLoopDetected_IncrementsReparseSkippedAndReturns()
    {
        using var dir = new TempDirectory();
        var builder = new TreeBuilder(
            new FileRecordStore(), dir.Path, dir.Path,
            new WalkOptions([], [], [], MaxDepth: 0, WorkerCount: 1, UseIgnoreFiles: false),
            CancellationToken.None, (_, _) => { });

        var parentAncestors = new AncestorNode(dir.Path, null);
        // Attempt to enqueue the exact same path that is already in parentAncestors
        builder.EnqueueDirectory(dir.Path, dir.Path, parentId: 2, depth: 1, NetworkIgnoreRuleSet.Empty, parentAncestors);

        Assert.AreEqual(1, builder._reparseSkipped);
        Assert.AreEqual(1, builder._skippedItems);
    }

    [TestMethod]
    [DataRow(5, 1)]
    [DataRow(64, 4)]
    public void WalkDirectory_CachedOpenFailure_UsesOneRetryBudget(int error, int expectedAttempts)
    {
        var (builder, current) = CreateWalk();
        var attempts = 0;
        var waits = new List<int>();

        builder.WalkDirectory(current,
            path => { Assert.AreEqual(current.Path, path); attempts++; throw new Win32Exception(error); },
            delay => { waits.Add(delay); return false; });

        Assert.AreEqual(expectedAttempts, attempts);
        CollectionAssert.AreEqual(expectedAttempts == 1 ? Array.Empty<int>() : new[] { 250, 1000, 3000 }, waits);
        Assert.AreEqual(1, builder._enumerateErrors);
        Assert.AreEqual(1, builder._errors);
        Assert.AreEqual(0, builder._reusedDirectories);
        Assert.HasCount(1, builder._store.Records);
        Assert.IsFalse(builder._store.Records[0].Flags.HasFlag(FileRecordFlags.Listed));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void WalkDirectory_CachedMidIterationFailure_KeepsPartialRecordsWithoutReopening(int failureKind)
    {
        var (builder, current) = CreateWalk();
        var attempts = 0;
        var waits = new List<int>();
        var disposed = false;
        Exception failure = failureKind switch
        {
            0 => new Win32Exception(59),
            1 => new IOException("Connection lost", unchecked((int)0x80070040)),
            _ => new UnauthorizedAccessException()
        };
        IEnumerable<NativeFileEntry> Entries()
        {
            try
            {
                yield return new NativeFileEntry("captured.txt", FileAttributes.Normal, 1, 0, 0, 0);
                throw failure;
            }
            finally { disposed = true; }
        }

        builder.WalkDirectory(current,
            _ => { attempts++; return Entries(); },
            delay => { waits.Add(delay); return false; });

        Assert.AreEqual(1, attempts);
        Assert.IsEmpty(waits);
        Assert.IsTrue(disposed);
        Assert.AreEqual(1, builder._enumerateErrors);
        Assert.AreEqual(1, builder._errors);
        Assert.AreEqual(0, builder._reusedDirectories);
        Assert.AreEqual(1, builder._indexedFiles);
        Assert.HasCount(2, builder._store.Records);
        Assert.AreEqual("captured.txt", builder._store.Records[1].Name);
        Assert.AreEqual(current.LocalId, builder._store.Records[1].ParentId);
        Assert.IsFalse(builder._store.Records[0].Flags.HasFlag(FileRecordFlags.Listed));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void WalkDirectory_CachedCancellationDuringBuffering_PropagatesWithoutReopening(bool iteratorThrows)
    {
        using var cancellation = new CancellationTokenSource();
        var (builder, current) = CreateWalk(token: cancellation.Token);
        var attempts = 0;
        var waits = new List<int>();
        var disposed = false;
        IEnumerable<NativeFileEntry> Entries()
        {
            try
            {
                yield return new NativeFileEntry("captured.txt", FileAttributes.Normal, 1, 0, 0, 0);
                cancellation.Cancel();
                if (iteratorThrows)
                    cancellation.Token.ThrowIfCancellationRequested();
                yield return new NativeFileEntry("cancelled.txt", FileAttributes.Normal, 1, 0, 0, 0);
                Assert.Fail("Buffering must observe cancellation before reading another entry.");
            }
            finally { disposed = true; }
        }

        var failure = Assert.ThrowsExactly<OperationCanceledException>(() => builder.WalkDirectory(current,
            _ => { attempts++; return Entries(); },
            delay => { waits.Add(delay); return false; }));

        Assert.AreEqual(cancellation.Token, failure.CancellationToken);
        Assert.AreEqual(1, attempts);
        Assert.IsEmpty(waits);
        Assert.IsTrue(disposed);
        Assert.AreEqual(0, builder._errors);
        Assert.AreEqual(0, builder._enumerateErrors);
        Assert.AreEqual(0, builder._reusedDirectories);
        Assert.HasCount(1, builder._store.Records);
        Assert.IsFalse(builder._store.Records[0].Flags.HasFlag(FileRecordFlags.Listed));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public void WalkDirectory_CachedSuccessfulListing_ReusesOrRebuildsWithoutReopening(bool changed, bool retry)
    {
        var (builder, current) = CreateWalk();
        NativeFileEntry[] entries = [new("captured.txt", FileAttributes.Normal, changed ? 2 : 1, 0, 0, 0)];
        var attempts = 0;
        var waits = new List<int>();

        builder.WalkDirectory(current,
            _ => ++attempts == 1 && retry ? throw new Win32Exception(64) : entries,
            delay => { waits.Add(delay); return false; });

        Assert.AreEqual(retry ? 2 : 1, attempts);
        CollectionAssert.AreEqual(retry ? new[] { 250 } : Array.Empty<int>(), waits);
        Assert.AreEqual(changed ? 0 : 1, builder._reusedDirectories);
        Assert.AreEqual(1, builder._indexedFiles);
        Assert.AreEqual(0, builder._enumerateErrors);
        Assert.AreEqual(0, builder._errors);
        Assert.HasCount(2, builder._store.Records);
        Assert.AreEqual("captured.txt", builder._store.Records[1].Name);
        Assert.AreEqual(changed ? 2L : 1L, builder._store.Records[1].Size);
        if (!changed)
            Assert.AreEqual((UInt128)2, builder._store.Records[1].Id);
        Assert.IsTrue(builder._store.Records[0].Flags.HasFlag(FileRecordFlags.Listed));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void WalkDirectory_NoListedBaseline_ConsumesEntriesAsTheyArrive(bool hasBaseline)
    {
        var (builder, current) = CreateWalk(listed: hasBaseline ? false : null);
        var attempts = 0;
        IEnumerable<NativeFileEntry> Entries()
        {
            yield return new NativeFileEntry("captured.txt", FileAttributes.Normal, 1, 0, 0, 0);
            Assert.AreEqual(1, builder._indexedFiles, "The first entry must be consumed before advancing the iterator.");
            yield return new NativeFileEntry("second.txt", FileAttributes.Normal, 2, 0, 0, 0);
        }

        builder.WalkDirectory(current, _ => { attempts++; return Entries(); });

        Assert.AreEqual(1, attempts);
        Assert.AreEqual(2, builder._indexedFiles);
        Assert.AreEqual(0, builder._reusedDirectories);
        Assert.AreEqual(0, builder._errors);
        CollectionAssert.AreEqual(new[] { string.Empty, "captured.txt", "second.txt" }, builder._store.Records.Select(r => r.Name).ToArray());
        Assert.IsTrue(builder._store.Records[0].Flags.HasFlag(FileRecordFlags.Listed));
    }

    [TestMethod]
    public void WalkDirectory_CachedUnexpectedIteratorFailure_PropagatesWithoutCountingEnumerationError()
    {
        var (builder, current) = CreateWalk();
        var failure = new InvalidOperationException("Iterator bug");
        var attempts = 0;
        IEnumerable<NativeFileEntry> Entries()
        {
            yield return new NativeFileEntry("captured.txt", FileAttributes.Normal, 1, 0, 0, 0);
            throw failure;
        }

        var observed = Assert.ThrowsExactly<InvalidOperationException>(() =>
            builder.WalkDirectory(current, _ => { attempts++; return Entries(); }));

        Assert.AreSame(failure, observed);
        Assert.AreEqual(1, attempts);
        Assert.AreEqual(0, builder._errors);
        Assert.AreEqual(0, builder._enumerateErrors);
        Assert.HasCount(1, builder._store.Records);
        Assert.IsFalse(builder._store.Records[0].Flags.HasFlag(FileRecordFlags.Listed));
    }

    private static (TreeBuilder Builder, WorkItem Current) CreateWalk(bool? listed = true, CancellationToken token = default)
    {
        const string path = @"C:\unused";
        var previousStore = new FileRecordStore();
        previousStore.Records.Add(new FileRecord(1, 1, string.Empty,
            FileRecordFlags.Directory | FileRecordFlags.SourceRoot | (listed == true ? FileRecordFlags.Listed : FileRecordFlags.None)));
        previousStore.Records.Add(new FileRecord(2, 1, "captured.txt", FileRecordFlags.None, 1, 0, 0, 0));
        var store = new FileRecordStore();
        store.Records.Add(new FileRecord(1, 1, string.Empty, FileRecordFlags.Directory | FileRecordFlags.SourceRoot));
        var builder = new TreeBuilder(store, path, path,
            new WalkOptions([], [], [], MaxDepth: 0, WorkerCount: 1, UseIgnoreFiles: false), token, (_, _) => { },
            diffBaseline: listed.HasValue ? TreeDiffBaseline.From(previousStore) : null);
        return (builder, new WorkItem(path, path, 1, 0, NetworkIgnoreRuleSet.Empty));
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;
        public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
    }
}
