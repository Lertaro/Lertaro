using System.Buffers.Binary;

namespace Lertaro.Core.Wire;

// Split from SearchRequestBinarySerializer to keep the wire-format dispatcher compact.
internal static class SearchRequestValueCodec
{
    // The SetMachineSettings payload: the drive selection followed by the three walk exclusion lists.
    // Appended rather than sent as a separate message so the rules ride the request that already exists;
    // the trailing position is what keeps the format forward-readable (see the version note in
    // SearchRequestBinarySerializer -- an older reader simply stops after the drives).
    public static int CalculateSettingsSize(SearchRequestMessage msg)
    {
        var settings = msg.MachineSettings ?? new MachineSettings();
        return sizeof(int) + settings.LocalDrives.Sum(drive => SearchRequestBinarySerializer.GetStringByteCount(drive) + 5)
            + CalculateStringListSize(msg.ExcludedPaths)
            + CalculateStringListSize(msg.IgnoredPathGlobs)
            + CalculateStringListSize(msg.IgnoredPathRegexes);
    }

    public static void WriteSettings(Span<byte> span, ref int offset, SearchRequestMessage msg)
    {
        var settings = msg.MachineSettings ?? new MachineSettings();
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), settings.LocalDrives.Count);
        offset += sizeof(int);
        foreach (var drive in settings.LocalDrives)
            SearchRequestBinarySerializer.WriteString(span, ref offset, drive);
        WriteStringList(span, ref offset, msg.ExcludedPaths);
        WriteStringList(span, ref offset, msg.IgnoredPathGlobs);
        WriteStringList(span, ref offset, msg.IgnoredPathRegexes);
    }

    /// <summary>
    /// Reads the SetMachineSettings payload into <paramref name="msg"/>, whose fields the lists land on.
    /// </summary>
    /// <remarks>
    /// The three lists are read only when the payload actually carries them, so a request written by an
    /// older App -- drives and nothing else -- still parses. That is the one place this format is lenient,
    /// and deliberately so: the ids and version are what fail loudly on a real mismatch, while a missing
    /// tail here has an unambiguous reading (no rules sent, so filter nothing) rather than a wrong one.
    /// </remarks>
    public static void ReadSettings(byte[] payload, ref int offset, SearchRequestMessage msg)
    {
        var count = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
        offset += sizeof(int);
        if (count < 0 || count > payload.Length - offset)
            throw new InvalidDataException("Invalid machine settings drive count.");
        var settings = new MachineSettings();
        for (var i = 0; i < count; i++)
            settings.LocalDrives.Add(SearchRequestBinarySerializer.ReadString(payload, ref offset));
        msg.MachineSettings = settings;

        if (offset >= payload.Length)
            return;
        msg.ExcludedPaths = ReadStringList(payload, ref offset);
        msg.IgnoredPathGlobs = ReadStringList(payload, ref offset);
        msg.IgnoredPathRegexes = ReadStringList(payload, ref offset);
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
