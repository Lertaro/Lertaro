namespace Lertaro.Core;

internal static class FileRecordStoreReplaceHelper
{
    public static void ReplaceWithRetry(string tempPath, string finalPath, Action<string> tryDelete)
    {
        const int maxAttempts = 5;
        var backupPath = finalPath + ".bak";
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (File.Exists(finalPath))
                {
                    File.Replace(tempPath, finalPath, backupPath, ignoreMetadataErrors: true);
                    tryDelete(backupPath);
                }
                else
                {
                    File.Move(tempPath, finalPath, overwrite: true);
                }
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(50 * attempt);
            }
        }

        try
        {
            if (File.Exists(finalPath))
            {
                File.Replace(tempPath, finalPath, backupPath, ignoreMetadataErrors: true);
                tryDelete(backupPath);
            }
            else
            {
                File.Move(tempPath, finalPath, overwrite: true);
            }
        }
        catch
        {
            // Every attempt failed -- the caller already logs this (see
            // NetworkIndexerPublisher.PublishCheckpoint's catch), so this only prevents tempPath from
            // being orphaned forever once nothing else is ever going to retry it.
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            throw;
        }
    }
}
