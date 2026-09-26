using System.IO.Pipes;
using Lertaro.Core.Services.HookLaunch;
using Lertaro.Core.Services.Pipe;
using Lertaro.Core.Wire;

namespace Lertaro.Core.Services;

// Request dispatch for UsnServicePipeServer's non-streaming commands (everything except Search/SearchDir,
// SubscribeStatus, and LaunchHook, which stay in the server itself since they stream rather than return a
// single response) -- extracted to keep UsnServicePipeServer.cs under the project's line limit.
internal static class UsnServicePipeRequestProcessor
{
    public static PipeResponse Process(SearchEngine? engine, SearchRequestMessage msg, CancellationToken token, NamedPipeServerStream pipe)
    {
        try
        {
            token.ThrowIfCancellationRequested();

            // One choke point for every state-changing command. Checking at the dispatch site instead
            // meant each new command had to remember its own guard, and four of them did not.
            if (RequiresAuthorizedCaller(msg.Id) && !IsAuthorizedControlClient(pipe))
            {
                // Logged, not merely refused: the rejection is invisible to the caller's UI (see
                // SearchServiceManagementExtensions.InitializeOrLoadIndexAsync, which ignores the response),
                // so without a line here a legitimate App whose identity check fails -- a portable or
                // debug build outside the install directory, the same case HookLaunchRequestHandler
                // documents -- would watch index initialization silently do nothing, with no trace on
                // either side.
                Logger.Log($"[UsnService] Refused {msg.Id}: the caller is not this install's Lertaro.App.exe.", LogLevel.Warn);
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
                    return new PipeResponse
                    {
                        Kind = PipeResponseKind.MachineSettings,
                        MachineSettings = engine?.GetMachineSettings() ?? new MachineSettings()
                    };

                case SearchRequestId.SetMachineSettings:
                    var settings = msg.MachineSettings;
                    if (settings == null)
                        return new PipeResponse { Kind = PipeResponseKind.Error, Message = "Invalid settings" };
                    Logger.Log("[UsnService] Received SET_MACHINE_SETTINGS request.");
                    engine?.UpdateMachineSettings(settings);
                    return new PipeResponse { Kind = PipeResponseKind.Ok };

                case SearchRequestId.GetFileMetadata:
                    var paths = msg.FilePaths ?? new List<string>();
                    var metadata = engine?.GetFileMetadataBatch(paths) ?? new Dictionary<string, FileMetadataEntry>();
                    return new PipeResponse { Kind = PipeResponseKind.FileMetadata, FileMetadata = metadata };

                case SearchRequestId.GetRecentFiles:
                    var directories = msg.Directories ?? new List<string>();
                    var recentFiles = engine?.GetRecentFiles(directories, msg.Limit, msg.MaxAgeMinutes) ?? new List<SearchResult>();
                    return new PipeResponse { Kind = PipeResponseKind.RecentFiles, RecentFiles = recentFiles };

                case SearchRequestId.GetSpaceEntries:
                    var spaceEntries = engine?.GetSpaceEntries(msg.Drive) ?? new List<IndexV2.Space.SpaceIndexEntry>();
                    return new PipeResponse { Kind = PipeResponseKind.SpaceEntries, SpaceEntries = spaceEntries };

                case SearchRequestId.ClearServiceLog:
                    // Reported, not assumed: the App's log page says "cleared" only when this did it.
                    return Logger.ClearCurrentLog()
                        ? new PipeResponse { Kind = PipeResponseKind.Ok }
                        : new PipeResponse { Kind = PipeResponseKind.Error, Message = "The service could not truncate its own log file" };

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

    // Commands that change service state or destroy data, and are therefore only ever issued by this
    // install's own Lertaro.App.exe: Rebuild/Initialize reload or rebuild the whole index, the three
    // drive commands start or tear down a per-drive index, SetMachineSettings rewrites machine-wide
    // configuration, and the two Clear commands truncate the service log and the path caches.
    //
    // Read/query commands are deliberately open -- Ping, Status, GetMachineSettings, GetFileMetadata,
    // GetRecentFiles, GetSpaceEntries -- because they are how any client (including the CLI and the
    // installer) finds out whether the service is up. That openness is a decision about this pipe's ACL
    // (PipeSecurityFactory.Create grants every local authenticated user access to it), NOT a claim that the
    // data is otherwise reachable: these commands return names, paths and metadata for anything the index
    // covers, which is a broader read than the caller's own token may have on disk. Commands the server
    // handles itself (Search, SearchDir, EnumerateDir, SubscribeStatus, SubscribeDirectoryChanges) never
    // reach here; LaunchHook carries its own check.
    internal static bool RequiresAuthorizedCaller(SearchRequestId id) => id switch
    {
        SearchRequestId.Rebuild or
        SearchRequestId.Initialize or
        SearchRequestId.RebuildDrive or
        SearchRequestId.DeleteDriveIndex or
        SearchRequestId.CancelDriveIndex or
        SearchRequestId.SetMachineSettings or
        SearchRequestId.ClearServiceLog or
        SearchRequestId.ClearPathCaches => true,
        _ => false,
    };

    private static bool IsAuthorizedControlClient(NamedPipeServerStream pipe)
    {
        try
        {
            return PipeClientIdentity.TryGetClientProcessId(pipe, out var callerPid) &&
                HookLaunchRequestHandler.IsGenuineAppProcess(callerPid);
        }
        catch
        {
            return false;
        }
    }
}
