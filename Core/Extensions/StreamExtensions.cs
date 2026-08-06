namespace Lertaro.Core.Extensions;

public static class StreamExtensions
{
    public static async Task<int> ReadInt32Async(this Stream stream, CancellationToken token)
    {
        var bytes = await stream.ReadExactlyAsync(sizeof(int), token).ConfigureAwait(false);
        return BitConverter.ToInt32(bytes, 0);
    }

    public static async Task<byte[]> ReadExactlyAsync(this Stream stream, int count, CancellationToken token)
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
