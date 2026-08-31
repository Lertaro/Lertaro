using System.Buffers.Binary;

namespace Lertaro.Core.Wire;

// Split from SearchRequestBinarySerializer to keep the wire-format dispatcher compact.
internal static class SearchRequestValueCodec
{
    public static int CalculateSettingsSize(MachineSettings settings)
        => sizeof(int) + settings.LocalDrives.Sum(drive => SearchRequestBinarySerializer.GetStringByteCount(drive) + 5);

    public static void WriteSettings(Span<byte> span, ref int offset, MachineSettings settings)
    {
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), settings.LocalDrives.Count);
        offset += sizeof(int);
        foreach (var drive in settings.LocalDrives)
            SearchRequestBinarySerializer.WriteString(span, ref offset, drive);
    }

    public static MachineSettings ReadSettings(byte[] payload, ref int offset)
    {
        var count = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
        offset += sizeof(int);
        if (count < 0 || count > payload.Length - offset)
            throw new InvalidDataException("Invalid machine settings drive count.");
        var settings = new MachineSettings();
        for (var i = 0; i < count; i++)
            settings.LocalDrives.Add(SearchRequestBinarySerializer.ReadString(payload, ref offset));
        return settings;
    }

    public static int CalculateStringListSize(List<string>? list)
        => sizeof(int) + (list?.Sum(value => SearchRequestBinarySerializer.GetStringByteCount(value) + 5) ?? 0);

    public static void WriteStringList(Span<byte> span, ref int offset, List<string>? list)
    {
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), list?.Count ?? 0);
        offset += sizeof(int);
        if (list == null)
            return;
        foreach (var value in list)
            SearchRequestBinarySerializer.WriteString(span, ref offset, value);
    }

    public static List<string> ReadStringList(byte[] payload, ref int offset)
    {
        var count = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
        offset += sizeof(int);
        if (count < 0 || count > payload.Length - offset)
            throw new InvalidDataException("Invalid string list count.");
        var result = new List<string>(count);
        for (var i = 0; i < count; i++)
            result.Add(SearchRequestBinarySerializer.ReadString(payload, ref offset));
        return result;
    }
}
