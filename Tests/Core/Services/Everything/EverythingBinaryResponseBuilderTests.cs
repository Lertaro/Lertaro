using System.Text;
using Lertaro.Core.Services.Everything;

namespace Lertaro.Core.Tests.Services.Everything;

[TestClass]
public class EverythingBinaryResponseBuilderTests
{
    [TestMethod]
    public void BuildListV1_Unicode_ProducesValidHeaderAndOffsets()
    {
        var items = new List<EverythingResultItem>
        {
            new(@"C:\Folder", "file1.txt", 1024, false),
            new(@"C:\Folder", "SubFolder", 2048, true)
        };

        var bytes = EverythingBinaryResponseBuilder.BuildListV1(items, totalFolders: 1, totalFiles: 1, offset: 0, isUnicode: true);

        Assert.IsNotNull(bytes);
        Assert.IsGreaterThan(28 + (2 * 12), bytes.Length);

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);

        var totFolders = reader.ReadUInt32();
        var totFiles = reader.ReadUInt32();
        var totItems = reader.ReadUInt32();
        var numFolders = reader.ReadUInt32();
        var numFiles = reader.ReadUInt32();
        var numItems = reader.ReadUInt32();
        var offset = reader.ReadUInt32();

        Assert.AreEqual(1u, totFolders);
        Assert.AreEqual(1u, totFiles);
        Assert.AreEqual(2u, totItems);
        Assert.AreEqual(1u, numFolders);
        Assert.AreEqual(1u, numFiles);
        Assert.AreEqual(2u, numItems);
        Assert.AreEqual(0u, offset);

        // Read item 0
        var flag0 = reader.ReadUInt32();
        var nameOffset0 = reader.ReadUInt32();
        var pathOffset0 = reader.ReadUInt32();
        Assert.AreEqual(0u, flag0); // file

        // Read item 1
        var flag1 = reader.ReadUInt32();
        var nameOffset1 = reader.ReadUInt32();
        var pathOffset1 = reader.ReadUInt32();
        Assert.AreEqual(EverythingIpcConstants.ItemFlagFolder, flag1); // folder

        // Verify string content at nameOffset0
        stream.Position = nameOffset0;
        var name0 = ReadUnicodeNullTerminated(stream);
        Assert.AreEqual("file1.txt", name0);

        stream.Position = pathOffset0;
        var path0 = ReadUnicodeNullTerminated(stream);
        Assert.AreEqual(@"C:\Folder", path0);
    }

    [TestMethod]
    public void BuildListV2_WithSizeAndDates_ProducesCorrectBinaryLayout()
    {
        var now = DateTime.UtcNow;
        var items = new List<EverythingResultItem>
        {
            new(@"D:\Data", "archive.zip", 10485760L, false, DateModified: now, Attributes: 0x20)
        };

        var requestFlags = EverythingIpcConstants.RequestFileName |
                           EverythingIpcConstants.RequestPath |
                           EverythingIpcConstants.RequestSize |
                           EverythingIpcConstants.RequestDateModified |
                           EverythingIpcConstants.RequestAttributes;

        var bytes = EverythingBinaryResponseBuilder.BuildListV2(
            items,
            totalItems: 1,
            offset: 0,
            requestFlags: requestFlags,
            sortType: EverythingIpcConstants.SortSizeDescending,
            isUnicode: true);

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);

        var totItems = reader.ReadUInt32();
        var numItems = reader.ReadUInt32();
        var offset = reader.ReadUInt32();
        var reqFlags = reader.ReadUInt32();
        var sortType = reader.ReadUInt32();

        Assert.AreEqual(1u, totItems);
        Assert.AreEqual(1u, numItems);
        Assert.AreEqual(0u, offset);
        Assert.AreEqual(requestFlags, reqFlags);
        Assert.AreEqual(EverythingIpcConstants.SortSizeDescending, sortType);

        var itemFlag = reader.ReadUInt32();
        var dataOffset = reader.ReadUInt32();
        Assert.AreEqual(0u, itemFlag);

        // Jump to data chunk
        stream.Position = dataOffset;

        // 1. FileName: DWORD len + wchar null-term
        var nameLen = reader.ReadUInt32();
        Assert.AreEqual((uint)"archive.zip".Length, nameLen);
        var name = ReadUnicodeNullTerminated(stream);
        Assert.AreEqual("archive.zip", name);

        // 2. Path: DWORD len + wchar null-term
        var pathLen = reader.ReadUInt32();
        Assert.AreEqual((uint)@"D:\Data".Length, pathLen);
        var path = ReadUnicodeNullTerminated(stream);
        Assert.AreEqual(@"D:\Data", path);

        // 3. Size: Int64
        var size = reader.ReadInt64();
        Assert.AreEqual(10485760L, size);

        // 4. DateModified: FileTime (Int64)
        var dateModFileTime = reader.ReadInt64();
        Assert.AreEqual(now.ToFileTimeUtc(), dateModFileTime);

        // 5. Attributes: DWORD
        var attrs = reader.ReadUInt32();
        Assert.AreEqual(0x20u, attrs);
    }

    [TestMethod]
    public void BuildListV2_WithSizeAndExtension_OrdersChunksPerSdk()
    {
        var items = new List<EverythingResultItem>
        {
            new(@"D:\Data", "archive.zip", 10485760L, false)
        };

        var requestFlags = EverythingIpcConstants.RequestFileName |
                           EverythingIpcConstants.RequestPath |
                           EverythingIpcConstants.RequestSize |
                           EverythingIpcConstants.RequestExtension;

        var bytes = EverythingBinaryResponseBuilder.BuildListV2(
            items,
            totalItems: 1,
            offset: 0,
            requestFlags: requestFlags,
            sortType: EverythingIpcConstants.SortNameAscending,
            isUnicode: true);

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);

        // LIST2 header: totitems, numitems, offset, request_flags, sort_type (5 x uint32)
        stream.Position = 20;
        var itemFlag = reader.ReadUInt32();
        var dataOffset = reader.ReadUInt32();
        Assert.AreEqual(0u, itemFlag);

        stream.Position = dataOffset;

        // 1. FileName: DWORD len + wchar null-term
        var nameLen = reader.ReadUInt32();
        var name = ReadUnicodeNullTerminated(stream);
        Assert.AreEqual("archive.zip", name);

        // 2. Path: DWORD len + wchar null-term
        var pathLen = reader.ReadUInt32();
        var path = ReadUnicodeNullTerminated(stream);
        Assert.AreEqual(@"D:\Data", path);

        // 3. Size: Int64 (SDK order: SIZE before EXTENSION)
        var size = reader.ReadInt64();
        Assert.AreEqual(10485760L, size);

        // 4. Extension: DWORD len + wchar null-term
        var extLen = reader.ReadUInt32();
        Assert.AreEqual((uint)"zip".Length, extLen);
        var ext = ReadUnicodeNullTerminated(stream);
        Assert.AreEqual("zip", ext);
    }

    private static string ReadUnicodeNullTerminated(Stream stream)
    {
        var chars = new List<char>();
        using var reader = new BinaryReader(stream, Encoding.Unicode, leaveOpen: true);
        while (stream.Position + 1 < stream.Length)
        {
            var ch = reader.ReadChar();
            if (ch == '\0') break;
            chars.Add(ch);
        }
        return new string(chars.ToArray());
    }
}
