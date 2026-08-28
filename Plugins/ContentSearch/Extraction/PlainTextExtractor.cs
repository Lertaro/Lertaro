using System.Text;

namespace Lertaro.Plugins.ContentSearch.Extraction;

/// <summary>
/// Extracts plain text from source code, markdown, logs, and configuration files.
/// </summary>
public sealed class PlainTextExtractor : ITextExtractor
{
    private static readonly HashSet<string> SupportedExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".log", ".csv", ".tsv",
        ".json", ".jsonc", ".xml", ".yaml", ".yml", ".ini", ".toml", ".conf", ".config",
        ".cs", ".cpp", ".c", ".h", ".hpp", ".rs", ".go", ".java", ".kt", ".swift",
        ".py", ".js", ".jsx", ".ts", ".tsx", ".html", ".htm", ".css", ".scss", ".sass", ".less",
        ".sql", ".sh", ".bash", ".bat", ".cmd", ".ps1", ".psm1"
    };

    public bool CanHandle(string extension) =>
        SupportedExts.Contains(extension);

    public async Task<string?> ExtractTextAsync(string filePath, long maxFileSizeBytes, CancellationToken cancellationToken = default)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists || fileInfo.Length > maxFileSizeBytes || fileInfo.Length == 0)
            return null;

        try
        {
            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                useAsync: true);

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch
        {
            return null;
        }
    }
}
