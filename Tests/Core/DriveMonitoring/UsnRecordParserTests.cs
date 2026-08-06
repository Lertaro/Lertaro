using System.Text;
using Lertaro.Core.DriveMonitoring;

namespace Lertaro.Core.Tests.DriveMonitoring;

[TestClass]
public sealed class UsnRecordParserTests
{
    [TestMethod]
    public void ParseRecord_V2Record_ParsesAllFieldsAndName()
    {
        var name = "test.txt";
        var nameBytes = Encoding.Unicode.GetBytes(name);
        const int nameOffset = 60;
        var buf = new byte[nameOffset + 32];

        WriteUInt32(buf, 0, (uint)(nameOffset + nameBytes.Length)); // RecordLength
        WriteUInt16(buf, 4, 2); // MajorVersion
        WriteUInt64(buf, 8, 0x1122334455667788UL); // FileReferenceNumber
        WriteUInt64(buf, 16, 0x99AABBCCDDEEFF00UL); // ParentFileReferenceNumber
        WriteInt64(buf, 24, 123456789L); // Usn
        WriteUInt32(buf, 40, 0x00000002); // Reason
        WriteUInt32(buf, 52, 0x00000020); // FileAttributes = ARCHIVE (not a directory)
        WriteUInt16(buf, 56, (ushort)nameBytes.Length); // FileNameLength
        WriteUInt16(buf, 58, nameOffset); // FileNameOffset
        nameBytes.CopyTo(buf, nameOffset);

        var record = UsnRecordParser.ParseRecord(buf);

        Assert.AreEqual(2, record.MajorVersion);
        Assert.AreEqual((UInt128)0x1122334455667788UL, record.FileReferenceNumber);
        Assert.AreEqual((UInt128)0x99AABBCCDDEEFF00UL, record.ParentFileReferenceNumber);
        Assert.AreEqual(123456789L, record.Usn);
        Assert.AreEqual(0x00000002u, record.Reason);
        Assert.AreEqual(0x00000020u, record.FileAttributes);
        Assert.AreEqual(name, record.FileName);
        Assert.IsFalse(record.IsDirectory);
    }

    [TestMethod]
    public void ParseRecord_V2Record_DirectoryAttribute_IsDirectoryTrue()
    {
        const int nameOffset = 60;
        var buf = new byte[nameOffset];
        WriteUInt32(buf, 0, nameOffset);
        WriteUInt16(buf, 4, 2);
        WriteUInt32(buf, 52, 0x00000010); // FILE_ATTRIBUTE_DIRECTORY
        WriteUInt16(buf, 56, 0);
        WriteUInt16(buf, 58, 0);

        var record = UsnRecordParser.ParseRecord(buf);

        Assert.IsTrue(record.IsDirectory);
        Assert.AreEqual(string.Empty, record.FileName);
    }

    [TestMethod]
    public void ParseRecord_V3Record_Parses128BitFileReferenceNumbers()
    {
        var name = "v3file.txt";
        var nameBytes = Encoding.Unicode.GetBytes(name);
        const int nameOffset = 76;
        var buf = new byte[nameOffset + nameBytes.Length];

        WriteUInt32(buf, 0, (uint)buf.Length);
        WriteUInt16(buf, 4, 3); // MajorVersion
        WriteUInt64(buf, 8, 0x1111111111111111UL); // FRN low
        WriteUInt64(buf, 16, 0x2222222222222222UL); // FRN high
        WriteUInt64(buf, 24, 0x3333333333333333UL); // Parent FRN low
        WriteUInt64(buf, 32, 0x4444444444444444UL); // Parent FRN high
        WriteInt64(buf, 40, 987654321L); // Usn
        WriteUInt32(buf, 56, 0x00000004); // Reason
        WriteUInt32(buf, 68, 0x00000020); // FileAttributes
        WriteUInt16(buf, 72, (ushort)nameBytes.Length);
        WriteUInt16(buf, 74, nameOffset);
        nameBytes.CopyTo(buf, nameOffset);

        var record = UsnRecordParser.ParseRecord(buf);

        Assert.AreEqual(3, record.MajorVersion);
        Assert.AreEqual(new UInt128(0x2222222222222222UL, 0x1111111111111111UL), record.FileReferenceNumber);
        Assert.AreEqual(new UInt128(0x4444444444444444UL, 0x3333333333333333UL), record.ParentFileReferenceNumber);
        Assert.AreEqual(987654321L, record.Usn);
        Assert.AreEqual(0x00000004u, record.Reason);
        Assert.AreEqual(name, record.FileName);
    }

    [TestMethod]
    public void ParseRecord_UnsupportedMajorVersion_Throws()
    {
        var buf = new byte[16];
        WriteUInt32(buf, 0, 16);
        WriteUInt16(buf, 4, 1); // unsupported version

        Assert.ThrowsExactly<NotSupportedException>(() => UsnRecordParser.ParseRecord(buf));
    }

    [TestMethod]
    public void ParseRecord_NameOffsetBeyondRecordLength_FileNameIsEmpty()
    {
        const int nameOffset = 60;
        var name = "ignored.txt";
        var nameBytes = Encoding.Unicode.GetBytes(name);
        var buf = new byte[nameOffset + nameBytes.Length];

        WriteUInt32(buf, 0, nameOffset); // RecordLength claims to end BEFORE the name data
        WriteUInt16(buf, 4, 2);
        WriteUInt16(buf, 56, (ushort)nameBytes.Length);
        WriteUInt16(buf, 58, nameOffset);
        nameBytes.CopyTo(buf, nameOffset);

        var record = UsnRecordParser.ParseRecord(buf);

        Assert.AreEqual(string.Empty, record.FileName);
    }

    private static void WriteUInt16(byte[] buf, int offset, int value) =>
        BitConverter.GetBytes((ushort)value).CopyTo(buf, offset);

    private static void WriteUInt32(byte[] buf, int offset, uint value) =>
        BitConverter.GetBytes(value).CopyTo(buf, offset);

    private static void WriteUInt64(byte[] buf, int offset, ulong value) =>
        BitConverter.GetBytes(value).CopyTo(buf, offset);

    private static void WriteInt64(byte[] buf, int offset, long value) =>
        BitConverter.GetBytes(value).CopyTo(buf, offset);
}
