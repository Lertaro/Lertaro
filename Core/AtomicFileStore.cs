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

        // Process-scoped temp name: two processes may write the same destination (the App and the
        // service both touch machine-settings.json), and a shared temp path makes them contend on the
        // very file they are each trying to swap in.
        var tempPath = $"{path}.{Environment.ProcessId}.tmp";
        // ponytail: a process death between writing the temp file and the replace leaves a lingering
        // .tmp file behind; harmless, and the next Write from the same process recreates it via
        // FileMode.Create. A failure that exhausts the retries deletes its own temp below.
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
                Thread.Sleep(RetryDelayMilliseconds);
            }
            catch (IOException)
            {
                // Best effort: a lingering temp file is recreated by the next Write anyway.
                try { File.Delete(tempPath); } catch (IOException) { }
                throw;
            }
        }
    }
}
