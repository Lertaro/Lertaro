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
    public void TryParse_RepeatedPaths_PreservesOrderAndRemovesDuplicates()
    {
        var directory = CreateTempDirectory();
        try
        {
            var first = Path.Combine(directory, "first & 100%.txt");
            var second = Path.Combine(directory, "second.txt");
            File.WriteAllText(first, string.Empty);
            File.WriteAllText(second, string.Empty);
            var uri = new Uri($"lertaro://localsend/items?path={Uri.EscapeDataString(first)}&path={Uri.EscapeDataString(second)}&path={Uri.EscapeDataString(first)}");

            var parsed = LocalSendUriParser.TryParse(uri, out var request);

            Assert.IsTrue(parsed);
            Assert.IsNotNull(request);
            Assert.AreEqual(LocalSendUriRequestKind.Files, request.Kind);
            CollectionAssert.AreEqual(new[] { Path.GetFullPath(first), Path.GetFullPath(second) }, request.Files?.ToArray());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void TryParse_TextRoute_DecodesFormEncodedValue()
    {
        var parsed = LocalSendUriParser.TryParse(new Uri("lertaro://localsend/text?value=hello+%E4%B8%96%E7%95%8C"), out var request);

        Assert.IsTrue(parsed);
        Assert.IsNotNull(request);
        Assert.AreEqual(LocalSendUriRequestKind.Text, request.Kind);
        Assert.AreEqual("hello 世界", request.Text);
    }

    [TestMethod]
    public void TryParse_FilesRouteWithUnknownParameter_ReturnsFalse()
    {
        var directory = CreateTempDirectory();
        try
        {
            var uri = new Uri($"lertaro://localsend/items?path={Uri.EscapeDataString(directory)}&text=nope");

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
            new Uri($"lertaro://localsend/items?path={Uri.EscapeDataString(missing)}"), out _));
        Assert.IsFalse(LocalSendUriParser.TryParse(
            new Uri("lertaro://localsend/items?path=relative.txt"), out _));
    }

    [TestMethod]
    public void TryParse_EmptyTextOrUnexpectedFragment_ReturnsFalse()
    {
        Assert.IsFalse(LocalSendUriParser.TryParse(new Uri("lertaro://localsend/text?value="), out _));
        Assert.IsFalse(LocalSendUriParser.TryParse(new Uri("lertaro://localsend#unexpected"), out _));
    }

    [TestMethod]
    public void TryParse_TooManyPathsOrOversizedText_ReturnsFalse()
    {
        var directory = CreateTempDirectory();
        try
        {
            var encodedPath = Uri.EscapeDataString(directory);
            var query = string.Join('&', Enumerable.Repeat($"path={encodedPath}", LocalSendUriParser.MaxPathCount + 1));

            Assert.IsFalse(LocalSendUriParser.TryParse(new Uri($"lertaro://localsend/items?{query}"), out _));
            Assert.IsFalse(LocalSendUriParser.TryParse(
                new Uri($"lertaro://localsend/text?value={new string('a', LocalSendUriParser.MaxTextLength + 1)}"), out _));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lertaro-uri-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
