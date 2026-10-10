using System.IO.Pipes;
using Lertaro.Core.Services.HookLaunch;
using Lertaro.Core.Services.Pipe;
using Lertaro.Core.Services.Search;
using Lertaro.Core.Wire;

namespace Lertaro.Core.Services;

// Request dispatch for UsnServicePipeServer's non-streaming commands (everything except Search/SearchDir,
// SubscribeStatus, and LaunchHook, which stay in the server itself since they stream rather than return a
// single response) -- extracted to keep UsnServicePipeServer.cs under the project's line limit.
internal static class UsnServicePipeRequestProcessor
{
    internal static async Task<PipeResponse> ProcessAsync(SearchEngine? engine, SearchRequestMessage msg,
        CancellationToken token, NamedPipeServerStream pipe, CallerVisibility visibility)
    {
        if (msg.Id != SearchRequestId.GetSpaceEntries)
            return Process(engine, msg, token, pipe, visibility);

        // Closing the blank window / typing a query closes its space pipe. Stop any remaining
        // permission checks on the listed entries when that happens.
        using var queryCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var watcherCts = new CancellationTokenSource();
        var watcher = SearchStreamPump.WatchForClientDisconnectAsync(pipe, queryCts, watcherCts.Token);
        try { return Process(engine, msg, queryCts.Token, pipe, visibility); }
        finally
        {
            watcherCts.Cancel();
            await watcher.ConfigureAwait(false);
        }
    }

