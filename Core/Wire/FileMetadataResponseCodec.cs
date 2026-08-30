using System.Buffers.Binary;

namespace Lertaro.Core.Wire;

// Split from PipeResponseBinarySerializer to keep the response dispatcher below the file limit.
internal static class FileMetadataResponseCodec
{
    public static int CalculateSize(Dictionary<string, FileMetadataEntry> metadata)
        => sizeof(int) + metadata.Sum(pair => PipeResponseBinarySerializer.GetStringByteCount(pair.Key) + 5 + 20);

    public static void Write(Span<byte> span, ref int offset, Dictionary<string, FileMetadataEntry> metadata)
    {
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), metadata.Count);
        offset += sizeof(int);
        foreach (var (path, entry) in metadata)
        {
            PipeResponseBinarySerializer.WriteString(span, ref offset, path);
            BinaryPrimitives.WriteInt64LittleEndian(span.Slice(offset), entry.Size);
            offset += sizeof(long);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset), entry.CreationTimeUnixSeconds);
            offset += sizeof(uint);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset), entry.LastWriteTimeUnixSeconds);
            offset += sizeof(uint);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset), entry.LastAccessTimeUnixSeconds);
            offset += sizeof(uint);
        }
    }

    public static Dictionary<string, FileMetadataEntry> Read(byte[] payload, ref int offset)
    {
        var count = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
        offset += sizeof(int);
        if (count < 0 || count > (payload.Length - offset) / (sizeof(long) + 3 * sizeof(uint) + 1))
            throw new InvalidDataException("Invalid file metadata count.");
        var metadata = new Dictionary<string, FileMetadataEntry>(count, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < count; i++)
        {
            var path = PipeResponseBinarySerializer.ReadString(payload, ref offset);
            var size = BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(offset));
            offset += sizeof(long);
            var created = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset));
            offset += sizeof(uint);
            var modified = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset));
            offset += sizeof(uint);
            var accessed = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset));
            offset += sizeof(uint);
            metadata[path] = new FileMetadataEntry(size, created, modified, accessed);
        }
        return metadata;
    }
}
