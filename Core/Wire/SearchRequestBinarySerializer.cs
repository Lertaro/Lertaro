using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace Lertaro.Core.Wire;

public static class SearchRequestBinarySerializer
{
    private const int Magic = 0x51504C53; // SLPQ
    // v5: Search/SearchDir gained the ExactMatch flag; v6: gained the EnumerateDir request.
    // Bumped for a new request id too, not only for a changed payload layout: the set of ids IS part of
    // this contract, and the version is what makes an App/Service pair that disagree about it fail
    // loudly and at once, in both directions, instead of one side quietly answering "Unknown command"
    // to a request the other believes is supported. App and Service always ship and restart together,
    // so a mismatch is an install-time transient, not a state worth degrading gracefully into.
    private const int VersionSearchRequest = 6;

    public static async Task WriteSearchRequestAsync(Stream stream, SearchRequestMessage msg, CancellationToken token = default)
    {
        var payloadSize = 1; // Id byte
        switch (msg.Id)
        {
            case SearchRequestId.SetMachineSettings:
                payloadSize += CalculateSettingsSize(msg.MachineSettings ?? new MachineSettings());
                break;
            case SearchRequestId.RebuildDrive:
            case SearchRequestId.DeleteDriveIndex:
            case SearchRequestId.CancelDriveIndex:
                payloadSize += GetStringByteCount(msg.Drive) + 5;
                break;
            case SearchRequestId.Search:
                payloadSize += 8 + GetStringByteCount(msg.Query) + 5 + CalculateStringListSize(msg.DisabledAliasComponents) + 1;
                break;
            case SearchRequestId.SearchDir:
                payloadSize += 8 + GetStringByteCount(msg.DirectoryFilter) + 5 + GetStringByteCount(msg.Query) + 5 + CalculateStringListSize(msg.DisabledAliasComponents) + 1;
                break;
            case SearchRequestId.EnumerateDir:
                payloadSize += 4 + GetStringByteCount(msg.DirectoryFilter) + 5 + GetStringByteCount(msg.Query) + 5 + 1;
                break;
            case SearchRequestId.GetFileMetadata:
                payloadSize += CalculateStringListSize(msg.FilePaths);
                break;
            case SearchRequestId.GetRecentFiles:
                payloadSize += 8 + CalculateStringListSize(msg.Directories);
                break;
            case SearchRequestId.LaunchHook:
                payloadSize += 1;
                break;
            case SearchRequestId.SubscribeDirectoryChanges:
                payloadSize += CalculateStringListSize(msg.Directories);
                break;
        }

        var totalSize = 12 + payloadSize; // Magic(4) + Version(4) + Length(4) + Payload
        var buffer = ArrayPool<byte>.Shared.Rent(totalSize);
        try
        {
            var span = buffer.AsSpan();
            var offset = 12;
            span[offset++] = (byte)msg.Id;

            switch (msg.Id)
            {
                case SearchRequestId.SetMachineSettings:
                    WriteMachineSettings(span, ref offset, msg.MachineSettings ?? new MachineSettings());
                    break;
                case SearchRequestId.RebuildDrive:
                case SearchRequestId.DeleteDriveIndex:
                case SearchRequestId.CancelDriveIndex:
                    WriteString(span, ref offset, msg.Drive);
                    break;
                case SearchRequestId.Search:
                    BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), msg.Limit);
                    offset += 4;
                    BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), msg.AppLimit);
                    offset += 4;
                    WriteString(span, ref offset, msg.Query);
                    WriteStringList(span, ref offset, msg.DisabledAliasComponents);
                    span[offset++] = (byte)(msg.ExactMatch ? 1 : 0);
                    break;
                case SearchRequestId.SearchDir:
                    BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), msg.Limit);
                    offset += 4;
                    BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), msg.AppLimit);
                    offset += 4;
                    WriteString(span, ref offset, msg.DirectoryFilter);
                    WriteString(span, ref offset, msg.Query);
                    WriteStringList(span, ref offset, msg.DisabledAliasComponents);
                    span[offset++] = (byte)(msg.ExactMatch ? 1 : 0);
                    break;
                case SearchRequestId.EnumerateDir:
                    BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), msg.Limit);
                    offset += 4;
                    WriteString(span, ref offset, msg.DirectoryFilter);
                    WriteString(span, ref offset, msg.Query);
                    span[offset++] = (byte)(msg.Recursive ? 1 : 0);
                    break;
                case SearchRequestId.GetFileMetadata:
                    WriteStringList(span, ref offset, msg.FilePaths);
                    break;
                case SearchRequestId.GetRecentFiles:
                    BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), msg.Limit);
                    offset += 4;
                    BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), msg.MaxAgeMinutes);
                    offset += 4;
                    WriteStringList(span, ref offset, msg.Directories);
                    break;
                case SearchRequestId.LaunchHook:
                    span[offset++] = (byte)(msg.RequestElevation ? 1 : 0);
                    break;
                case SearchRequestId.SubscribeDirectoryChanges:
                    WriteStringList(span, ref offset, msg.Directories);
                    break;
            }

            var actualPayloadSize = offset - 12;
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(0), Magic);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4), VersionSearchRequest);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(8), actualPayloadSize);

            await stream.WriteAsync(buffer.AsMemory(0, offset), token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static async Task<SearchRequestMessage> ReadSearchRequestAsync(Stream stream, CancellationToken token = default)
    {
        var magic = await ReadInt32Async(stream, token).ConfigureAwait(false);
        if (magic != Magic)
            throw new InvalidDataException("Invalid pipe request binary header.");

        var version = await ReadInt32Async(stream, token).ConfigureAwait(false);
        if (version != VersionSearchRequest)
            throw new InvalidDataException($"Unsupported pipe search request version: {version}.");

        var length = await ReadInt32Async(stream, token).ConfigureAwait(false);
        if (length < 0 || length > 10 * 1024 * 1024)
            throw new InvalidDataException($"Invalid search request payload length: {length}");

        var payload = await ReadExactlyAsync(stream, length, token).ConfigureAwait(false);

        var offset = 0;
        var id = (SearchRequestId)payload[offset++];
        var msg = new SearchRequestMessage { Id = id };

        switch (id)
        {
            case SearchRequestId.SetMachineSettings:
                msg.MachineSettings = ReadMachineSettings(payload, ref offset);
                break;
            case SearchRequestId.RebuildDrive:
            case SearchRequestId.DeleteDriveIndex:
            case SearchRequestId.CancelDriveIndex:
                msg.Drive = ReadString(payload, ref offset);
                break;
            case SearchRequestId.Search:
                msg.Limit = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
                offset += 4;
                msg.AppLimit = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
                offset += 4;
                msg.Query = ReadString(payload, ref offset);
                msg.DisabledAliasComponents = ReadStringList(payload, ref offset);
                msg.ExactMatch = payload[offset++] != 0;
                break;
            case SearchRequestId.SearchDir:
                msg.Limit = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
                offset += 4;
                msg.AppLimit = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
                offset += 4;
                msg.DirectoryFilter = ReadString(payload, ref offset);
                msg.Query = ReadString(payload, ref offset);
                msg.DisabledAliasComponents = ReadStringList(payload, ref offset);
                msg.ExactMatch = payload[offset++] != 0;
                break;
            case SearchRequestId.EnumerateDir:
                msg.Limit = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
                offset += 4;
                msg.DirectoryFilter = ReadString(payload, ref offset);
                msg.Query = ReadString(payload, ref offset);
                msg.Recursive = payload[offset++] != 0;
                break;
            case SearchRequestId.GetFileMetadata:
                msg.FilePaths = ReadStringList(payload, ref offset);
                break;
            case SearchRequestId.GetRecentFiles:
                msg.Limit = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
                offset += 4;
                msg.MaxAgeMinutes = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
                offset += 4;
                msg.Directories = ReadStringList(payload, ref offset);
                break;
            case SearchRequestId.LaunchHook:
                msg.RequestElevation = payload[offset++] != 0;
                break;
            case SearchRequestId.SubscribeDirectoryChanges:
                msg.Directories = ReadStringList(payload, ref offset);
                break;
        }

        return msg;
    }

    private static int GetStringByteCount(string? str) => Encoding.UTF8.GetByteCount(str ?? string.Empty);

    private static int CalculateSettingsSize(MachineSettings settings)
    {
        var size = 4; // Count
        foreach (var drive in settings.LocalDrives)
            size += GetStringByteCount(drive) + 5;
        return size;
    }

    private static void WriteString(Span<byte> buffer, ref int offset, string? str)
    {
        var s = str ?? string.Empty;
        var len = Encoding.UTF8.GetByteCount(s);
        Write7BitEncodedInt(buffer, ref offset, len);
        Encoding.UTF8.GetBytes(s, buffer.Slice(offset));
        offset += len;
    }

    private static void WriteMachineSettings(Span<byte> span, ref int offset, MachineSettings settings)
    {
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), settings.LocalDrives.Count);
        offset += 4;
        foreach (var drive in settings.LocalDrives)
            WriteString(span, ref offset, drive);
    }

    private static MachineSettings ReadMachineSettings(byte[] payload, ref int offset)
    {
        var count = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
        offset += 4;
        var settings = new MachineSettings();
        for (var i = 0; i < count; i++)
            settings.LocalDrives.Add(ReadString(payload, ref offset));
        return settings;
    }

    private static void Write7BitEncodedInt(Span<byte> destination, ref int offset, int value)
    {
        var uValue = (uint)value;
        while (uValue >= 0x80)
        {
            destination[offset++] = (byte)(uValue | 0x80);
            uValue >>= 7;
        }
        destination[offset++] = (byte)uValue;
    }

    private static int Read7BitEncodedInt(byte[] buffer, ref int offset)
    {
        uint result = 0;
        var shift = 0;
        while (shift < 35)
        {
            var b = buffer[offset++];
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
        var str = Encoding.UTF8.GetString(buffer, offset, length);
        offset += length;
        return str;
    }

    private static async Task<int> ReadInt32Async(Stream stream, CancellationToken token)
    {
        var bytes = await ReadExactlyAsync(stream, sizeof(int), token).ConfigureAwait(false);
        return BinaryPrimitives.ReadInt32LittleEndian(bytes);
    }

    private static async Task<byte[]> ReadExactlyAsync(Stream stream, int count, CancellationToken token)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), token).ConfigureAwait(false);
            if (read <= 0)
                throw new EndOfStreamException($"End of stream reached. Read {offset} of {count} bytes.");
            offset += read;
        }
        return buffer;
    }

    private static int CalculateStringListSize(List<string>? list)
    {
        var size = 4; // Count
        if (list != null)
        {
            foreach (var s in list)
                size += GetStringByteCount(s) + 5;
        }
        return size;
    }

    private static void WriteStringList(Span<byte> span, ref int offset, List<string>? list)
    {
        var count = list?.Count ?? 0;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), count);
        offset += 4;
        if (list != null)
        {
            foreach (var s in list)
                WriteString(span, ref offset, s);
        }
    }

    private static List<string> ReadStringList(byte[] payload, ref int offset)
    {
        var count = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
        offset += 4;
        var list = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            list.Add(ReadString(payload, ref offset));
        }
        return list;
    }
}
