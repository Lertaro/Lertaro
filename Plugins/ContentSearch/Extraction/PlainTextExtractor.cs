namespace Lertaro.Plugins.ContentSearch.Extraction;

/// <summary>
/// Extracts plain text from document and code files using UTF-8 reading.
/// </summary>
public sealed class PlainTextExtractor : ITextExtractor
{
    public bool CanHandle(string extension) => !string.IsNullOrWhiteSpace(extension);

    public async Task<string?> ExtractTextAsync(string filePath, long maxFileSizeBytes, CancellationToken cancellationToken = default)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists || fileInfo.Length > maxFileSizeBytes || fileInfo.Length == 0)
                return null;

            return await File.ReadAllTextAsync(filePath, cancellationToken);
        }
        catch
        {
            return null;
        }
    }
}
