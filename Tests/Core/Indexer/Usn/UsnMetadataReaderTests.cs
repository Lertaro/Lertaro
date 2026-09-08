using System.Buffers.Binary;
using Lertaro.Core.Indexer.Usn;

namespace Lertaro.Core.Tests.Indexer.Usn;

[TestClass]
public sealed class UsnMetadataReaderTests
{
    [TestMethod]
    public void Read_RealFileAndDirectory_VerifiesIdentityAndMetadata()
    {
        var directory = Directory.CreateTempSubdirectory("lertaro-metadata-").FullName;
        var file = Path.Combine(directory, "sample.txt");
        try
        {
            File.WriteAllText(file, "abc");
            foreach (var path in new[] { file, directory })
            {
                using var handle = Win32Api.CreateFileW(path, 0, 7, IntPtr.Zero, Win32Api.OPEN_EXISTING,
                    Win32Api.FILE_FLAG_BACKUP_SEMANTICS | Win32Api.FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
                Assert.IsFalse(handle.IsInvalid);
                Assert.IsTrue(Win32Api.GetFileInformationByHandleEx(handle, 18, out var info, 24));
                var id = new UInt128(info.FileId.High, info.FileId.Low);
                if (new DriveInfo(Path.GetPathRoot(path)!).DriveFormat == "NTFS")
                {
                    Assert.IsTrue(Win32Api.GetFileInformationByHandle(handle, out var legacy));
                    Assert.AreEqual((UInt128)(((ulong)legacy.nFileIndexHigh << 32) | legacy.nFileIndexLow), id);
                }
                var result = UsnMetadataReader.Read(path, id);
                Assert.IsNotNull(result);
                Assert.AreEqual(path == file ? 3L : 0L, result.Value.Size);
                Assert.AreEqual(path == directory, result.Value.Flags.HasFlag(FileRecordFlags.Directory));
                Assert.IsNull(UsnMetadataReader.Read(path, id ^ 1));
            }
        }
        finally
        {
            File.Delete(file);
            Directory.Delete(directory);
        }
    }

    [TestMethod]
    public void DecodesNativeTimesSizeAndHiddenSystemAttributes()
    {
        var basic = new byte[40]; var standard = new byte[24];
        for (var i = 0; i < 3; i++)
            BinaryPrimitives.WriteInt64LittleEndian(basic.AsSpan(i * 8), DateTimeOffset.FromUnixTimeSeconds(100 + i).UtcDateTime.ToFileTimeUtc());
        BinaryPrimitives.WriteUInt32LittleEndian(basic.AsSpan(32), 6);
        BinaryPrimitives.WriteInt64LittleEndian(standard.AsSpan(8), 23);
        var value = UsnMetadataReader.Decode(basic, standard);
        Assert.AreEqual(23L, value.Size);
        Assert.AreEqual(100U, value.Created);
        Assert.AreEqual(102U, value.Modified);
        Assert.AreEqual(101U, value.Accessed);
        Assert.AreEqual(FileRecordFlags.Hidden | FileRecordFlags.System, value.Flags);
    }

    [TestMethod]
    public void DirectorySymlinkKeepsReparseFlagAndHasZeroSize()
    {
        var basic = new byte[40]; var standard = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(basic.AsSpan(32), 0x410);
        BinaryPrimitives.WriteInt64LittleEndian(standard.AsSpan(8), 99);
        var value = UsnMetadataReader.Decode(basic, standard);
        Assert.AreEqual(0L, value.Size);
        Assert.AreEqual(FileRecordFlagsHelper.FromAttributes(FileAttributes.Directory | FileAttributes.ReparsePoint), value.Flags);
    }

    [TestMethod]
    public void RejectsTruncationAndNegativeSize()
    {
        Assert.Throws<InvalidDataException>(() => UsnMetadataReader.Decode(new byte[32], new byte[24]));
        var standard = new byte[24]; BinaryPrimitives.WriteInt64LittleEndian(standard.AsSpan(8), -1);
        Assert.Throws<InvalidDataException>(() => UsnMetadataReader.Decode(new byte[40], standard));
    }

    [TestMethod]
    public void ClassifiesTransientOpenFailuresWithoutTreatingThemAsDeletion()
    {
        Assert.IsTrue(UsnMetadataReader.IsUnavailableError(5));
        Assert.IsTrue(UsnMetadataReader.IsUnavailableError(32));
        Assert.IsTrue(UsnMetadataReader.IsUnavailableError(33));
        Assert.IsFalse(UsnMetadataReader.IsUnavailableError(2));
        Assert.IsFalse(UsnMetadataReader.IsUnavailableError(87));
    }

    [TestMethod]
    public void RecognizesAllNtfsRootMetadataAndExtendSubtree()
    {
        Assert.IsTrue(UsnMetadataReader.IsNtfsInternalPath(@"D:\$Mft"));
        Assert.IsTrue(UsnMetadataReader.IsNtfsInternalPath(@"D:\$Bitmap"));
        Assert.IsTrue(UsnMetadataReader.IsNtfsInternalPath(@"D:\$Extend\$Deleted\0001"));
        Assert.IsTrue(UsnMetadataReader.IsNtfsInternalPath(@"D:\$Extend\$RmMetadata\$TxfLog\$Tops\0002"));
        Assert.IsFalse(UsnMetadataReader.IsNtfsInternalPath(@"D:\Users\test\$Extend\$Deleted\file.txt"));
        Assert.IsFalse(UsnMetadataReader.IsNtfsInternalPath(@"D:\Documents\report.txt"));
    }
}
