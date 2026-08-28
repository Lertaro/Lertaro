using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Tests.Storage;

[TestClass]
public sealed class SnippetFileHelperTests
{
    [TestMethod]
    public void CreateFileSnippet_NonExistentFile_ReturnsEmpty()
    {
        var result = SnippetFileHelper.CreateFileSnippet(@"C:\NonExistent\dummy_12345.txt", "query");
        Assert.AreEqual(string.Empty, result);
    }

    [TestMethod]
    public void CreateFileSnippet_PlainTextFile_ExtractsCleanSnippet()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"snippet_test_{Guid.NewGuid():N}.txt");
        try
        {
            var text = "Hello world! This is a test file for on-demand snippet reading.";
            File.WriteAllText(tempFile, text);

            var snippet = SnippetFileHelper.CreateFileSnippet(tempFile, "test file");
            Assert.Contains("test file", snippet, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }
}
