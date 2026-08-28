namespace Lertaro.Plugins.ContentSearch.Extraction;

/// <summary>
/// Dispatches file text extraction to the appropriate registered extractor based on file extension.
/// </summary>
public sealed class TextExtractorRegistry
{
    public static TextExtractorRegistry Instance { get; } = new();

    private readonly List<ITextExtractor> _extractors;

    public TextExtractorRegistry() => _extractors = new List<ITextExtractor>
    {
        new PlainTextExtractor(),
        new DocxExtractor(),
        new PdfExtractor()
    };

    public bool IsSupportedExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return false;

        foreach (var extractor in _extractors)
        {
            if (extractor.CanHandle(extension))
                return true;
        }

        return false;
    }

    public async Task<string?> ExtractTextAsync(string filePath, long maxFileSizeBytes, CancellationToken cancellationToken = default)
    {
        var ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext))
            return null;

        foreach (var extractor in _extractors)
        {
            if (extractor.CanHandle(ext))
            {
                return await extractor.ExtractTextAsync(filePath, maxFileSizeBytes, cancellationToken);
            }
        }

        return null;
    }
}
