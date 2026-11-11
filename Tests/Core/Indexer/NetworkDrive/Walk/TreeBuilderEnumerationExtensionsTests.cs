using System.ComponentModel;

using Lertaro.Core.Indexer.NetworkDrive.Walk;

namespace Lertaro.Core.Tests.Indexer.NetworkDrive.Walk;

[TestClass]
public sealed class TreeBuilderEnumerationExtensionsTests
{
    [TestMethod]
    [DataRow(2)] // file not found
    [DataRow(3)] // path not found
    [DataRow(5)] // access denied
    [DataRow(15)] // invalid drive
    [DataRow(65)] // network access denied
    [DataRow(67)] // bad network name
    [DataRow(123)] // invalid name
    [DataRow(206)] // path too long
    [DataRow(267)] // not a directory
    public void TryEnumerateChildren_PermanentFailure_DoesNotRetryOrWait(int error)
    {
        var builder = CreateBuilder(@"C:\unused");
        var failure = new Win32Exception(error);
        var attempts = 0;

        var success = builder.TryEnumerateChildren(@"C:\unused", out var children, out var observed,
            _ => { attempts++; throw failure; },
            _ => { Assert.Fail("Permanent failures must not spend the retry budget."); return false; });

        Assert.IsFalse(success);
        Assert.AreEqual(1, attempts);
        Assert.AreSame(failure, observed);
        Assert.IsEmpty(children);
    }

    [TestMethod]
    [DataRow(53)]
    [DataRow(59)]
    [DataRow(64)]
    [DataRow(121)]
    [DataRow(1231)]
    [DataRow(1236)]
    [DataRow(1460)]
    public void TryEnumerateChildren_TransientFailure_RecoversOnNextAttempt(int error)
    {
        var builder = CreateBuilder(@"C:\unused");
        NativeFileEntry[] expected = [new("kept.txt", FileAttributes.Normal, 7, 0, 0, 0)];
        var attempts = 0;
        var waits = new List<int>();

        var success = builder.TryEnumerateChildren(@"C:\unused", out var children, out _,
            _ => ++attempts == 1 ? throw new Win32Exception(error) : expected,
            delay => { waits.Add(delay); return false; });

        Assert.IsTrue(success);
        Assert.AreEqual(2, attempts);
        CollectionAssert.AreEqual(new[] { 250 }, waits);
        CollectionAssert.AreEqual(expected, children.ToArray());
    }

    [TestMethod]
    public void TryEnumerateChildren_PersistentNetworkFailure_StopsAfterBoundedRetries()
    {
        var builder = CreateBuilder(@"C:\unused");
        var failure = new IOException("Connection lost", unchecked((int)0x80070040));
        var attempts = 0;
        var waits = new List<int>();

        var success = builder.TryEnumerateChildren(@"C:\unused", out var children, out var observed,
            _ => { attempts++; throw failure; },
            delay => { waits.Add(delay); return false; });

        Assert.IsFalse(success);
        Assert.AreEqual(4, attempts);
        CollectionAssert.AreEqual(new[] { 250, 1000, 3000 }, waits);
        Assert.AreSame(failure, observed);
        Assert.IsEmpty(children);
    }

    [TestMethod]
    public void TryEnumerateChildren_AccessDeniedException_DoesNotWait()
    {
        var builder = CreateBuilder(@"C:\unused");
        var failure = new UnauthorizedAccessException();

        Assert.IsFalse(builder.TryEnumerateChildren(@"C:\unused", out _, out var observed,
            _ => throw failure,
            _ => { Assert.Fail("Access denied is not a transient failure."); return false; }));

        Assert.AreSame(failure, observed);
    }

    [TestMethod]
    public void TryEnumerateChildren_CancelledDuringBackoff_DoesNotAttemptAgain()
    {
        using var cancellation = new CancellationTokenSource();
        var builder = CreateBuilder(@"C:\unused", cancellation.Token);
        var attempts = 0;

        Assert.ThrowsExactly<OperationCanceledException>(() => builder.TryEnumerateChildren(@"C:\unused", out _, out _,
            _ => { attempts++; throw new Win32Exception(64); },
            _ => { cancellation.Cancel(); return true; }));

        Assert.AreEqual(1, attempts);
        Assert.AreEqual(0, builder._enumerateErrors);
    }

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

    // Cancellation must win before any native open or backoff.
    [TestMethod]
    public void TryEnumerateChildren_CancelledWalk_DoesNotAttemptToOpenDirectory()
    {
        using var dir = new TempDirectory();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var builder = CreateBuilder(dir.Path, cancelled.Token);

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            builder.TryEnumerateChildren(Path.Combine(dir.Path, "missing"), out _, out _));
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
