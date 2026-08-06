using System.Collections.Concurrent;
using Lertaro.Core.Indexer.Usn.Journal;

namespace Lertaro.Core.Tests.Indexer.Usn.Journal;

// ScanDrive/ScanParallel/ProcessDir all need a real SafeFileHandle onto a live ReFS volume (OpenFileById/
// GetFileInformationByHandleEx), so they aren't unit-testable here -- CopyReusedChildren and
// ToFileTimeUtcOrZero are the pure, Win32-free parts of the diff-reuse addition and get direct coverage
// instead, matching this codebase's established split (see e.g. TreeBuilderDiffExtensionsTests, which
// tests TreeBuilder's own reuse logic against a real temp directory precisely because THAT one only needs
// Directory.EnumerateFileSystemEntries, not a raw volume handle).
[TestClass]
public sealed class ReFsScannerTests
{
    [TestMethod]
    public void CopyReusedChildren_MixOfFilesAndDirectories_PopulatesItemsAndCountsCorrectly()
    {
        var items = new ConcurrentDictionary<UInt128, ReFsItem>();
        var cached = new[]
        {
            new FileRecord(10, 1, "file.txt", FileRecordFlags.None, size: 5, lastWriteTimeUnixSeconds: 100),
            new FileRecord(11, 1, "subdir", FileRecordFlags.Directory | FileRecordFlags.Listed, lastWriteTimeUnixSeconds: 200),
        };
        var files = 0;
        var dirs = 0;
        var subdirsSeen = new List<UInt128>();

        ReFsScanner.CopyReusedChildren(cached, directoryId: 1, items, ref files, ref dirs, subdirsSeen.Add);

        Assert.AreEqual(1, files);
        Assert.AreEqual(1, dirs);
        CollectionAssert.AreEqual(new UInt128[] { 11 }, subdirsSeen);
        Assert.IsTrue(items.ContainsKey(10));
        Assert.IsTrue(items.ContainsKey(11));
        Assert.AreEqual("file.txt", items[10].Name);
        Assert.IsFalse(items[10].IsDir);
        Assert.AreEqual((UInt128)1, items[10].ParentFrn);
        Assert.IsTrue(items[11].IsDir);
    }

    [TestMethod]
    public void CopyReusedChildren_DuplicateChildId_SkipsWithoutThrowingOrDoubleCounting()
    {
        var items = new ConcurrentDictionary<UInt128, ReFsItem>();
        items.TryAdd(10, new ReFsItem("already-there", 1, false, 0, 0, 0, 0));
        var cached = new[] { new FileRecord(10, 1, "duplicate.txt", FileRecordFlags.None) };
        var files = 0;
        var dirs = 0;

        ReFsScanner.CopyReusedChildren(cached, directoryId: 1, items, ref files, ref dirs, _ => { });

        Assert.AreEqual(0, files);
        Assert.AreEqual("already-there", items[10].Name); // untouched, not overwritten by the duplicate
    }

    // Regression coverage: FileTimeHelper.FromUnixSeconds(0) returns DateTime.MinValue (its own "not
    // recorded" convention), and DateTime.MinValue.ToFileTimeUtc() throws ArgumentOutOfRangeException
    // (FILETIME's epoch is 1601, DateTime's is year 1) -- a cached child with an unset timestamp (0, the
    // normal value for e.g. a record that was never metadata-refreshed) must not crash the whole reuse.
    [TestMethod]
    public void CopyReusedChildren_ChildWithZeroTimestamps_DoesNotThrow()
    {
        var items = new ConcurrentDictionary<UInt128, ReFsItem>();
        var cached = new[] { new FileRecord(10, 1, "no-timestamps.txt", FileRecordFlags.None) }; // all timestamps default to 0
        var files = 0;
        var dirs = 0;

        ReFsScanner.CopyReusedChildren(cached, directoryId: 1, items, ref files, ref dirs, _ => { });

        Assert.AreEqual(0L, items[10].CreationTimeUtc);
        Assert.AreEqual(0L, items[10].LastWriteTimeUtc);
        Assert.AreEqual(0L, items[10].LastAccessTimeUtc);
    }

    [TestMethod]
    public void ToFileTimeUtcOrZero_ZeroInput_ReturnsZero() =>
        Assert.AreEqual(0L, ReFsScanner.ToFileTimeUtcOrZero(0));

    [TestMethod]
    public void ToFileTimeUtcOrZero_NonZeroInput_RoundTripsThroughFileTimeToUnixSeconds()
    {
        const uint unixSeconds = 1_700_000_000; // 2023-11-14, arbitrary non-zero real timestamp

        var fileTime = ReFsScanner.ToFileTimeUtcOrZero(unixSeconds);

        Assert.AreNotEqual(0L, fileTime);
        Assert.AreEqual(unixSeconds, FileTimeHelper.FileTimeToUnixSeconds(fileTime));
    }
}
