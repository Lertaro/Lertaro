namespace Lertaro.Plugins.BrowserData.Readers;

// Chrome/Firefox may hold their History/places.sqlite file open while running. A plain file copy still
// succeeds against another process's shared-read handle, so this reads a consistent snapshot without
// needing the browser closed or fighting over the file lock.
internal static class SqliteCopyReader
{
    public static List<BrowserEntry> ReadCopy(string sourcePath, Func<string, List<BrowserEntry>> read)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "lertaro_browserdata_" + Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            File.Copy(sourcePath, tempPath, overwrite: true);
            // WAL sidecar files hold not-yet-checkpointed writes -- copy them too so recently-added
            // history/bookmarks aren't silently missing from the snapshot.
            foreach (var suffix in new[] { "-wal", "-shm" })
            {
                var sidecar = sourcePath + suffix;
                if (File.Exists(sidecar))
                {
                    try { File.Copy(sidecar, tempPath + suffix, overwrite: true); } catch { }
                }
            }

            return read(tempPath);
        }
        catch (Exception ex)
        {
            PluginSdk.Logger.Log($"[BrowserData] Failed to read '{sourcePath}': {ex.Message}", PluginSdk.LogLevel.Warn);
            return new List<BrowserEntry>();
        }
        finally
        {
            TryDelete(tempPath);
            TryDelete(tempPath + "-wal");
            TryDelete(tempPath + "-shm");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}
