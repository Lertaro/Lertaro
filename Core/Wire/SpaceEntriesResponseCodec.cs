using System.Buffers.Binary;
using Lertaro.Core.IndexV2.Space;

namespace Lertaro.Core.Wire;

internal static class SpaceEntriesResponseCodec
{
    public static int CalculateSize(IReadOnlyList<SpaceIndexEntry> entries)
    {
        var size = sizeof(int);
        foreach (var entry in entries)
            size += PipeResponseBinarySerializer.GetStringByteCount(entry.Path) + 5
                + PipeResponseBinarySerializer.GetStringByteCount(entry.Name) + 5 + sizeof(long) + 2;
        return size;
    }

    public static void Write(Span<byte> span, ref int offset, IReadOnlyList<SpaceIndexEntry> entries)
    {
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), entries.Count);
        offset += sizeof(int);
        foreach (var entry in entries)
        {
            PipeResponseBinarySerializer.WriteString(span, ref offset, entry.Path);
            PipeResponseBinarySerializer.WriteString(span, ref offset, entry.Name);
            BinaryPrimitives.WriteInt64LittleEndian(span.Slice(offset), entry.Size);
            offset += sizeof(long);
            span[offset++] = entry.IsDirectory ? (byte)1 : (byte)0;
            span[offset++] = entry.IsHardLinkDuplicate ? (byte)1 : (byte)0;
        }
    }

    public static List<SpaceIndexEntry> Read(byte[] payload, ref int offset)
    {
        var count = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
        offset += sizeof(int);
        if (count < 0 || count > (payload.Length - offset) / 12)
            throw new InvalidDataException("Invalid space entry count.");
        var entries = new List<SpaceIndexEntry>(count);
        for (var i = 0; i < count; i++)
        {
            var path = PipeResponseBinarySerializer.ReadString(payload, ref offset);
            var name = PipeResponseBinarySerializer.ReadString(payload, ref offset);
            var size = BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(offset));
            offset += sizeof(long);
            entries.Add(new SpaceIndexEntry(path, name, size, payload[offset++] != 0, payload[offset++] != 0));
        }
        return entries;
    }
}
