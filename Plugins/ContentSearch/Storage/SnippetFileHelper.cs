using Lertaro.Plugins.ContentSearch.Extraction;

namespace Lertaro.Plugins.ContentSearch.Storage;

/// <summary>
/// Reads on-demand text from source files to generate search snippets in Contentless mode.
/// </summary>
public static class SnippetFileHelper
{
    public static string CreateFileSnippet(string filePath, string query, long maxFileSizeBytes = 10 * 1024 * 1024)
    {
        try
        {
            if (!File.Exists(filePath)) return string.Empty;

            var extracted = TextExtractorRegistry.Instance.ExtractTextAsync(filePath, maxFileSizeBytes).GetAwaiter().GetResult();
            if (!string.IsNullOrEmpty(extracted))
            {
                return SnippetGenerator.CreateSnippet(extracted, query);
            }
        }
        catch { }

        return string.Empty;
    }
}
