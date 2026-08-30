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
                    if (!IsAuthorizedControlClient(pipe))
                        return new PipeResponse { Kind = PipeResponseKind.Error, Message = "Unauthorized caller." };
                    Logger.Log("[UsnService] Received REBUILD request from client.");
                    engine?.InitializeOrLoadIndex(true);
                    return new PipeResponse { Kind = PipeResponseKind.Ok };

                case SearchRequestId.Initialize:
                    Logger.Log("[UsnService] Received INITIALIZE request from client.");
                    engine?.InitializeOrLoadIndex(false);
                    return new PipeResponse { Kind = PipeResponseKind.Ok };

                case SearchRequestId.RebuildDrive:
                    if (!IsAuthorizedControlClient(pipe))
                        return new PipeResponse { Kind = PipeResponseKind.Error, Message = "Unauthorized caller." };
                    var drive = msg.Drive ?? string.Empty;
                    Logger.Log($"[UsnService] Received REBUILD_DRIVE request from client: {drive}");
                    return engine?.RebuildDriveIndex(drive) == true
                        ? new PipeResponse { Kind = PipeResponseKind.Ok }
                        : new PipeResponse { Kind = PipeResponseKind.Error, Message = "Invalid or disabled drive" };

                case SearchRequestId.DeleteDriveIndex:
                    if (!IsAuthorizedControlClient(pipe))
                        return new PipeResponse { Kind = PipeResponseKind.Error, Message = "Unauthorized caller." };
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
                    if (!IsAuthorizedControlClient(pipe))
                        return new PipeResponse { Kind = PipeResponseKind.Error, Message = "Unauthorized caller." };
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
                    Logger.ClearCurrentLog();
                    return new PipeResponse { Kind = PipeResponseKind.Ok };

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
