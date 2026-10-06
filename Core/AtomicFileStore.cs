namespace Lertaro.Core;

/// <summary>
/// Shared durable-write path for the settings and history stores (one write discipline, five
/// call sites: UserSettings, MachineSettings, SearchHistoryStore, KeywordHistoryStore, and the
/// settings data-directory migrator). Extracted for that reuse, not for any line limit; owns no state.
/// </summary>
public static class AtomicFileStore
{
    private const int RetryCount = 5;
    private const int RetryDelayMilliseconds = 50;

    /// <summary>
    /// Writes <paramref name="content"/> by way of a temp file in the destination's own directory and
    /// an atomic <see cref="File.Replace"/>, so a crash mid-write leaves the previous content intact
    /// instead of a truncated file. When <paramref name="backupPath"/> is given, the replaced content
    /// lands there as a read-time fallback for the store's load path. Throws IOException after the
    /// retries are exhausted; callers keep their own catch-and-log where they had one.
    /// </summary>
    public static void Write(string path, string content, string? backupPath = null)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // Unique per write, not per process: concurrent saves and pre-planted links must never share
        // a temporary file. CreateNew also refuses an existing entry instead of following it.
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        var created = false;
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                created = true;
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    if (File.Exists(path))
                        File.Replace(tempPath, path, backupPath);
                    else
                        File.Move(tempPath, path);
                    return;
                }
                catch (IOException) when (attempt < RetryCount) { Thread.Sleep(RetryDelayMilliseconds); }
            }
        }
        finally
        {
            if (created)
                try { File.Delete(tempPath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
