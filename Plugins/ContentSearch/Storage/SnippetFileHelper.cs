using Lertaro.Plugins.ContentSearch.Extraction;

namespace Lertaro.Plugins.ContentSearch.Storage;

/// <summary>
/// Reads on-demand text segments from source files to generate search snippets in Contentless mode.
/// </summary>
public static class SnippetFileHelper
{
    public static string ReadSnippetContext(string filePath, int offset, int length, long maxFileSizeBytes = 10 * 1024 * 1024)
    {
        try
        {
            if (!File.Exists(filePath)) return string.Empty;

            var extracted = TextExtractorRegistry.Instance.ExtractTextAsync(filePath, maxFileSizeBytes).GetAwaiter().GetResult();
            if (extracted != null && offset < extracted.Length)
            {
                var len = Math.Min(length, extracted.Length - offset);
                return extracted.Substring(offset, len);
            }
        }
        catch { }

        return string.Empty;
    }
}
