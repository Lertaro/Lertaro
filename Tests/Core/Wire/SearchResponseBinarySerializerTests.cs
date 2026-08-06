using Lertaro.Core.Wire;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.Core.Tests.Wire;

[TestClass]
public sealed class SearchResponseBinarySerializerTests
{
    private static SearchResult MakeResult(string name, string path, bool isDir = false, string drive = "C", FileAttributes attributes = FileAttributes.Normal) => new()
    {
        Name = name,
        Path = path,
        IsDir = isDir,
        Drive = drive,
        Attributes = attributes,
        Metadata = new FileMetadata(
            1024,
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToLocalTime(),
            new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc).ToLocalTime(),
            new DateTime(2024, 6, 20, 8, 0, 0, DateTimeKind.Utc).ToLocalTime())
    };

    [TestMethod]
    public async Task ReadAsync_HeaderThenEnd_InvokesCallbackZeroTimes()
    {
        using var stream = new MemoryStream();
        await SearchResponseBinarySerializer.WriteHeaderAsync(stream);
        await SearchResponseBinarySerializer.WriteEndAsync(stream);
        stream.Position = 0;

        var results = new List<SearchResult>();
        await SearchResponseBinarySerializer.ReadAsync(stream, results.Add);

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public async Task ReadAsync_SingleFileResult_RoundTripsNamePathAndFlags()
    {
        using var stream = new MemoryStream();
        await SearchResponseBinarySerializer.WriteHeaderAsync(stream);
        await SearchResponseBinarySerializer.WriteFileResultAsync(stream, MakeResult("readme.txt", @"c:\readme.txt"));
        await SearchResponseBinarySerializer.WriteEndAsync(stream);
        stream.Position = 0;

        var results = new List<SearchResult>();
        await SearchResponseBinarySerializer.ReadAsync(stream, results.Add);

        Assert.HasCount(1, results);
        Assert.AreEqual("readme.txt", results[0].Name);
        Assert.AreEqual(@"c:\readme.txt", results[0].Path);
        Assert.IsFalse(results[0].IsDir);
        Assert.AreEqual("C", results[0].Drive);
    }

    [TestMethod]
    public async Task ReadAsync_SingleResult_RoundTripsMetadataFields()
    {
        using var stream = new MemoryStream();
        await SearchResponseBinarySerializer.WriteHeaderAsync(stream);
        await SearchResponseBinarySerializer.WriteFileResultAsync(stream, MakeResult("readme.txt", @"c:\readme.txt"));
        await SearchResponseBinarySerializer.WriteEndAsync(stream);
        stream.Position = 0;

        var results = new List<SearchResult>();
        await SearchResponseBinarySerializer.ReadAsync(stream, results.Add);

        var metadata = results[0].Metadata;
        Assert.AreEqual(1024, metadata.Size);
        Assert.AreEqual(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), metadata.Created.ToUniversalTime());
        Assert.AreEqual(new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc), metadata.Modified.ToUniversalTime());
        Assert.AreEqual(new DateTime(2024, 6, 20, 8, 0, 0, DateTimeKind.Utc), metadata.Accessed.ToUniversalTime());
    }

    [TestMethod]
    public async Task ReadAsync_DirectoryResult_PreservesIsDirFlag()
    {
        using var stream = new MemoryStream();
        await SearchResponseBinarySerializer.WriteHeaderAsync(stream);
        await SearchResponseBinarySerializer.WriteFileResultAsync(stream, MakeResult("Projects", @"c:\Projects", isDir: true));
        await SearchResponseBinarySerializer.WriteEndAsync(stream);
        stream.Position = 0;

        var results = new List<SearchResult>();
        await SearchResponseBinarySerializer.ReadAsync(stream, results.Add);

        Assert.IsTrue(results[0].IsDir);
    }

    [TestMethod]
    public async Task ReadAsync_HiddenSystemAttributes_RoundTrips()
    {
        // Regression test: this field was silently dropped by the wire format entirely, so
        // FileSystemItemFilter.IsHiddenOrSystem (client-side) always saw the zero default and never
        // filtered NTFS metadata files like $MFT despite the filter itself being unconditionally wired in.
        using var stream = new MemoryStream();
        await SearchResponseBinarySerializer.WriteHeaderAsync(stream);
        await SearchResponseBinarySerializer.WriteFileResultAsync(stream, MakeResult("$MFT", @"c:\$MFT", attributes: FileAttributes.Hidden | FileAttributes.System));
        await SearchResponseBinarySerializer.WriteEndAsync(stream);
        stream.Position = 0;

        var results = new List<SearchResult>();
        await SearchResponseBinarySerializer.ReadAsync(stream, results.Add);

        Assert.AreEqual(FileAttributes.Hidden | FileAttributes.System, results[0].Attributes);
    }

    [TestMethod]
    public async Task ReadAsync_MultipleResults_PreservesWriteOrder()
    {
        using var stream = new MemoryStream();
        await SearchResponseBinarySerializer.WriteHeaderAsync(stream);
        await SearchResponseBinarySerializer.WriteFileResultAsync(stream, MakeResult("a.txt", @"c:\a.txt"));
        await SearchResponseBinarySerializer.WriteFileResultAsync(stream, MakeResult("b.txt", @"c:\b.txt"));
        await SearchResponseBinarySerializer.WriteFileResultAsync(stream, MakeResult("c.txt", @"c:\c.txt"));
        await SearchResponseBinarySerializer.WriteEndAsync(stream);
        stream.Position = 0;

        var results = new List<SearchResult>();
        await SearchResponseBinarySerializer.ReadAsync(stream, results.Add);

        CollectionAssert.AreEqual(new[] { "a.txt", "b.txt", "c.txt" }, results.ConvertAll(r => r.Name));
    }

    [TestMethod]
    public async Task ReadAsync_UnicodeNameAndPath_RoundTripsExactly()
    {
        using var stream = new MemoryStream();
        await SearchResponseBinarySerializer.WriteHeaderAsync(stream);
        await SearchResponseBinarySerializer.WriteFileResultAsync(stream, MakeResult("文件搜索.txt", @"c:\文件搜索.txt"));
        await SearchResponseBinarySerializer.WriteEndAsync(stream);
        stream.Position = 0;

        var results = new List<SearchResult>();
        await SearchResponseBinarySerializer.ReadAsync(stream, results.Add);

        Assert.AreEqual("文件搜索.txt", results[0].Name);
        Assert.AreEqual(@"c:\文件搜索.txt", results[0].Path);
    }

    // The whole point of the frame: a directory the index can't answer for and an empty directory both
    // produce zero results, and only the first is worth falling back to a real filesystem walk over.
    [TestMethod]
    public async Task ReadAsync_NotIndexedFrame_SignalsTheCallerWithoutProducingResults()
    {
        using var stream = new MemoryStream();
        await SearchResponseBinarySerializer.WriteHeaderAsync(stream);
        await SearchResponseBinarySerializer.WriteNotIndexedAsync(stream);
        await SearchResponseBinarySerializer.WriteEndAsync(stream);
        stream.Position = 0;

        var results = new List<SearchResult>();
        var notIndexed = false;
        await SearchResponseBinarySerializer.ReadAsync(stream, results.Add, onNotIndexed: () => notIndexed = true);

        Assert.IsTrue(notIndexed);
        Assert.IsEmpty(results);
    }

    [TestMethod]
    public async Task ReadAsync_WithoutTheNotIndexedFrame_LeavesTheSignalUnset()
    {
        using var stream = new MemoryStream();
        await SearchResponseBinarySerializer.WriteHeaderAsync(stream);
        await SearchResponseBinarySerializer.WriteFileResultAsync(stream, MakeResult("a.txt", @"c:\a.txt"));
        await SearchResponseBinarySerializer.WriteEndAsync(stream);
        stream.Position = 0;

        var notIndexed = false;
        await SearchResponseBinarySerializer.ReadAsync(stream, _ => { }, onNotIndexed: () => notIndexed = true);

        Assert.IsFalse(notIndexed);
    }

    // A reader that doesn't care (every search caller) must not trip over the frame.
    [TestMethod]
    public async Task ReadAsync_NotIndexedFrameWithNoCallback_IsSkipped()
    {
        using var stream = new MemoryStream();
        await SearchResponseBinarySerializer.WriteHeaderAsync(stream);
        await SearchResponseBinarySerializer.WriteNotIndexedAsync(stream);
        await SearchResponseBinarySerializer.WriteFileResultAsync(stream, MakeResult("a.txt", @"c:\a.txt"));
        await SearchResponseBinarySerializer.WriteEndAsync(stream);
        stream.Position = 0;

        var results = new List<SearchResult>();
        await SearchResponseBinarySerializer.ReadAsync(stream, results.Add);

        Assert.HasCount(1, results);
    }

    [TestMethod]
    public async Task ReadAsync_HeaderWithWrongVersion_ThrowsInvalidDataException()
    {
        using var stream = new MemoryStream();
        // Write a valid file-result frame first (any frame with this serializer's own magic), which
        // ReadAsync will misinterpret as a header since we craft the header bytes manually below with
        // a bad version -- simplest way to hit the version check without touching internals.
        var badHeader = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(badHeader, 0x53524C53); // magic
        badHeader[4] = 255; // HeaderFrame
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(badHeader.AsSpan(5), 4); // length
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(badHeader.AsSpan(9), 999); // bad version
        await stream.WriteAsync(badHeader);
        stream.Position = 0;

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => SearchResponseBinarySerializer.ReadAsync(stream, _ => { }));
    }

    [TestMethod]
    public async Task ReadAsync_CorruptedMagic_ThrowsInvalidDataException()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => SearchResponseBinarySerializer.ReadAsync(stream, _ => { }));
    }

    // SearchPipeClient reads the response through a BufferedStream, because reading a result's magic,
    // frame type, length and payload as four separate calls straight onto a pipe is four syscalls per
    // result and cost twenty seconds of a whole-drive search. A buffer boundary can then fall anywhere
    // -- mid-frame, mid-payload, mid-length -- so what these pin is that a frame never has to be
    // contained in one read for the parse to work. The buffer is deliberately tiny so a boundary lands
    // inside almost every frame rather than once in a while.
    [TestMethod]
    public async Task ReadAsync_ThroughATinyBuffer_StillReadsEveryFrameWhole()
    {
        using var stream = new MemoryStream();
        await SearchResponseBinarySerializer.WriteHeaderAsync(stream);
        var written = new List<SearchResult>();
        for (var i = 0; i < 200; i++)
        {
            // Varying name lengths so frame sizes differ and the boundaries land in different places
            // from frame to frame rather than lining up with a fixed stride.
            var name = new string('n', 1 + i % 37) + i + ".txt";
            var result = MakeResult(name, @"c:\deep\folder\" + new string('d', i % 23) + @"\" + name);
            written.Add(result);
            await SearchResponseBinarySerializer.WriteFileResultAsync(stream, result);
        }
        await SearchResponseBinarySerializer.WriteEndAsync(stream);
        stream.Position = 0;

        var read = new List<SearchResult>();
        await using var buffered = new BufferedStream(stream, 7);
        await SearchResponseBinarySerializer.ReadAsync(buffered, read.Add);

        Assert.HasCount(written.Count, read);
        for (var i = 0; i < written.Count; i++)
        {
            Assert.AreEqual(written[i].Name, read[i].Name, $"name at {i}");
            Assert.AreEqual(written[i].Path, read[i].Path, $"path at {i}");
            Assert.AreEqual(written[i].Metadata.Size, read[i].Metadata.Size, $"size at {i}");
            Assert.AreEqual(written[i].Attributes, read[i].Attributes, $"attributes at {i}");
        }
    }

    [TestMethod]
    public async Task ReadAsync_ThroughABufferSmallerThanOnePayload_StillRoundTrips()
    {
        // A single path longer than the whole read buffer -- the payload cannot be satisfied by one
        // underlying read no matter where the boundary falls.
        var longPath = @"c:\" + string.Join('\\', Enumerable.Range(0, 60).Select(i => $"segment{i}")) + @"\file.txt";

        using var stream = new MemoryStream();
        await SearchResponseBinarySerializer.WriteHeaderAsync(stream);
        await SearchResponseBinarySerializer.WriteFileResultAsync(stream, MakeResult("file.txt", longPath));
        await SearchResponseBinarySerializer.WriteEndAsync(stream);
        stream.Position = 0;

        var read = new List<SearchResult>();
        await using var buffered = new BufferedStream(stream, 16);
        await SearchResponseBinarySerializer.ReadAsync(buffered, read.Add);

        Assert.HasCount(1, read);
        Assert.AreEqual(longPath, read[0].Path);
    }

    [TestMethod]
    public async Task ReadAsync_ThroughABuffer_MultiByteCharactersSurviveABoundary()
    {
        // UTF-8 is decoded from the assembled payload rather than as bytes arrive, so a boundary
        // splitting a multi-byte character must not corrupt it.
        var name = string.Concat(Enumerable.Repeat("文件搜索", 20)) + ".txt";

        using var stream = new MemoryStream();
        await SearchResponseBinarySerializer.WriteHeaderAsync(stream);
        await SearchResponseBinarySerializer.WriteFileResultAsync(stream, MakeResult(name, @"c:\" + name));
        await SearchResponseBinarySerializer.WriteEndAsync(stream);
        stream.Position = 0;

        var read = new List<SearchResult>();
        await using var buffered = new BufferedStream(stream, 5);
        await SearchResponseBinarySerializer.ReadAsync(buffered, read.Add);

        Assert.HasCount(1, read);
        Assert.AreEqual(name, read[0].Name);
    }
}
