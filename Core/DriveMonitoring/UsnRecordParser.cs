using System.Runtime.InteropServices;
using System.Text;

namespace Lertaro.Core.DriveMonitoring;

public struct ParsedUsnRecord
{
    public uint RecordLength;
    public ushort MajorVersion;
    public UInt128 FileReferenceNumber;
    public UInt128 ParentFileReferenceNumber;
    public long Usn;
    public uint Reason;
    public uint FileAttributes;
    public string FileName;
    public bool IsDirectory => (FileAttributes & 0x00000010) != 0; // FILE_ATTRIBUTE_DIRECTORY
}

public static class UsnRecordParser
{
    public static ParsedUsnRecord ParseRecord(ReadOnlySpan<byte> span)
    {
        var record = new ParsedUsnRecord();
        record.RecordLength = MemoryMarshal.Read<uint>(span.Slice(0, 4));
        record.MajorVersion = MemoryMarshal.Read<ushort>(span.Slice(4, 2));

        ushort nameLength = 0;
        ushort nameOffset = 0;

        if (record.MajorVersion == 2)
        {
            var frn = MemoryMarshal.Read<ulong>(span.Slice(8, 8));
            var parentFrn = MemoryMarshal.Read<ulong>(span.Slice(16, 8));
            record.FileReferenceNumber = frn;
            record.ParentFileReferenceNumber = parentFrn;
            record.Usn = MemoryMarshal.Read<long>(span.Slice(24, 8));
            record.Reason = MemoryMarshal.Read<uint>(span.Slice(40, 4));
            record.FileAttributes = MemoryMarshal.Read<uint>(span.Slice(52, 4));
            nameLength = MemoryMarshal.Read<ushort>(span.Slice(56, 2));
            nameOffset = MemoryMarshal.Read<ushort>(span.Slice(58, 2));
        }
        else if (record.MajorVersion == 3)
        {
            var frnLow = MemoryMarshal.Read<ulong>(span.Slice(8, 8));
            var frnHigh = MemoryMarshal.Read<ulong>(span.Slice(16, 8));
            var parentLow = MemoryMarshal.Read<ulong>(span.Slice(24, 8));
            var parentHigh = MemoryMarshal.Read<ulong>(span.Slice(32, 8));

            record.FileReferenceNumber = new UInt128(frnHigh, frnLow);
            record.ParentFileReferenceNumber = new UInt128(parentHigh, parentLow);
            record.Usn = MemoryMarshal.Read<long>(span.Slice(40, 8));
            record.Reason = MemoryMarshal.Read<uint>(span.Slice(56, 4));
            record.FileAttributes = MemoryMarshal.Read<uint>(span.Slice(68, 4));
            nameLength = MemoryMarshal.Read<ushort>(span.Slice(72, 2));
            nameOffset = MemoryMarshal.Read<ushort>(span.Slice(74, 2));
        }
        else
        {
            throw new NotSupportedException($"USN Record Major Version {record.MajorVersion} is not supported.");
        }

        if (nameLength > 0 && nameOffset > 0 && nameOffset + nameLength <= record.RecordLength)
        {
            var nameSpan = span.Slice(nameOffset, nameLength);
            record.FileName = Encoding.Unicode.GetString(nameSpan);
        }
        else
        {
            record.FileName = string.Empty;
        }

        return record;
    }
}
