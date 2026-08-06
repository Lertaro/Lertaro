using Lertaro.Core.Indexer.Usn;
using Lertaro.Core.Indexer.Usn.Journal;

namespace Lertaro.Core.Tests.Indexer.Usn;

[TestClass]
public sealed class IndexCacheManagerTests
{
    // Regression coverage: CreateStoreFromDriveData used to build each record's flags from IsDir alone,
    // silently dropping ReFsItem.Listed -- every non-root directory came out of a ReFS scan (both the
    // final store and every mid-walk checkpoint) with FileRecordFlags.Listed unset, so
    // TreeDiffBaseline.TryGetUnchangedChildren could never trust it on a later scan and diff-reuse only
    // ever worked for the root's own direct children.
    [TestMethod]
    public void CreateStoreFromDriveData_ListedDirectory_PersistsListedFlag()
    {
        var items = new Dictionary<UInt128, ReFsItem>
        {
            [2] = new ReFsItem("listed-dir", 1, IsDir: true, Size: 0, CreationTimeUtc: 0, LastWriteTimeUtc: 0, LastAccessTimeUtc: 0, Listed: true),
            [3] = new ReFsItem("unlisted-dir", 1, IsDir: true, Size: 0, CreationTimeUtc: 0, LastWriteTimeUtc: 0, LastAccessTimeUtc: 0, Listed: false),
            [4] = new ReFsItem("file.txt", 1, IsDir: false, Size: 5, CreationTimeUtc: 0, LastWriteTimeUtc: 0, LastAccessTimeUtc: 0, Listed: true),
        };

        var store = IndexCacheManager.CreateStoreFromDriveData("C", rootFrn: 1, items, nextUsn: 0, journalId: 0);

        var listedDir = store.Records.Single(r => r.Id == 2);
        var unlistedDir = store.Records.Single(r => r.Id == 3);
        var file = store.Records.Single(r => r.Id == 4);
        Assert.IsTrue(listedDir.Flags.HasFlag(FileRecordFlags.Listed));
        Assert.IsFalse(unlistedDir.Flags.HasFlag(FileRecordFlags.Listed));
        // A file has no children of its own to "list" -- Listed on a ReFsItem only ever means anything
        // for a directory, but a stray true on a file record must still not crash or corrupt its flags.
        Assert.IsTrue(file.Flags.HasFlag(FileRecordFlags.Listed));
        Assert.IsFalse(file.Flags.HasFlag(FileRecordFlags.Directory));
    }

    // Regression coverage: the root record used to default to LastWriteTimeUnixSeconds=0 and no Listed
    // flag, which meant TreeDiffBaseline.TryGetUnchangedChildren could never match it against a live stat
    // -- permanently forcing a full re-list of the root's own children on every resume. Both real USN/MFT
    // drives (via MftIndexScanner) and ReFsScanner share this same root-record construction.
    [TestMethod]
    public void CreateEmptyStore_RealDrive_StampsRootWithLiveMtimeAndListedFlag()
    {
        var systemDrive = Path.GetPathRoot(Environment.SystemDirectory)![0].ToString();

        var store = IndexCacheManager.CreateEmptyStore(systemDrive, rootFrn: 1, nextUsn: 0, journalId: 0);

        var root = store.Records.Single();
        Assert.IsTrue(root.Flags.HasFlag(FileRecordFlags.Listed));
        Assert.AreNotEqual(0u, root.LastWriteTimeUnixSeconds);
    }

    [TestMethod]
    public void CreateEmptyStore_UnresolvableDrive_StillSetsListedButLeavesMtimeZero()
    {
        var store = IndexCacheManager.CreateEmptyStore("~", rootFrn: 1, nextUsn: 0, journalId: 0);

        var root = store.Records.Single();
        Assert.IsTrue(root.Flags.HasFlag(FileRecordFlags.Listed));
        Assert.AreEqual(0u, root.LastWriteTimeUnixSeconds);
    }
}
