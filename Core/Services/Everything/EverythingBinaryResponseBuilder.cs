using System.Text;

namespace Lertaro.Core.Services.Everything;

/// <summary>Serializes Everything search result lists into binary buffers for IPC WM_COPYDATA replies.</summary>
public static class EverythingBinaryResponseBuilder
{
    private const int ListV1HeaderSize = 28;
    private const int ItemV1Size = 12;

    private const int ListV2HeaderSize = 20;
    private const int ItemV2Size = 8;

    public static byte[] BuildListV1(IReadOnlyList<EverythingResultItem> items, uint totalFolders, uint totalFiles, uint offset, bool isUnicode)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var encoding = isUnicode ? Encoding.Unicode : EverythingAnsiEncoding.Instance;

        var numItems = (uint)items.Count;
        var numFolders = (uint)items.Count(i => i.IsDirectory);
        var numFiles = numItems - numFolders;

        writer.Write(totalFolders);
        writer.Write(totalFiles);
        writer.Write(totalFolders + totalFiles);
        writer.Write(numFolders);
        writer.Write(numFiles);
        writer.Write(numItems);
        writer.Write(offset);

        // Pre-allocate space for item headers
        var itemArrayOffset = ListV1HeaderSize;
        var stringHeapOffset = itemArrayOffset + (int)numItems * ItemV1Size;
        stream.Position = stringHeapOffset;

        var itemHeaders = new (uint flags, uint nameOffset, uint pathOffset)[numItems];
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var flags = (item.IsDirectory ? EverythingIpcConstants.ItemFlagFolder : 0u) |
                        (item.IsDrive ? EverythingIpcConstants.ItemFlagDrive : 0u);

            var nameOffset = (uint)stream.Position;
            WriteStringNullTerminated(writer, item.FileName, encoding);

            var pathOffset = (uint)stream.Position;
            WriteStringNullTerminated(writer, item.Path, encoding);

