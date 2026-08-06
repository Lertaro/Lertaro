using Lertaro.Core.Indexer.Usn;

namespace Lertaro.Core.Tests.Indexer.Usn;

[TestClass]
public sealed class UsnDriveIndexResultTests
{
    [TestMethod]
    public void ItemCount_ExcludesTheRootRecord()
    {
        var store = new FileRecordStore();
        store.Records.Add(new FileRecord(1, 1, "", FileRecordFlags.Directory | FileRecordFlags.SourceRoot));
        store.Records.Add(new FileRecord(2, 1, "a.txt", FileRecordFlags.None));
        store.Records.Add(new FileRecord(3, 1, "b.txt", FileRecordFlags.None));

        var result = new UsnDriveIndexResult { Store = store, NextUsn = 100, JournalId = 1, IsSortedById = true };

        Assert.AreEqual(2, result.ItemCount);
    }

    [TestMethod]
    public void ItemCount_OnlyRootRecord_IsZero()
    {
        var store = new FileRecordStore();
        store.Records.Add(new FileRecord(1, 1, "", FileRecordFlags.Directory | FileRecordFlags.SourceRoot));

        var result = new UsnDriveIndexResult { Store = store, NextUsn = 0, JournalId = 0, IsSortedById = false };

        Assert.AreEqual(0, result.ItemCount);
    }
}
