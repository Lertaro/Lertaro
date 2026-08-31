using System.Buffers.Binary;
using System.Text;

using Lertaro.PluginSdk.Abstractions;
namespace Lertaro.Core.Wire;

// The payload inside one result frame: how a SearchResult's fields are laid out and read back.
// Split out of SearchResponseBinarySerializer purely to keep that file under the repo's per-file line
// limit; it holds no state and owns none of the framing (magic, frame byte, length, stream loop),
// which stays with the serializer. Field ORDER here is the wire contract -- changing it, or the size
// of any field, is a SearchResponseBinarySerializer.Version bump.
internal static class SearchResultFrameCodec
{
    // Upper bound for renting a buffer: the three strings plus fixed-width fields and their length
    // prefixes. WritePayload returns what it actually used.
    public static int MaxPayloadSize(SearchResult result)
        => Encoding.UTF8.GetByteCount(result.Name ?? string.Empty)
           + Encoding.UTF8.GetByteCount(result.Path ?? string.Empty)
           + Encoding.UTF8.GetByteCount(result.Drive ?? string.Empty)
           + 48;

    public static int WritePayload(Span<byte> destination, SearchResult result)
    {
        var offset = 0;
        WriteString(destination, ref offset, result.Name);
        WriteString(destination, ref offset, result.Path);
        destination[offset++] = (byte)(result.IsDir ? 1 : 0);
        WriteString(destination, ref offset, result.Drive);

        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(offset), result.RankSortKey);
        offset += 8;

        BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(offset), result.Metadata.Size);
        offset += 8;
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset), FileTimeHelper.ToUnixSeconds(result.Metadata.Created.ToUniversalTime()));
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset), FileTimeHelper.ToUnixSeconds(result.Metadata.Modified.ToUniversalTime()));
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset), FileTimeHelper.ToUnixSeconds(result.Metadata.Accessed.ToUniversalTime()));
        offset += 4;

        // Hidden/System bits drive FileSystemItemFilter.IsHiddenOrSystem client-side -- without this,
        // that check always sees the zero default and never filters anything (confirmed live: NTFS
        // metadata files like $MFT showed up in results despite the filter being unconditionally
        // wired in). Only the two bits the filter actually reads are worth carrying; write the whole
        // enum value anyway since it's a single int and keeps this a faithful round-trip.
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(offset), (int)result.Attributes);
        offset += 4;
        return offset;
    }

    public static SearchResult ReadPayload(byte[] payload)
    {
        var offset = 0;
        var name = ReadString(payload, ref offset);
        var path = ReadString(payload, ref offset);
        var isDir = payload[offset++] != 0;
        var drive = ReadString(payload, ref offset);
        var rankSortKey = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(offset));
        offset += 8;
        var size = BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(offset));
        offset += 8;
        var created = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset));
        offset += 4;
        var modified = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset));
        offset += 4;
        var accessed = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset));
        offset += 4;
        var attributes = (FileAttributes)BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
        return new SearchResult
        {
            Name = name,
            Path = path,
            IsDir = isDir,
            Drive = drive,
            Attributes = attributes,
            RankSortKey = rankSortKey,
            Metadata = new FileMetadata(
                size,
                FileTimeHelper.FromUnixSeconds(created).ToLocalTime(),
                FileTimeHelper.FromUnixSeconds(modified).ToLocalTime(),
                FileTimeHelper.FromUnixSeconds(accessed).ToLocalTime()),
        };
    }

    private static void WriteString(Span<byte> destination, ref int offset, string? value)
    {
        var text = value ?? string.Empty;
        var length = Encoding.UTF8.GetByteCount(text);
        Write7BitEncodedInt(destination.Slice(offset), length, out var written);
        offset += written;
        Encoding.UTF8.GetBytes(text, destination.Slice(offset));
        offset += length;
    }

    private static void Write7BitEncodedInt(Span<byte> destination, int value, out int bytesWritten)
    {
        bytesWritten = 0;
        var uValue = (uint)value;
        while (uValue >= 0x80)
        {
            destination[bytesWritten++] = (byte)(uValue | 0x80);
            uValue >>= 7;
        }
        destination[bytesWritten++] = (byte)uValue;
    }

    private static int Read7BitEncodedInt(byte[] buffer, ref int offset)
    {
        uint result = 0;
        var shift = 0;
        while (shift < 35)
        {
            if (offset >= buffer.Length)
                throw new InvalidDataException("Truncated 7-bit encoded integer.");
            var b = buffer[offset++];
            if (shift == 28 && (b & 0xF0) != 0)
                throw new InvalidDataException("Invalid 7-bit encoded integer.");
            result |= (uint)(b & 0x7F) << shift;
            shift += 7;
            if ((b & 0x80) == 0)
                return (int)result;
        }
        throw new FormatException("Invalid 7-bit encoded integer.");
    }

    private static string ReadString(byte[] buffer, ref int offset)
    {
        var length = Read7BitEncodedInt(buffer, ref offset);
        if (length == 0) return string.Empty;
        if (length < 0 || length > buffer.Length - offset)
            throw new InvalidDataException("Invalid string length.");
        var str = Encoding.UTF8.GetString(buffer, offset, length);
        offset += length;
        return str;
    }
}
