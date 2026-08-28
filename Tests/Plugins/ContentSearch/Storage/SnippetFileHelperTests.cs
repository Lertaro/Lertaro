using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Tests.Storage;

[TestClass]
public sealed class SnippetFileHelperTests
{
    [TestMethod]
    public void ReadSnippetContext_NonExistentFile_ReturnsEmpty()
    {
        var result = SnippetFileHelper.ReadSnippetContext(@"C:\NonExistent\dummy_12345.txt", 0, 100);
        Assert.AreEqual(string.Empty, result);
    }

    [TestMethod]
    public void ReadSnippetContext_PlainTextFile_ReadsExactSegment()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"snippet_test_{Guid.NewGuid():N}.txt");
        try
        {
            var text = "Hello world! This is a test file for on-demand snippet reading.";
            File.WriteAllText(tempFile, text);

            var segment = SnippetFileHelper.ReadSnippetContext(tempFile, 13, 20);
            Assert.AreEqual("This is a test file ", segment);
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
