using Lertaro.Core.Indexer.NetworkDrive.Walk;

namespace Lertaro.Core.Tests.Indexer.NetworkDrive.Walk;

[TestClass]
public sealed class WalkTypesTests
{
    [TestMethod]
    public void NetworkWalkRecord_Properties_DelegateToUnderlyingFileRecord()
    {
        var record = new FileRecord(5, 1, "file.txt", FileRecordFlags.ReadOnly);
        var walkRecord = new NetworkWalkRecord(record, FileAttributes.ReadOnly);

        Assert.AreEqual(record.Id, walkRecord.Id);
        Assert.AreEqual(record.ParentId, walkRecord.ParentId);
        Assert.AreEqual(record.Name, walkRecord.Name);
        Assert.AreEqual(record.Flags, walkRecord.Flags);
        Assert.AreEqual(FileAttributes.ReadOnly, walkRecord.Attributes);
    }

    [TestMethod]
    public void NetworkWalkRecord_ImplicitConversion_YieldsUnderlyingFileRecord()
    {
        var record = new FileRecord(5, 1, "file.txt", FileRecordFlags.None);
        var walkRecord = new NetworkWalkRecord(record, FileAttributes.Normal);

        FileRecord converted = walkRecord;

        Assert.AreEqual(record.Id, converted.Id);
        Assert.AreEqual(record.Name, converted.Name);
    }
}
