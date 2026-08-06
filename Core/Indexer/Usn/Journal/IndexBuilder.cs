using System.Diagnostics;
using Lertaro.Core.Indexer.NetworkDrive.Walk;

namespace Lertaro.Core.Indexer.Usn.Journal;

internal static class IndexBuilder
{
    public static List<(string Drive, ulong JournalId, long NextUsn)> BuildDrives(
        JournalReader reader,
        IReadOnlyList<string> drives,
        Action<string, string> setDriveState,
        Action<string, int, int> onDriveProgress,
        Func<string, Action<int, int>?, CancellationToken, FileRecordStore?> buildFolderDrive,
        Action<string, FileRecordStore, int, int> onFolderDriveCompleted,
        Action<string, UsnDriveIndexResult, int, int> onDriveCompleted,
        Action<double> onCompleted,
        Func<string, CancellationToken>? getToken = null,
        Action<string>? onDriveCancelled = null,
        Func<string, FileRecordStore?>? getPreviousStore = null,
        Action<string, FileRecordStore, NetworkDriveWalkStats, CancellationToken>? onDriveCheckpoint = null)
    {
        getToken ??= _ => CancellationToken.None;
        getPreviousStore ??= _ => null;
        var stopWatch = Stopwatch.StartNew();
        var monitorsToStart = new List<(string Drive, ulong JournalId, long NextUsn)>();

        var indexResults = new (string Drive, UsnDriveIndexResult? Result)[drives.Count];
        var folderResults = new (string Drive, FileRecordStore? Result)[drives.Count];
        var cancelled = new bool[drives.Count];

        Parallel.For(0, drives.Count, i =>
        {
            var drive = drives[i];
            Logger.Log($"[UsnIndexer] Indexing drive {drive} in parallel ({i + 1}/{drives.Count})");
            var token = getToken(drive);
            try
            {
                var fs = VolumeHelper.GetFileSystemType(drive);
                if (VolumeHelper.IsJournalCapableFileSystem(fs))
                    indexResults[i] = (drive, reader.IndexDrive(drive, getPreviousStore(drive), (files, dirs) => onDriveProgress(drive, files, dirs),
                        onDriveCheckpoint == null ? null : (store, stats) => onDriveCheckpoint(drive, store, stats, token), token));
                else
                    folderResults[i] = (drive, buildFolderDrive(drive, (files, dirs) => onDriveProgress(drive, files, dirs), token));
            }
            catch (OperationCanceledException)
            {
                // Caught HERE, per drive, rather than left to escape the Parallel.For body -- an exception
                // escaping one iteration is not contained to it: .NET stops scheduling further iterations
                // and can abandon others still in flight, then rethrows as an AggregateException from this
                // whole call. A single drive's Stop request must not abort every other drive's own scan.
                cancelled[i] = true;
            }
        });

        for (var i = 0; i < drives.Count; i++)
        {
            var drive = drives[i];
            var res = indexResults[i].Result;
            if (res != null)
            {
                var data = res;
                Logger.Log($"[UsnIndexer] Drive {drive} indexing completed. Found {data.ItemCount} items.");
                setDriveState(drive, "indexing");

                var progress = (int)(((double)(i + 1) / drives.Count) * 100);
                onDriveCompleted(drive, data, progress, i + 1);

                monitorsToStart.Add((drive, data.JournalId, data.NextUsn));
            }
            else if (folderResults[i].Result != null)
            {
                var result = folderResults[i].Result!;
                Logger.Log($"[UsnIndexer] Drive {drive} folder scan completed. Found {result.Records.Count} items.");
                setDriveState(drive, "indexing");

                var progress = (int)(((double)(i + 1) / drives.Count) * 100);
                onFolderDriveCompleted(drive, result, progress, i + 1);
                monitorsToStart.Add((drive, 0, 0));
            }
            else if (cancelled[i])
            {
                Logger.Log($"[UsnIndexer] Drive {drive} indexing cancelled by user request.");
                onDriveCancelled?.Invoke(drive);
            }
            else
            {
                Logger.Log($"[UsnIndexer] Drive {drive} indexing failed.", LogLevel.Error);
            }
        }

        stopWatch.Stop();
        onCompleted(stopWatch.Elapsed.TotalSeconds);

        return monitorsToStart;
    }
}
