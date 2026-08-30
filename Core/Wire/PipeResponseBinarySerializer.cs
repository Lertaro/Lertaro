using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using Lertaro.Core.Indexer.Usn;
using Lertaro.Core.IndexV2.Space;
namespace Lertaro.Core.Wire;

public enum PipeResponseKind : byte
{
    Ok = 1,
    Error = 2,
    Status = 3,
    MachineSettings = 4,
    FileMetadata = 5,
    RecentFiles = 6,
    HookLaunched = 7,
    // Pushed, unprompted, on a SubscribeDirectoryChanges connection: the watched directories that a
    // change just landed under. Carries only what the subscriber asked about, so it is a few paths on
    // the rare occasion one is touched, not a running commentary on the volume.
    DirectoriesChanged = 8,
    SpaceEntries = 9
}
public readonly struct PipeResponse
{
    public PipeResponseKind Kind { get; init; }
    public string Message { get; init; }
    public UsnIndexer.IndexerStatus? Status { get; init; }
    /// <summary>Which of the subscriber's watched directories a change landed under.</summary>
    public List<string>? ChangedDirectories { get; init; }
    public MachineSettings? MachineSettings { get; init; }
    public Dictionary<string, FileMetadataEntry>? FileMetadata { get; init; }
    public List<SearchResult>? RecentFiles { get; init; }
    public IReadOnlyList<SpaceIndexEntry>? SpaceEntries { get; init; }
    public int Pid { get; init; }
    public bool IsOk => Kind != PipeResponseKind.Error;
}
public static class PipeResponseBinarySerializer
{
    private const int Magic = 0x52504C53; // SLPR
    // v4: RecentFiles gained ModifiedUtc; v5: drive revisions; v6: in-memory space entries.
    private const int Version = 6;

    public static Task WriteOkAsync(Stream stream, CancellationToken token = default)
        => WriteAsync(stream, new PipeResponse { Kind = PipeResponseKind.Ok }, token);
    public static Task WriteErrorAsync(Stream stream, string message, CancellationToken token = default)
        => WriteAsync(stream, new PipeResponse { Kind = PipeResponseKind.Error, Message = message }, token);
    public static Task WriteStatusAsync(Stream stream, UsnIndexer.IndexerStatus status, CancellationToken token = default)
        => WriteAsync(stream, new PipeResponse { Kind = PipeResponseKind.Status, Status = status }, token);
    public static Task WriteDirectoriesChangedAsync(Stream stream, List<string> directories, CancellationToken token = default)
        => WriteAsync(stream, new PipeResponse { Kind = PipeResponseKind.DirectoriesChanged, ChangedDirectories = directories }, token);
    public static Task WriteMachineSettingsAsync(Stream stream, MachineSettings settings, CancellationToken token = default)
        => WriteAsync(stream, new PipeResponse { Kind = PipeResponseKind.MachineSettings, MachineSettings = settings }, token);
    public static Task WriteFileMetadataAsync(Stream stream, Dictionary<string, FileMetadataEntry> metadata, CancellationToken token = default)
        => WriteAsync(stream, new PipeResponse { Kind = PipeResponseKind.FileMetadata, FileMetadata = metadata }, token);
    public static Task WriteHookLaunchAsync(Stream stream, int pid, CancellationToken token = default)
        => WriteAsync(stream, new PipeResponse { Kind = PipeResponseKind.HookLaunched, Pid = pid }, token);
    public static Task WriteSpaceEntriesAsync(Stream stream, IReadOnlyList<SpaceIndexEntry> entries, CancellationToken token = default)
        => WriteAsync(stream, new PipeResponse { Kind = PipeResponseKind.SpaceEntries, SpaceEntries = entries }, token);
    public static async Task<PipeResponse> ReadAsync(Stream stream, CancellationToken token = default)
    {
        var magic = await ReadInt32Async(stream, token).ConfigureAwait(false);
        if (magic != Magic)
            throw new InvalidDataException("Invalid pipe response binary header.");

        var version = await ReadInt32Async(stream, token).ConfigureAwait(false);
        if (version != Version)
            throw new InvalidDataException($"Unsupported pipe response binary version: {version}.");

        var length = await ReadInt32Async(stream, token).ConfigureAwait(false);
        if (length < 0 || length > 64 * 1024 * 1024)
            throw new InvalidDataException($"Invalid response payload length: {length}");
        var payload = await ReadExactlyAsync(stream, length, token).ConfigureAwait(false);

        var offset = 0;
        var kind = (PipeResponseKind)payload[offset++];

        return kind switch
        {
            PipeResponseKind.Ok => new PipeResponse { Kind = kind },
            PipeResponseKind.Error => new PipeResponse { Kind = kind, Message = ReadString(payload, ref offset) },
            PipeResponseKind.Status => new PipeResponse { Kind = kind, Status = PipeResponseStatusSerializer.Read(payload, ref offset) },
            PipeResponseKind.MachineSettings => new PipeResponse { Kind = kind, MachineSettings = ReadMachineSettings(payload, ref offset) },
            PipeResponseKind.FileMetadata => new PipeResponse { Kind = kind, FileMetadata = FileMetadataResponseCodec.Read(payload, ref offset) },
            PipeResponseKind.RecentFiles => new PipeResponse { Kind = kind, RecentFiles = RecentFilesResponseCodec.ReadRecentFiles(payload, ref offset) },
            PipeResponseKind.HookLaunched => new PipeResponse { Kind = kind, Pid = ReadInt32(payload, ref offset) },
            PipeResponseKind.DirectoriesChanged => new PipeResponse { Kind = kind, ChangedDirectories = ReadDirectories(payload, ref offset) },
            PipeResponseKind.SpaceEntries => new PipeResponse { Kind = kind, SpaceEntries = SpaceEntriesResponseCodec.Read(payload, ref offset) },
            _ => throw new InvalidDataException($"Unknown pipe response kind: {kind}.")
        };
    }

