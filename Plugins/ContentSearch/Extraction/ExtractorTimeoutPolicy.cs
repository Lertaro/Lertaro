namespace Lertaro.Plugins.ContentSearch.Extraction;

/// <summary>
/// Computes the per-file extraction timeout for the CPU-bound document extractors.
/// Large documents get proportionally more time than the previous flat 5 seconds so a
/// big PDF is not killed mid-parse, with hard bounds so a pathological file cannot
/// stall the indexing loop.
/// </summary>
public static class ExtractorTimeoutPolicy
{
    // ponytail: 5 s/MB with 5 s..120 s bounds is a user-confirmed heuristic, not a
    // measured model. If huge PDFs still time out in practice, raise the ceiling first.
    private const double SecondsPerMegabyte = 5;
    private const double MinimumSeconds = 5;
    private const double MaximumSeconds = 120;

    internal static TimeSpan ForFileSize(long fileLengthBytes)
    {
        var seconds = fileLengthBytes / (1024d * 1024d) * SecondsPerMegabyte;
        return TimeSpan.FromSeconds(Math.Clamp(seconds, MinimumSeconds, MaximumSeconds));
    }
}
