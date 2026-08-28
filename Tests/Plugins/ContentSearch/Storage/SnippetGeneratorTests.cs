using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Tests.Storage;

[TestClass]
public sealed class SnippetGeneratorTests
{
    [TestMethod]
    public void CreateSnippet_EmptyContent_ReturnsEmpty()
    {
        var snippet = SnippetGenerator.CreateSnippet("", "query");
        Assert.AreEqual(string.Empty, snippet);
    }

    [TestMethod]
    public void CreateSnippet_KeywordMatch_ContainsSurroundingContext()
    {
        var content = "This is a long introductory text before the important keyword that we want to search for in this test.";
        var snippet = SnippetGenerator.CreateSnippet(content, "keyword", maxLength: 50);

        Assert.IsTrue(snippet.Contains("keyword", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void CreateSnippet_NoMatch_ReturnsTruncatedPrefix()
    {
        var content = "Alpha Beta Gamma Delta Epsilon Zeta Eta Theta Iota Kappa Lambda";
        var snippet = SnippetGenerator.CreateSnippet(content, "NotFoundKeyword", maxLength: 20);

        Assert.IsLessThanOrEqualTo(20, snippet.Length);
        Assert.StartsWith(snippet, content);
    }
}