    internal static async Task WriteAsync(Stream stream, PipeResponse response, CancellationToken token)
    {
        var payloadSize = 1; // Kind byte
        switch (response.Kind)
        {
            case PipeResponseKind.Error:
                payloadSize += GetStringByteCount(response.Message) + 5;
                break;
            case PipeResponseKind.Status:
                payloadSize += PipeResponseStatusSerializer.CalculateSize(response.Status ?? new UsnIndexer.IndexerStatus { State = "error" });
                break;
            case PipeResponseKind.MachineSettings:
                payloadSize += CalculateSettingsSize(response.MachineSettings ?? new MachineSettings());
                break;
            case PipeResponseKind.FileMetadata:
                payloadSize += FileMetadataResponseCodec.CalculateSize(response.FileMetadata ?? new Dictionary<string, FileMetadataEntry>());
                break;
            case PipeResponseKind.RecentFiles:
                payloadSize += RecentFilesResponseCodec.CalculateRecentFilesSize(response.RecentFiles ?? new List<SearchResult>());
                break;
            case PipeResponseKind.HookLaunched:
                payloadSize += 4;
                break;
            case PipeResponseKind.DirectoriesChanged:
                payloadSize += 4; // count
                foreach (var directory in response.ChangedDirectories ?? new List<string>())
                    payloadSize += GetStringByteCount(directory) + 5;
                break;
            case PipeResponseKind.SpaceEntries:
                payloadSize += SpaceEntriesResponseCodec.CalculateSize(response.SpaceEntries ?? Array.Empty<SpaceIndexEntry>());
                break;
        }
        var totalSize = 12 + payloadSize; // Magic(4) + Version(4) + Length(4) + Payload
        var buffer = ArrayPool<byte>.Shared.Rent(totalSize);
        try
        {
            var span = buffer.AsSpan();
            var offset = 12;
            span[offset++] = (byte)response.Kind;

            switch (response.Kind)
            {
                case PipeResponseKind.Error:
                    WriteString(span, ref offset, response.Message);
                    break;
                case PipeResponseKind.Status:
                    PipeResponseStatusSerializer.Write(span, ref offset, response.Status ?? new UsnIndexer.IndexerStatus { State = "error" });
                    break;
                case PipeResponseKind.MachineSettings:
                    WriteMachineSettings(span, ref offset, response.MachineSettings ?? new MachineSettings());
                    break;
                case PipeResponseKind.FileMetadata:
                    FileMetadataResponseCodec.Write(span, ref offset, response.FileMetadata ?? new Dictionary<string, FileMetadataEntry>());
                    break;
                case PipeResponseKind.RecentFiles:
                    RecentFilesResponseCodec.WriteRecentFiles(span, ref offset, response.RecentFiles ?? new List<SearchResult>());
                    break;
                case PipeResponseKind.HookLaunched:
                    BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), response.Pid);
                    offset += 4;
                    break;
                case PipeResponseKind.DirectoriesChanged:
                    var changed = response.ChangedDirectories ?? new List<string>();
                    BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), changed.Count);
                    offset += 4;
                    foreach (var directory in changed)
                        WriteString(span, ref offset, directory);
                    break;
                case PipeResponseKind.SpaceEntries:
                    SpaceEntriesResponseCodec.Write(span, ref offset, response.SpaceEntries ?? Array.Empty<SpaceIndexEntry>());
                    break;
            }

            var actualPayloadSize = offset - 12;
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(0), Magic);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4), Version);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(8), actualPayloadSize);

            await stream.WriteAsync(buffer.AsMemory(0, offset), token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static List<string> ReadDirectories(byte[] payload, ref int offset)
    {
        var count = ReadInt32(payload, ref offset);
        if (count < 0 || count > payload.Length - offset)
            throw new InvalidDataException("Invalid directory count.");
        var directories = new List<string>(count);
        for (var i = 0; i < count; i++)
            directories.Add(ReadString(payload, ref offset));
        return directories;
    }

    internal static int GetStringByteCount(string? str) => Encoding.UTF8.GetByteCount(str ?? string.Empty);

    private static int CalculateSettingsSize(MachineSettings settings)
    {
        var size = 4; // Count
        foreach (var drive in settings.LocalDrives)
            size += GetStringByteCount(drive) + 5;
        return size;
    }

    internal static void WriteString(Span<byte> buffer, ref int offset, string? str)
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
        if (count < 0 || count > payload.Length - offset)
            throw new InvalidDataException("Invalid machine settings drive count.");
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

    private static int ReadInt32(byte[] buffer, ref int offset)
    {
        var value = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(offset));
        offset += 4;
        return value;
    }

    internal static string ReadString(byte[] buffer, ref int offset)
    {
        var length = Read7BitEncodedInt(buffer, ref offset);
        if (length == 0) return string.Empty;
        if (length < 0 || length > buffer.Length - offset)
            throw new InvalidDataException("Invalid string length.");
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
}
