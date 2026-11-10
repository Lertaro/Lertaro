using Lertaro.Core.SearchIndex;
using Lertaro.Core.Wire;

namespace Lertaro.Core.Tests.Wire;

[TestClass]
public sealed class SearchRegexFailureTests
{
    [TestMethod]
    public async Task HighlightStream_InvalidTokenPreservesFeedback()
    {
        using var stream = new MemoryStream();
        const string message = "无效标记：<s>garbage";
        await SearchResultWithHighlightBinarySerializer.WriteHeaderAsync(stream);
        await SearchResultWithHighlightBinarySerializer.WriteQueryErrorAsync(stream, message);
        await SearchResultWithHighlightBinarySerializer.WriteEndAsync(stream);
        stream.Position = 0;
        var error = await Assert.ThrowsExactlyAsync<FormatException>(() => SearchResultWithHighlightBinarySerializer.ReadAsync(stream, (_, _) => { }));
        Assert.AreEqual(message, error.Message);
    }
    [TestMethod]
    public async Task ServiceStream_TimeoutCannotBeReadAsSuccessfulEnd()
    {
        using var stream = new MemoryStream();
        await SearchResponseBinarySerializer.WriteHeaderAsync(stream);
        await SearchResponseBinarySerializer.WriteFileResultAsync(stream, new() { Name = "partial", Path = @"Z:\partial" });
        await SearchResponseBinarySerializer.WriteRegexTimeoutAsync(stream);
        await SearchResponseBinarySerializer.WriteEndAsync(stream);
        stream.Position = 0;
        var results = new List<SearchResult>();
        await Assert.ThrowsExactlyAsync<SearchRegexTimeoutException>(() => SearchResponseBinarySerializer.ReadAsync(stream, results.Add));
        Assert.HasCount(1, results);
    }

    [TestMethod]
    public async Task HighlightStream_TimeoutCannotBeReadAsSuccessfulEnd()
    {
        using var stream = new MemoryStream();
        await SearchResultWithHighlightBinarySerializer.WriteHeaderAsync(stream);
        await SearchResultWithHighlightBinarySerializer.WriteRegexTimeoutAsync(stream);
        await SearchResultWithHighlightBinarySerializer.WriteEndAsync(stream);
        stream.Position = 0;
        await Assert.ThrowsExactlyAsync<SearchRegexTimeoutException>(() => SearchResultWithHighlightBinarySerializer.ReadAsync(stream, (_, _) => { }));
    }
}
