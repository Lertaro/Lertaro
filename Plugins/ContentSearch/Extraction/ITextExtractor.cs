namespace Lertaro.Plugins.ContentSearch.Extraction;

/// <summary>
/// Defines the contract for extracting text content from supported document file types.
/// </summary>
public interface ITextExtractor
{
    /// <summary>
    /// Checks whether this extractor supports the given file extension (e.g., ".txt", ".docx", ".pdf").
    /// </summary>
    bool CanHandle(string extension);

    /// <summary>
    /// Extracts text content asynchronously from the specified file path.
    /// </summary>
    /// <param name="filePath">Absolute path to the target document.</param>
    /// <param name="maxFileSizeBytes">Maximum allowed file size in bytes; larger files should be rejected.</param>
    /// <param name="cancellationToken">Cancellation token for aborting extraction.</param>
    /// <returns>Extracted plain text or null if extraction failed or was unsupported.</returns>
    Task<string?> ExtractTextAsync(string filePath, long maxFileSizeBytes, CancellationToken cancellationToken = default);
}
