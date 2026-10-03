namespace Lertaro.Plugins.ContentSearch.Indexing;

/// <summary>
/// Decides whether the content index has reached its configured size cap and reports the
/// resulting pause exactly once per episode.
/// Split out to keep IndexBatchProcessor under the repository's per-file line limit; this class
/// owns nothing but that one report flag.
/// </summary>
internal sealed class IndexCapPauseMonitor
{
    private bool _pauseReported;

    /// <summary>
    /// True while the index is at or past the cap, which pauses extraction and writes until the
    /// user raises the cap or clears the index. The pause is reported the first time it is seen
    /// and stays silent for every later batch: a scan that keeps re-enqueueing the skipped files
    /// would otherwise log the identical warning once per batch for as long as the cap holds.
    /// Once the index is back under the cap the episode ends, so reaching it again reports again.
    /// </summary>
    public bool IsPaused(long indexBytes, long maxIndexSizeBytes)
    {
        if (indexBytes <= maxIndexSizeBytes)
        {
            _pauseReported = false;
            return false;
        }

        if (!_pauseReported)
        {
            _pauseReported = true;
            PluginSdk.Logger.Log(
                $"[ContentSearch] Index size cap reached ({indexBytes / (1024 * 1024)} MB of {maxIndexSizeBytes / (1024 * 1024)} MB), indexing is paused until the cap is raised or the index cleared",
                PluginSdk.LogLevel.Warn);
        }

        return true;
    }
}
