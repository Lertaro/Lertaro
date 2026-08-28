namespace Lertaro.Core;

/// <summary>
/// Shared durable-write path for the settings and history stores (one write discipline, five
/// call sites: UserSettings, MachineSettings, SearchHistoryStore, KeywordHistoryStore, and the
/// settings data-directory migrator). Extracted for that reuse, not for any line limit; owns no state.
/// </summary>
internal static class AtomicFileStore
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

        var tempPath = $"{path}.tmp";
        // ponytail: a process death between writing the temp file and the replace leaves a lingering
        // .tmp file behind; harmless, and the next Write recreates it via FileMode.Create anyway.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                // Write and fully close the temp file before the swap: File.Replace and File.Move
                // cannot move a file this process still holds open.
                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(content);
                }

                if (File.Exists(path))
                    File.Replace(tempPath, path, backupPath);
                else
                    File.Move(tempPath, path);
                return;
            }
            catch (IOException) when (attempt < RetryCount)
            {
                Task.Delay(RetryDelayMilliseconds).Wait();
            }
        }
    }
}