            itemHeaders[i] = (flags, nameOffset, pathOffset);
        }

        // Write back item headers
        stream.Position = itemArrayOffset;
        for (var i = 0; i < numItems; i++)
        {
            writer.Write(itemHeaders[i].flags);
            writer.Write(itemHeaders[i].nameOffset);
            writer.Write(itemHeaders[i].pathOffset);
        }

        return stream.ToArray();
    }

    public static byte[] BuildListV2(IReadOnlyList<EverythingResultItem> items, uint totalItems, uint offset, uint requestFlags, uint sortType, bool isUnicode)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var encoding = isUnicode ? Encoding.Unicode : EverythingAnsiEncoding.Instance;
        var numItems = (uint)items.Count;

        writer.Write(totalItems);
        writer.Write(numItems);
        writer.Write(offset);
        writer.Write(requestFlags);
        writer.Write(sortType);

        var itemArrayOffset = ListV2HeaderSize;
        var dataChunksOffset = itemArrayOffset + (int)numItems * ItemV2Size;
        stream.Position = dataChunksOffset;

        var itemHeaders = new (uint flags, uint dataOffset)[numItems];
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var flags = (item.IsDirectory ? EverythingIpcConstants.ItemFlagFolder : 0u) |
                        (item.IsDrive ? EverythingIpcConstants.ItemFlagDrive : 0u);
            var dataOffset = (uint)stream.Position;

            WriteItemV2Data(writer, item, requestFlags, encoding);
            itemHeaders[i] = (flags, dataOffset);
        }

        stream.Position = itemArrayOffset;
        for (var i = 0; i < numItems; i++)
        {
            writer.Write(itemHeaders[i].flags);
            writer.Write(itemHeaders[i].dataOffset);
        }

        return stream.ToArray();
    }

    private static void WriteItemV2Data(BinaryWriter writer, EverythingResultItem item, uint requestFlags, Encoding encoding)
    {
        if ((requestFlags & EverythingIpcConstants.RequestFileName) != 0)
            WriteLengthPrefixedString(writer, item.FileName, encoding);

        if ((requestFlags & EverythingIpcConstants.RequestPath) != 0)
            WriteLengthPrefixedString(writer, item.Path, encoding);

        if ((requestFlags & EverythingIpcConstants.RequestFullPathAndFileName) != 0)
        {
            var fullPath = item.Path.EndsWith('\\') ? item.Path + item.FileName : item.Path + "\\" + item.FileName;
            WriteLengthPrefixedString(writer, fullPath, encoding);
        }

        if ((requestFlags & EverythingIpcConstants.RequestSize) != 0)
            writer.Write(item.Size);

        if ((requestFlags & EverythingIpcConstants.RequestExtension) != 0)
        {
            var ext = item.IsDirectory ? string.Empty : Path.GetExtension(item.FileName).TrimStart('.');
            WriteLengthPrefixedString(writer, ext, encoding);
        }

        if ((requestFlags & EverythingIpcConstants.RequestDateCreated) != 0)
            writer.Write(item.DateCreated?.ToFileTimeUtc() ?? 0L);

        if ((requestFlags & EverythingIpcConstants.RequestDateModified) != 0)
            writer.Write(item.DateModified?.ToFileTimeUtc() ?? 0L);

        if ((requestFlags & EverythingIpcConstants.RequestDateAccessed) != 0)
            writer.Write(item.DateAccessed?.ToFileTimeUtc() ?? 0L);

        if ((requestFlags & EverythingIpcConstants.RequestAttributes) != 0)
        {
            var attrs = item.Attributes != 0
                ? item.Attributes
                : (item.IsDirectory ? (uint)FileAttributes.Directory : (uint)FileAttributes.Normal);
            writer.Write(attrs);
        }

        if ((requestFlags & EverythingIpcConstants.RequestFileListFileName) != 0)
            WriteLengthPrefixedString(writer, string.Empty, encoding);

        if ((requestFlags & EverythingIpcConstants.RequestRunCount) != 0)
            writer.Write(item.RunCount);

        if ((requestFlags & EverythingIpcConstants.RequestDateRun) != 0)
            writer.Write(item.DateRun?.ToFileTimeUtc() ?? 0L);

        if ((requestFlags & EverythingIpcConstants.RequestDateRecentlyChanged) != 0)
            writer.Write(item.DateRecentlyChanged?.ToFileTimeUtc() ?? 0L);

        if ((requestFlags & EverythingIpcConstants.RequestHighlightedFileName) != 0)
            WriteLengthPrefixedString(writer, item.FileName, encoding);

        if ((requestFlags & EverythingIpcConstants.RequestHighlightedPath) != 0)
            WriteLengthPrefixedString(writer, item.Path, encoding);

        if ((requestFlags & EverythingIpcConstants.RequestHighlightedFullPathAndFileName) != 0)
        {
            var fullPath = item.Path.EndsWith('\\') ? item.Path + item.FileName : item.Path + "\\" + item.FileName;
            WriteLengthPrefixedString(writer, fullPath, encoding);
        }
    }

    private static void WriteLengthPrefixedString(BinaryWriter writer, string text, Encoding encoding)
    {
        var str = text ?? string.Empty;
        // The SDK's QUERY2 ITEM2 data chunks prefix each string with its length "in characters":
        // that is WCHAR count for the Unicode variants, but CHAR (encoded byte) count for the
        // ANSI variants, because the chunks are walked by byte offsets. str.Length is only
        // correct for Encoding.Unicode.
        var length = encoding == Encoding.Unicode ? str.Length : encoding.GetByteCount(str);
        writer.Write((uint)length);
        WriteStringNullTerminated(writer, str, encoding);
    }

    private static void WriteStringNullTerminated(BinaryWriter writer, string text, Encoding encoding)
    {
        var str = text ?? string.Empty;
        var bytes = encoding.GetBytes(str);
        writer.Write(bytes);
        if (encoding == Encoding.Unicode)
        {
            writer.Write((byte)0);
            writer.Write((byte)0);
        }
        else
        {
            writer.Write((byte)0);
        }
    }
}
