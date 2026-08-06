using System.Buffers;
using System.Buffers.Binary;

namespace Lertaro.Core.Wire;

public static class SearchResponseBinarySerializer
{
    private const int Magic = 0x53524C53; // SLRS
    // v4: gained Size/Created/Modified/Accessed (SearchResult.Metadata); v5: gained Attributes;
    // v6: gained the NotIndexed frame.
    private const int Version = 6;
    private const byte EndFrame = 0;
    private const byte FileResultFrame = 1;
    private const byte AppResultFrame = 2;
    // "No loaded index covers what you asked for" -- distinct from an empty result set, which a stream
    // of zero results is indistinguishable from. Only ever written for EnumerateDir, whose caller has a
    // real filesystem walk to fall back to; a search has nowhere to fall back to and never sends it.
    private const byte NotIndexedFrame = 3;
    private const byte HeaderFrame = 255;

    public static async Task WriteHeaderAsync(Stream stream, CancellationToken token = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(13);
        try
        {
            var span = buffer.AsSpan();
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(0), Magic);
            span[4] = HeaderFrame;
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(5), 4);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(9), Version);

            await stream.WriteAsync(buffer.AsMemory(0, 13), token).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }



    public static Task WriteFileResultAsync(Stream stream, SearchResult result, CancellationToken token = default)
        => WriteResultAsync(stream, FileResultFrame, result, token);

    public static Task WriteEndAsync(Stream stream, CancellationToken token = default)
        => WriteEmptyFrameAsync(stream, EndFrame, token);

    public static Task WriteNotIndexedAsync(Stream stream, CancellationToken token = default)
        => WriteEmptyFrameAsync(stream, NotIndexedFrame, token);

    private static async Task WriteEmptyFrameAsync(Stream stream, byte frame, CancellationToken token)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(9);
        try
        {
            var span = buffer.AsSpan();
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(0), Magic);
            span[4] = frame;
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(5), 0);

            await stream.WriteAsync(buffer.AsMemory(0, 9), token).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // onNotIndexed fires (before the End frame) when the responder answered "that path is in no loaded
    // index" -- see NotIndexedFrame. Callers that can't act on it simply don't pass it.
    public static async Task ReadAsync(Stream stream, Action<SearchResult> onResult, CancellationToken token = default, Action? onNotIndexed = null)
    {
        try
        {
            while (true)
            {
                var magic = await ReadInt32Async(stream, token).ConfigureAwait(false);
                if (magic != Magic)
                {
                    Logger.Log($"[Serializer ERROR] Invalid magic: {magic:X}. Expected: {Magic:X}", LogLevel.Error);
                    throw new InvalidDataException($"Invalid search response magic: {magic:X}. Expected: {Magic:X}");
                }

                var frameType = await ReadByteAsync(stream, token).ConfigureAwait(false);
                var length = await ReadInt32Async(stream, token).ConfigureAwait(false);
                if (length < 0 || length > 10 * 1024 * 1024)
                {
                    Logger.Log($"[Serializer ERROR] Invalid length: {length}. Magic={magic:X}, FrameType={frameType}", LogLevel.Error);
                    throw new InvalidDataException($"Invalid search response payload length: {length}");
                }

                var payload = await ReadExactlyAsync(stream, length, token).ConfigureAwait(false);
                if (frameType == EndFrame)
                    return;

                if (frameType == HeaderFrame)
                {
                    if (payload.Length < 4)
                        throw new InvalidDataException("Invalid header payload length.");
                    var version = BinaryPrimitives.ReadInt32LittleEndian(payload);
                    if (version != Version)
                        throw new InvalidDataException($"Unsupported search response binary version: {version}. Expected: {Version}");
                    continue;
                }

                if (frameType == FileResultFrame || frameType == AppResultFrame)
                {
                    onResult(SearchResultFrameCodec.ReadPayload(payload));
                    continue;
                }

                if (frameType == NotIndexedFrame)
                {
                    onNotIndexed?.Invoke();
                    continue;
                }

                throw new InvalidDataException($"Unknown search response frame: {frameType}.");
            }
        }
        catch (OperationCanceledException ex)
        {
            Logger.Log($"[Serializer Cancelled] {ex.Message}", LogLevel.Debug);
            throw;
        }
        catch (Exception ex)
        {
            Logger.Log($"[Serializer Exception] {ex.Message}", LogLevel.Error);
            throw;
        }
    }

    // Framing only: magic, frame byte, payload length. The payload itself is SearchResultFrameCodec's.
    private static async Task WriteResultAsync(Stream stream, byte frame, SearchResult result, CancellationToken token)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(9 + SearchResultFrameCodec.MaxPayloadSize(result));
        try
        {
            var span = buffer.AsSpan();
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(0), Magic);
            span[4] = frame;

            var payloadLength = SearchResultFrameCodec.WritePayload(span.Slice(9), result);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(5), payloadLength);

            await stream.WriteAsync(buffer.AsMemory(0, 9 + payloadLength), token).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
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