    public static PipeResponse Process(SearchEngine? engine, SearchRequestMessage msg, CancellationToken token, NamedPipeServerStream pipe,
        CallerVisibility visibility)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            var administrative = msg.Id is SearchRequestId.Rebuild or SearchRequestId.RebuildDrive or
                SearchRequestId.DeleteDriveIndex or SearchRequestId.CancelDriveIndex or
                SearchRequestId.SetMachineSettings or SearchRequestId.ClearServiceLog or SearchRequestId.GetServiceLog;
            if (RequiresAuthorizedCaller(msg.Id) &&
                !PipeClientIdentity.IsAuthorizedApp(pipe, administrative))
            {
                Logger.Log($"[UsnService] Refused {msg.Id}: the caller lacks the required App identity or administrator access.", LogLevel.Warn);
                return new PipeResponse { Kind = PipeResponseKind.Error, Message = "Unauthorized caller." };
            }
            switch (msg.Id)
            {
                case SearchRequestId.Ping:
                    return new PipeResponse { Kind = PipeResponseKind.Ok };

                case SearchRequestId.Status:
                    var status = engine?.GetStatus();
                    return new PipeResponse
                    {
                        Kind = PipeResponseKind.Status,
                        Status = status ?? new Indexer.Usn.UsnIndexer.IndexerStatus { State = "error" }
                    };

                case SearchRequestId.Rebuild:
                    Logger.Log("[UsnService] Received REBUILD request from client.");
                    engine?.InitializeOrLoadIndex(true);
                    return new PipeResponse { Kind = PipeResponseKind.Ok };

                case SearchRequestId.Initialize:
                    Logger.Log("[UsnService] Received INITIALIZE request from client.");
                    engine?.InitializeOrLoadIndex(false);
                    return new PipeResponse { Kind = PipeResponseKind.Ok };

                case SearchRequestId.RebuildDrive:
                    var drive = msg.Drive ?? string.Empty;
                    Logger.Log($"[UsnService] Received REBUILD_DRIVE request from client: {drive}");
                    return engine?.RebuildDriveIndex(drive) == true
                        ? new PipeResponse { Kind = PipeResponseKind.Ok }
                        : new PipeResponse { Kind = PipeResponseKind.Error, Message = "Invalid or disabled drive" };

                case SearchRequestId.DeleteDriveIndex:
                    var deleteDrive = msg.Drive ?? string.Empty;
                    Logger.Log($"[UsnService] Received DELETE_DRIVE_INDEX request from client: {deleteDrive}");
                    return engine?.DeleteDriveIndex(deleteDrive) == true
                        ? new PipeResponse { Kind = PipeResponseKind.Ok }
                        : new PipeResponse { Kind = PipeResponseKind.Error, Message = "Invalid drive" };

                case SearchRequestId.CancelDriveIndex:
                    var cancelDrive = msg.Drive ?? string.Empty;
                    Logger.Log($"[UsnService] Received CANCEL_DRIVE_INDEX request from client: {cancelDrive}");
                    return engine?.CancelDriveIndex(cancelDrive) == true
                        ? new PipeResponse { Kind = PipeResponseKind.Ok }
                        : new PipeResponse { Kind = PipeResponseKind.Error, Message = "Not currently rebuilding" };

                case SearchRequestId.GetMachineSettings:
                    if (engine == null)
                        return new PipeResponse { Kind = PipeResponseKind.Error, Message = "Machine settings are not ready." };
                    return new PipeResponse
                    {
                        Kind = PipeResponseKind.MachineSettings,
                        MachineSettings = engine.GetMachineSettings()
                    };

                case SearchRequestId.SetMachineSettings:
                    var settings = msg.MachineSettings;
                    if (settings?.LocalDrives == null || engine == null)
                        return new PipeResponse { Kind = PipeResponseKind.Error, Message = "Invalid settings" };
                    Logger.Log("[UsnService] Received SET_MACHINE_SETTINGS request.");
                    engine.UpdateMachineSettings(settings);
                    return new PipeResponse { Kind = PipeResponseKind.Ok };

                case SearchRequestId.GetFileMetadata:
                    // The request's own paths are filtered, not just the reply: an answer at all says whether
                    // the file exists.
                    var paths = (msg.FilePaths ?? new List<string>()).Where(visibility.IsVisible).Select(Path.GetFullPath).ToList();
                    var metadata = engine?.GetFileMetadataBatch(paths) ?? new Dictionary<string, FileMetadataEntry>();
                    return new PipeResponse { Kind = PipeResponseKind.FileMetadata, FileMetadata = metadata };

                case SearchRequestId.GetRecentFiles:
                    var directories = (msg.Directories ?? []).Where(visibility.IsVisible).Select(Path.GetFullPath).ToList();
                    var recentFiles = (engine?.GetRecentFiles(directories, msg.Limit, msg.MaxAgeMinutes) ?? new List<SearchResult>())
                        .Where(result => visibility.IsVisible(result.Path)).ToList();
                    return new PipeResponse { Kind = PipeResponseKind.RecentFiles, RecentFiles = recentFiles };

                case SearchRequestId.GetSpaceEntries:
                    var spaceEntries = visibility.IsVisible(msg.Drive)
                        ? engine?.GetSpaceEntries(string.IsNullOrEmpty(msg.Drive) ? null : Path.GetFullPath(msg.Drive), path =>
                        {
                            token.ThrowIfCancellationRequested();
                            return visibility.IsVisible(path);
                        }) ?? [] : [];
                    return new PipeResponse { Kind = PipeResponseKind.SpaceEntries, SpaceEntries = spaceEntries };

                case SearchRequestId.ClearServiceLog:
                    // Reported, not assumed: the App's log page says "cleared" only when this did it.
                    return Logger.ClearCurrentLog()
                        ? new PipeResponse { Kind = PipeResponseKind.Ok }
                        : new PipeResponse { Kind = PipeResponseKind.Error, Message = "The service could not truncate its own log file" };

                case SearchRequestId.GetServiceLog:
                    return new PipeResponse { Kind = PipeResponseKind.ServiceLog,
                        Message = string.Join('\n', Logger.ReadLogLines(Path.Combine(Logger.SharedDataDir, "logs", "service.log")).TakeLast(500)) };

                case SearchRequestId.ClearPathCaches:
                    engine?.ClearPathCaches();
                    return new PipeResponse { Kind = PipeResponseKind.Ok };
            }

            return new PipeResponse { Kind = PipeResponseKind.Error, Message = "Unknown command" };
        }
        catch (OperationCanceledException)
        {
            return new PipeResponse { Kind = PipeResponseKind.Error, Message = "Cancelled" };
        }
        catch (Exception ex)
        {
            Logger.Log($"[UsnService] Error processing request {msg.Id}: {ex.Message}", LogLevel.Error);
            return new PipeResponse { Kind = PipeResponseKind.Error, Message = ex.Message };
        }
    }

    // State changes require this installation's App. Machine changes and service logs also require
    // an administrator account; read-only file queries retain their per-caller visibility checks.
    internal static bool RequiresAuthorizedCaller(SearchRequestId id) => id is
        SearchRequestId.Rebuild or SearchRequestId.Initialize or SearchRequestId.RebuildDrive or
        SearchRequestId.DeleteDriveIndex or SearchRequestId.CancelDriveIndex or SearchRequestId.SetMachineSettings or
        SearchRequestId.ClearServiceLog or SearchRequestId.GetServiceLog or SearchRequestId.ClearPathCaches;
}
