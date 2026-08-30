namespace Lertaro.Core.Tests.Indexer;

[TestClass]
public sealed class FileRecordFlagsHelperTests
{
    [TestMethod]
    public void FromAttributes_DirectoryHiddenSystem_SetsAllThreeFlags()
    {
        var attrs = FileAttributes.Directory | FileAttributes.Hidden | FileAttributes.System;

        var flags = FileRecordFlagsHelper.FromAttributes(attrs);

        Assert.IsTrue(flags.HasFlag(FileRecordFlags.Directory));
        Assert.IsTrue(flags.HasFlag(FileRecordFlags.Hidden));
        Assert.IsTrue(flags.HasFlag(FileRecordFlags.System));
        Assert.IsFalse(flags.HasFlag(FileRecordFlags.ReadOnly));
    }

    [TestMethod]
    public void FromAttributes_NormalFile_ProducesNoFlags() => Assert.AreEqual(FileRecordFlags.None, FileRecordFlagsHelper.FromAttributes(FileAttributes.Normal));

    [TestMethod]
    public void ToAttributes_NoFlags_ReturnsNormal() => Assert.AreEqual(FileAttributes.Normal, FileRecordFlagsHelper.ToAttributes(FileRecordFlags.None));

    [TestMethod]
    public void ToAttributes_DirectoryFlag_ReturnsDirectoryAttribute()
    {
        var attrs = FileRecordFlagsHelper.ToAttributes(FileRecordFlags.Directory);

        Assert.IsTrue(attrs.HasFlag(FileAttributes.Directory));
    }

    [TestMethod]
    public void FromAttributes_ThenToAttributes_RoundTripsSupportedFlags()
    {
        var original = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly | FileAttributes.Compressed | FileAttributes.Encrypted;

        var restored = FileRecordFlagsHelper.ToAttributes(FileRecordFlagsHelper.FromAttributes(original));

        Assert.AreEqual(original, restored);
    }

    [TestMethod]
    public void FromAttributes_IgnoresUnsupportedAttributeBits()
    {
        // Archive isn't tracked by FileRecordFlags at all -- FromAttributes should silently drop it
        // rather than throw or misclassify it as some other flag.
        var flags = FileRecordFlagsHelper.FromAttributes(FileAttributes.Archive);

        Assert.AreEqual(FileRecordFlags.None, flags);
    }

    [TestMethod]
    public void FromAttributes_ReparsePoint_PreservesLinkMarker()
    {
        var flags = FileRecordFlagsHelper.FromAttributes(FileAttributes.ReparsePoint);

        Assert.IsTrue(flags.HasFlag(FileRecordFlags.ReparsePoint));
        Assert.AreEqual(FileAttributes.ReparsePoint, FileRecordFlagsHelper.ToAttributes(flags));
    }

    [TestMethod]
    public void FileRecord_IsDirectory_ReflectsDirectoryFlag()
    {
        var record = new FileRecord(1, 0, "folder", FileRecordFlags.Directory);

        Assert.IsTrue(record.IsDirectory);
        Assert.IsFalse(record.IsDeleted);
    }

    [TestMethod]
    public void FileRecord_IsDeleted_ReflectsDeletedFlag()
    {
        var record = new FileRecord(1, 0, "file.txt", FileRecordFlags.Deleted);

        Assert.IsTrue(record.IsDeleted);
        Assert.IsFalse(record.IsDirectory);
    }
}
