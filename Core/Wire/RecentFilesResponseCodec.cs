using System.Buffers.Binary;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.Core.Wire;

// The RecentFiles PipeResponse kind's own codec -- kept as its own static class (not a partial split
// of PipeResponseBinarySerializer) to stay under the repo's per-file line limit.
public static class RecentFilesResponseCodec
{
    public static Task WriteRecentFilesAsync(Stream stream, List<SearchResult> recentFiles, CancellationToken token = default)
        => PipeResponseBinarySerializer.WriteAsync(stream, new PipeResponse { Kind = PipeResponseKind.RecentFiles, RecentFiles = recentFiles }, token);

    // Name/Path/IsDir/Drive/modified-time only -- Attributes isn't read by any caller (see
    // SearchResultHelper.CreateUiResult). The modified time is carried so SearchService.GetRecentFilesAsync
    // can merge this response with the network/WSL result set by actual recency instead of just
    // concatenating -- reconstructed into a Metadata with only Modified set (Size/Created/Accessed aren't
    // needed for Recent Files and were never in this wire format).
    internal static int CalculateRecentFilesSize(List<SearchResult> recentFiles)
    {
        var size = 4; // Count
        foreach (var item in recentFiles)
            size += PipeResponseBinarySerializer.GetStringByteCount(item.Name) + 5 + PipeResponseBinarySerializer.GetStringByteCount(item.Path) + 5 + 1 + PipeResponseBinarySerializer.GetStringByteCount(item.Drive) + 5 + 4;
        return size;
    }

    internal static void WriteRecentFiles(Span<byte> span, ref int offset, List<SearchResult> recentFiles)
    {
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), recentFiles.Count);
        offset += 4;
        foreach (var item in recentFiles)
        {
            PipeResponseBinarySerializer.WriteString(span, ref offset, item.Name);
            PipeResponseBinarySerializer.WriteString(span, ref offset, item.Path);
            span[offset++] = (byte)(item.IsDir ? 1 : 0);
            PipeResponseBinarySerializer.WriteString(span, ref offset, item.Drive);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset), FileTimeHelper.ToUnixSeconds(item.Metadata.Modified.ToUniversalTime()));
            offset += 4;
        }
    }

    internal static List<SearchResult> ReadRecentFiles(byte[] payload, ref int offset)
    {
        var count = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
        offset += 4;
        if (count < 0 || count > (payload.Length - offset) / 8)
            throw new InvalidDataException("Invalid recent files count.");
        var recentFiles = new List<SearchResult>(count);
        for (var i = 0; i < count; i++)
        {
            var name = PipeResponseBinarySerializer.ReadString(payload, ref offset);
            var path = PipeResponseBinarySerializer.ReadString(payload, ref offset);
            var isDir = payload[offset++] != 0;
            var drive = PipeResponseBinarySerializer.ReadString(payload, ref offset);
            var modifiedUtc = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset));
            offset += 4;
            var modified = FileTimeHelper.FromUnixSeconds(modifiedUtc).ToLocalTime();
            recentFiles.Add(new SearchResult { Name = name, Path = path, IsDir = isDir, Drive = drive, Metadata = new FileMetadata(0, DateTime.MinValue, modified, DateTime.MinValue) });
        }
        return recentFiles;
    }
}
