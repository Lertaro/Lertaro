using Lertaro.Core.Wire;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.Core.Tests.Wire;

[TestClass]
public sealed class SearchResultWithHighlightBinarySerializerTests
{
    private static SearchResult MakeResult(string name, string path, FileAttributes attributes = FileAttributes.Normal) => new()
    {
        Name = name,
        Path = path,
        Drive = "C",
        Attributes = attributes,
        Metadata = new FileMetadata(512, DateTime.UtcNow.ToLocalTime(), DateTime.UtcNow.ToLocalTime(), DateTime.UtcNow.ToLocalTime())
    };

    private static async Task<(SearchResult Result, int[] Ranges)> RoundTripSingleAsync(SearchResult result, IReadOnlyList<int> ranges)
    {
        using var stream = new MemoryStream();
        await SearchResultWithHighlightBinarySerializer.WriteHeaderAsync(stream);
        await SearchResultWithHighlightBinarySerializer.WriteFileResultAsync(stream, result, ranges);
        await SearchResultWithHighlightBinarySerializer.WriteEndAsync(stream);
        stream.Position = 0;

        SearchResult? captured = null;
        int[]? capturedRanges = null;
        await SearchResultWithHighlightBinarySerializer.ReadAsync(stream, (r, ranges) =>
        {
            captured = r;
            capturedRanges = ranges;
        });

        return (captured!, capturedRanges!);
    }

    [TestMethod]
    public async Task RoundTrip_WithHighlightRanges_PreservesResultAndRanges()
    {
        var (result, ranges) = await RoundTripSingleAsync(MakeResult("readme.txt", @"c:\readme.txt"), new[] { 0, 4, 7, 3 });

        Assert.AreEqual("readme.txt", result.Name);
        Assert.AreEqual(@"c:\readme.txt", result.Path);
        CollectionAssert.AreEqual(new[] { 0, 4, 7, 3 }, ranges);
    }

    [TestMethod]
    public async Task RoundTrip_NoHighlightRanges_ReturnsEmptyRangesArray()
    {
        var (_, ranges) = await RoundTripSingleAsync(MakeResult("readme.txt", @"c:\readme.txt"), Array.Empty<int>());

        Assert.IsEmpty(ranges);
    }

    [TestMethod]
    public async Task RoundTrip_UnicodeName_PreservesExactText()
    {
        var (result, _) = await RoundTripSingleAsync(MakeResult("文件搜索.txt", @"c:\文件搜索.txt"), Array.Empty<int>());

        Assert.AreEqual("文件搜索.txt", result.Name);
    }

    [TestMethod]
    public async Task RoundTrip_HiddenSystemAttributes_RoundTrips()
    {
        var (result, _) = await RoundTripSingleAsync(
            MakeResult("$MFT", @"c:\$MFT", FileAttributes.Hidden | FileAttributes.System), Array.Empty<int>());

        Assert.AreEqual(FileAttributes.Hidden | FileAttributes.System, result.Attributes);
    }

