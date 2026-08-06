using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.Core.Wire;

// Dedicated wire format for AppSearchPipeService's own pipe -- deliberately NOT an extension of
// SearchResponseBinarySerializer (the production LertaroPipe/elevated-service protocol), to keep this
// prototype's protocol additions fully isolated from that already-shipped format; nothing here is read
// or written by LertaroPipe's own client/server pair. Carries the same per-result fields
// SearchResponseBinarySerializer does, plus which character ranges of the result's own Name matched the
// query -- computed server-side (inside the App process, which has plugins loaded) via
// FuzzyMatcher.ComputeHighlightMask, since a bare client has no way to compute that correctly itself for
// a non-ASCII/pinyin-alias-matched name (see AppSearchPipeService's own comment on why this pipe exists
// at all).
public static class SearchResultWithHighlightBinarySerializer
{
    private const int Magic = 0x53524C48; // "HLRS" as bytes, arbitrary but distinct from SearchResponseBinarySerializer's own magic
    private const int Version = 2; // v2: gained Attributes (see SearchResponseBinarySerializer's own v5 for why)
    private const byte EndFrame = 0;
    private const byte FileResultFrame = 1;
    private const byte HeaderFrame = 255;

    public static async Task WriteHeaderAsync(Stream stream, CancellationToken token = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(13);
        try
        {
            var span = buffer.AsSpan();
            BinaryPrimitives.WriteInt32LittleEndian(span[..4], Magic);
            span[4] = HeaderFrame;
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(5, 4), 4);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(9, 4), Version);
            await stream.WriteAsync(buffer.AsMemory(0, 13), token).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static async Task WriteEndAsync(Stream stream, CancellationToken token = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(9);
        try
        {
            var span = buffer.AsSpan();
            BinaryPrimitives.WriteInt32LittleEndian(span[..4], Magic);
            span[4] = EndFrame;
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(5, 4), 0);
            await stream.WriteAsync(buffer.AsMemory(0, 9), token).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // `highlightRanges` is a flat (start, length) pair sequence over `result.Name` -- e.g. [2, 1, 7, 3]
    // means chars [2,3) and [7,10) matched. See FlattenMask for the usual bool[]-mask-to-ranges
    // conversion. Capped at 255 disjoint ranges (a filename practically never has more; if it somehow
    // did, the excess just doesn't highlight rather than corrupting the frame).
    public static async Task WriteFileResultAsync(Stream stream, SearchResult result, IReadOnlyList<int> highlightRanges, CancellationToken token = default)
    {
        var name = result.Name ?? string.Empty;
        var path = result.Path ?? string.Empty;
        var drive = result.Drive ?? string.Empty;

        var nameLen = Encoding.UTF8.GetByteCount(name);
        var pathLen = Encoding.UTF8.GetByteCount(path);
        var driveLen = Encoding.UTF8.GetByteCount(drive);
        var rangeCount = Math.Min(highlightRanges.Count / 2, 255);

        var maxPayloadSize = nameLen + pathLen + driveLen + 48 + 1 + rangeCount * 4;
        var totalSize = 9 + maxPayloadSize;

        var buffer = ArrayPool<byte>.Shared.Rent(totalSize);
        try
        {
            var span = buffer.AsSpan();
            var offset = 0;

            BinaryPrimitives.WriteInt32LittleEndian(span[..4], Magic);
            offset += 4;
            span[offset++] = FileResultFrame;

            var payloadLengthOffset = offset;
            offset += 4;
            var payloadStart = offset;

            VarIntStringCodec.WriteString(span, ref offset, name);
            VarIntStringCodec.WriteString(span, ref offset, path);
            span[offset++] = (byte)(result.IsDir ? 1 : 0);
            VarIntStringCodec.WriteString(span, ref offset, drive);

            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), result.RankSortKey);
            offset += 8;
            BinaryPrimitives.WriteInt64LittleEndian(span.Slice(offset, 8), result.Metadata.Size);
            offset += 8;
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), FileTimeHelper.ToUnixSeconds(result.Metadata.Created.ToUniversalTime()));
            offset += 4;
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), FileTimeHelper.ToUnixSeconds(result.Metadata.Modified.ToUniversalTime()));
            offset += 4;
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), FileTimeHelper.ToUnixSeconds(result.Metadata.Accessed.ToUniversalTime()));
            offset += 4;

            // See SearchResponseBinarySerializer's own identical addition for why this is here.
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset, 4), (int)result.Attributes);
            offset += 4;

            span[offset++] = (byte)rangeCount;
            for (var i = 0; i < rangeCount; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, 2), (ushort)Math.Clamp(highlightRanges[i * 2], 0, ushort.MaxValue));
                offset += 2;
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, 2), (ushort)Math.Clamp(highlightRanges[i * 2 + 1], 0, ushort.MaxValue));
                offset += 2;
            }

            var payloadLength = offset - payloadStart;
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(payloadLengthOffset, 4), payloadLength);

            await stream.WriteAsync(buffer.AsMemory(0, offset), token).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static async Task ReadAsync(Stream stream, Action<SearchResult, int[]> onResult, CancellationToken token = default)
    {
        while (true)
        {
            var magic = await ReadInt32Async(stream, token).ConfigureAwait(false);
            if (magic != Magic)
                throw new InvalidDataException($"Invalid search-with-highlight response magic: {magic:X}");

            var frameType = await ReadByteAsync(stream, token).ConfigureAwait(false);
            var length = await ReadInt32Async(stream, token).ConfigureAwait(false);
            if (length < 0 || length > 10 * 1024 * 1024)
                throw new InvalidDataException($"Invalid search-with-highlight payload length: {length}");

            var payload = await ReadExactlyAsync(stream, length, token).ConfigureAwait(false);

            if (frameType == EndFrame)
                return;

            if (frameType == HeaderFrame)
            {
                if (payload.Length < 4)
                    throw new InvalidDataException("Invalid header payload length.");
                var version = BinaryPrimitives.ReadInt32LittleEndian(payload);
                if (version != Version)
                    throw new InvalidDataException($"Unsupported search-with-highlight binary version: {version}. Expected: {Version}");
                continue;
            }

            if (frameType == FileResultFrame)
            {
                var (result, ranges) = ReadResult(payload);
                onResult(result, ranges);
                continue;
            }

            throw new InvalidDataException($"Unknown search-with-highlight frame: {frameType}.");
        }
    }

    // Converts a bool[] highlight mask (one entry per character of the matched text, e.g. from
    // FuzzyMatcher.ComputeHighlightMask) into the compact flat (start,length) pair encoding
    // WriteFileResultAsync expects.
    public static int[] FlattenMask(bool[]? mask)
    {
        if (mask == null || mask.Length == 0)
            return Array.Empty<int>();

        var ranges = new List<int>();
        var i = 0;
        while (i < mask.Length)
        {
            if (!mask[i])
            {
                i++;
                continue;
            }
            var start = i;
            while (i < mask.Length && mask[i]) i++;
            ranges.Add(start);
            ranges.Add(i - start);
        }
        return ranges.ToArray();
    }

    private static (SearchResult Result, int[] HighlightRanges) ReadResult(byte[] payload)
    {
        var offset = 0;
        var name = VarIntStringCodec.ReadString(payload, ref offset);
        var path = VarIntStringCodec.ReadString(payload, ref offset);
        var isDir = payload[offset++] != 0;
        var drive = VarIntStringCodec.ReadString(payload, ref offset);
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
        offset += 4;

        var rangeCount = payload[offset++];
        var ranges = new int[rangeCount * 2];
        for (var i = 0; i < rangeCount; i++)
        {
            ranges[i * 2] = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(offset));
            offset += 2;
            ranges[i * 2 + 1] = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(offset));
            offset += 2;
        }

        var result = new SearchResult
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
        return (result, ranges);
    }

    private static async Task<int> ReadInt32Async(Stream stream, CancellationToken token)
    {
        var bytes = await ReadExactlyAsync(stream, sizeof(int), token).ConfigureAwait(false);
        return BinaryPrimitives.ReadInt32LittleEndian(bytes);
    }

    private static async Task<byte> ReadByteAsync(Stream stream, CancellationToken token)
    {
        var bytes = await ReadExactlyAsync(stream, 1, token).ConfigureAwait(false);
        return bytes[0];
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
