using System.IO;
using Lertaro.App.Services.UrlProtocol;

namespace Lertaro.App.Tests.Services.UrlProtocol;

[TestClass]
public sealed class LocalSendUriParserTests
{
    [TestMethod]
    public void TryParse_OpenRoute_ReturnsOpenRequest()
    {
        var parsed = LocalSendUriParser.TryParse(new Uri("lertaro://localsend"), out var request);

        Assert.IsTrue(parsed);
        Assert.IsNotNull(request);
        Assert.AreEqual(LocalSendUriRequestKind.Open, request.Kind);
    }

    [TestMethod]
    public void TryParse_ItemSegments_PreserveOrderAndRemoveDuplicates()
    {
        var directory = CreateTempDirectory();
        try
        {
            var first = Path.Combine(directory, "first & 100%.txt");
            var second = Path.Combine(directory, "folder");
            File.WriteAllText(first, string.Empty);
            Directory.CreateDirectory(second);
            var uri = new Uri($"lertaro://localsend/items/{Encode(first)}/{Encode(second)}/{Encode(first)}");

            var parsed = LocalSendUriParser.TryParse(uri, out var request);

            Assert.IsTrue(parsed);
            Assert.IsNotNull(request);
            Assert.AreEqual(LocalSendUriRequestKind.Items, request.Kind);
            CollectionAssert.AreEqual(new[] { Path.GetFullPath(first), Path.GetFullPath(second) }, request.Files?.ToArray());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void TryParse_TextSegment_DecodesOnceAndKeepsPlusLiteral()
    {
        var parsed = LocalSendUriParser.TryParse(new Uri("lertaro://localsend/text/hello+%E4%B8%96%E7%95%8C%2Fagain"), out var request);

        Assert.IsTrue(parsed);
        Assert.IsNotNull(request);
        Assert.AreEqual(LocalSendUriRequestKind.Text, request.Kind);
        Assert.AreEqual("hello+世界/again", request.Text);
    }

    [TestMethod]
    public void TryParse_QuerySyntax_ReturnsFalse()
    {
        var directory = CreateTempDirectory();
        try
        {
            var uri = new Uri($"lertaro://localsend/items?path={Encode(directory)}");

            Assert.IsFalse(LocalSendUriParser.TryParse(uri, out _));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void TryParse_NonexistentOrRelativePath_ReturnsFalse()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"lertaro-missing-{Guid.NewGuid():N}");

        Assert.IsFalse(LocalSendUriParser.TryParse(
            new Uri($"lertaro://localsend/items/{Encode(missing)}"), out _));
        Assert.IsFalse(LocalSendUriParser.TryParse(
            new Uri("lertaro://localsend/items/relative.txt"), out _));
    }

    [TestMethod]
    public void TryParse_EmptyModeRoutes_ReturnExplicitModes()
    {
        Assert.IsTrue(LocalSendUriParser.TryParse(new Uri("lertaro://localsend/text"), out var textRequest));
        Assert.IsNotNull(textRequest);
        Assert.AreEqual(LocalSendUriRequestKind.Text, textRequest.Kind);
        Assert.IsNull(textRequest.Text);

        Assert.IsTrue(LocalSendUriParser.TryParse(new Uri("lertaro://localsend/items/"), out var itemsRequest));
        Assert.IsNotNull(itemsRequest);
        Assert.AreEqual(LocalSendUriRequestKind.Items, itemsRequest.Kind);
        Assert.IsNotNull(itemsRequest.Files);
        Assert.IsEmpty(itemsRequest.Files);
    }

    [TestMethod]
    public void TryParse_FragmentOrUnencodedTextSlash_ReturnsFalse()
    {
        Assert.IsFalse(LocalSendUriParser.TryParse(new Uri("lertaro://localsend#unexpected"), out _));
        Assert.IsFalse(LocalSendUriParser.TryParse(new Uri("lertaro://localsend/text/one/two"), out _));
    }

    [TestMethod]
    public void TryParse_TooManyPathsOrOversizedText_ReturnsFalse()
    {
        var directory = CreateTempDirectory();
        try
        {
            var encodedPath = Encode(directory);
            var segments = string.Join('/', Enumerable.Repeat(encodedPath, LocalSendUriParser.MaxItemCount + 1));

            Assert.IsFalse(LocalSendUriParser.TryParse(new Uri($"lertaro://localsend/items/{segments}"), out _));
            Assert.IsFalse(LocalSendUriParser.TryParse(
                new Uri($"lertaro://localsend/text/{new string('a', LocalSendUriParser.MaxTextLength + 1)}"), out _));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string Encode(string value) => Uri.EscapeDataString(value);

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lertaro-uri-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