    [TestMethod]
    public async Task ReadAsync_MismatchedMagic_ThrowsInvalidDataException()
    {
        using var stream = new MemoryStream(new byte[] { 9, 9, 9, 9, 0, 0, 0, 0, 0 });

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => SearchResultWithHighlightBinarySerializer.ReadAsync(stream, (_, _) => { }));
    }

    // AppSearchPipeClient reads this format through a BufferedStream and AppSearchPipeService writes it
    // through one, because a result per syscall in each direction is what the measurement on the GUI's
    // own pipe put at 30us a result against 2.1. A buffer boundary can then land anywhere in a frame, so
    // what these pin is that no frame has to arrive in a single read. Buffer sizes are deliberately tiny
    // so a boundary falls inside nearly every frame rather than occasionally.
    [TestMethod]
    public async Task ReadAsync_BufferedBothWays_RoundTripsEveryResultAndItsRanges()
    {
        using var stream = new MemoryStream();
        await using var writeBuffer = new BufferedStream(stream, 11);
        await SearchResultWithHighlightBinarySerializer.WriteHeaderAsync(writeBuffer);
        for (var i = 0; i < 150; i++)
        {
            var name = new string('n', 1 + i % 29) + i + ".txt";
            await SearchResultWithHighlightBinarySerializer.WriteFileResultAsync(
                writeBuffer, MakeResult(name, @"c:\folder" + new string('d', i % 17) + @"\" + name), new[] { 0, 1 + i % 5 });
        }
        await SearchResultWithHighlightBinarySerializer.WriteEndAsync(writeBuffer);
        await writeBuffer.FlushAsync();

        stream.Position = 0;
        var read = new List<(SearchResult Result, int[] Ranges)>();
        await using var readBuffer = new BufferedStream(stream, 7);
        await SearchResultWithHighlightBinarySerializer.ReadAsync(readBuffer, (r, ranges) => read.Add((r, ranges)));

        Assert.HasCount(150, read);
        for (var i = 0; i < read.Count; i++)
        {
            var expectedName = new string('n', 1 + i % 29) + i + ".txt";
            Assert.AreEqual(expectedName, read[i].Result.Name, $"name at {i}");
            CollectionAssert.AreEqual(new[] { 0, 1 + i % 5 }, read[i].Ranges, $"ranges at {i}");
        }
    }

    [TestMethod]
    public async Task ReadAsync_BufferSmallerThanOnePayload_StillRoundTrips()
    {
        var longPath = @"c:\" + string.Join('\\', Enumerable.Range(0, 60).Select(i => $"segment{i}")) + @"\file.txt";

        using var stream = new MemoryStream();
        await SearchResultWithHighlightBinarySerializer.WriteHeaderAsync(stream);
        await SearchResultWithHighlightBinarySerializer.WriteFileResultAsync(stream, MakeResult("file.txt", longPath), new[] { 0, 4 });
        await SearchResultWithHighlightBinarySerializer.WriteEndAsync(stream);
        stream.Position = 0;

        var read = new List<(SearchResult Result, int[] Ranges)>();
        await using var readBuffer = new BufferedStream(stream, 16);
        await SearchResultWithHighlightBinarySerializer.ReadAsync(readBuffer, (r, ranges) => read.Add((r, ranges)));

        Assert.HasCount(1, read);
        Assert.AreEqual(longPath, read[0].Result.Path);
        CollectionAssert.AreEqual(new[] { 0, 4 }, read[0].Ranges);
    }

    [TestMethod]
    public async Task ReadAsync_Buffered_MultiByteCharactersSurviveABoundary()
    {
        var name = string.Concat(Enumerable.Repeat("文件搜索", 20)) + ".txt";

        using var stream = new MemoryStream();
        await SearchResultWithHighlightBinarySerializer.WriteHeaderAsync(stream);
        await SearchResultWithHighlightBinarySerializer.WriteFileResultAsync(stream, MakeResult(name, @"c:\" + name), new[] { 0, 4 });
        await SearchResultWithHighlightBinarySerializer.WriteEndAsync(stream);
        stream.Position = 0;

        var read = new List<(SearchResult Result, int[] Ranges)>();
        await using var readBuffer = new BufferedStream(stream, 5);
        await SearchResultWithHighlightBinarySerializer.ReadAsync(readBuffer, (r, ranges) => read.Add((r, ranges)));

        Assert.HasCount(1, read);
        Assert.AreEqual(name, read[0].Result.Name);
    }

    [TestMethod]
    public void FlattenMask_NullMask_ReturnsEmptyArray()
    {
        var ranges = SearchResultWithHighlightBinarySerializer.FlattenMask(null);

        Assert.IsEmpty(ranges);
    }

    [TestMethod]
    public void FlattenMask_AllFalse_ReturnsEmptyArray()
    {
        var ranges = SearchResultWithHighlightBinarySerializer.FlattenMask(new[] { false, false, false });

        Assert.IsEmpty(ranges);
    }

    [TestMethod]
    public void FlattenMask_SingleContiguousRun_ReturnsOneStartLengthPair()
    {
        // Indices 1,2,3 are true -> one run starting at 1, length 3.
        var ranges = SearchResultWithHighlightBinarySerializer.FlattenMask(new[] { false, true, true, true, false });

        CollectionAssert.AreEqual(new[] { 1, 3 }, ranges);
    }

    [TestMethod]
    public void FlattenMask_MultipleDisjointRuns_ReturnsPairPerRun()
    {
        // true at [0], false at [1], true at [2,3] -> two runs: (0,1) and (2,2).
        var ranges = SearchResultWithHighlightBinarySerializer.FlattenMask(new[] { true, false, true, true });

        CollectionAssert.AreEqual(new[] { 0, 1, 2, 2 }, ranges);
    }

    [TestMethod]
    public void FlattenMask_EntireMaskTrue_ReturnsOneRunCoveringWholeMask()
    {
        var ranges = SearchResultWithHighlightBinarySerializer.FlattenMask(new[] { true, true, true });

        CollectionAssert.AreEqual(new[] { 0, 3 }, ranges);
    }
}
