using Lertaro.Core.Indexer.NetworkDrive;

using Lertaro.Core.Services.Network;

using Lertaro.Core.Wire;
namespace Lertaro.Core.Services.Search;

// Drive and settings admin pass-throughs for SearchService, as extension methods (matching
// RuntimeIndex's BucketExtensions/QueryExtensions/StoreExtensions) instead of a partial class, to keep
// SearchService.cs under the repo's per-file line limit.
public static class SearchServiceManagementExtensions
{
    public static void RefreshNetworkIndexes(this SearchService service)
    {
        UserNetworkDriveSearch.Refresh();
        SearchScopeCoverage.Invalidate();
    }

    public static void ConfigureNetworkIndexes(this SearchService service)
    {
        UserNetworkDriveSearch.Configure();
        SearchScopeCoverage.Invalidate();
    }
    public static bool RefreshNetworkDriveIndex(this SearchService service, string drive) => UserNetworkDriveSearch.RefreshDrive(drive);
    public static bool CancelNetworkDriveIndex(this SearchService service, string drive) => UserNetworkDriveSearch.CancelDrive(drive);
    public static IReadOnlyList<NetworkIndexStatus> GetNetworkIndexStatuses(this SearchService service) => UserNetworkDriveSearch.GetStatuses();
    public static bool HasNetworkDriveCache(this SearchService service, string drive) => UserNetworkDriveSearch.HasCache(drive);
    public static IReadOnlyList<string> GetCachedNetworkDrives(this SearchService service) => UserNetworkDriveSearch.GetCachedDrives();
    public static void DeleteNetworkDriveCache(this SearchService service, string drive) => UserNetworkDriveSearch.DeleteCache(drive);

    public static async Task InitializeOrLoadIndexAsync(this SearchService service, bool forceRebuild = false, CancellationToken token = default)
    {
        var requestId = forceRebuild ? SearchRequestId.Rebuild : SearchRequestId.Initialize;
        await service.SendPipeCommandAsync(new SearchRequestMessage { Id = requestId }, token).ConfigureAwait(false);
    }

    // service.log lives under the service's own (elevated/system) data directory, which the App
    // process cannot write to directly -- ask the service to truncate its own log file instead.
    public static async Task<bool> ClearServiceLogAsync(this SearchService service, CancellationToken token = default)
    {
        var resp = await service.SendPipeCommandAsync(new SearchRequestMessage { Id = SearchRequestId.ClearServiceLog }, token).ConfigureAwait(false);
        return resp.Kind == PipeResponseKind.Ok;
    }

    public static async Task<bool> RebuildDriveIndexAsync(this SearchService service, string drive, CancellationToken token = default)
    {
        var resp = await service.SendPipeCommandAsync(new SearchRequestMessage { Id = SearchRequestId.RebuildDrive, Drive = drive }, token).ConfigureAwait(false);
        return resp.Kind == PipeResponseKind.Ok;
    }

    public static async Task<bool> DeleteDriveIndexAsync(this SearchService service, string drive, CancellationToken token = default)
    {
        var resp = await service.SendPipeCommandAsync(new SearchRequestMessage { Id = SearchRequestId.DeleteDriveIndex, Drive = drive }, token).ConfigureAwait(false);
        return resp.Kind == PipeResponseKind.Ok;
    }

    // Local drive rebuilds run in the elevated --service process (unlike network drives' in-process
    // CancelNetworkDriveIndex), so a Stop request has to go over the pipe like Rebuild/Delete do.
    public static async Task<bool> CancelDriveIndexAsync(this SearchService service, string drive, CancellationToken token = default)
    {
        var resp = await service.SendPipeCommandAsync(new SearchRequestMessage { Id = SearchRequestId.CancelDriveIndex, Drive = drive }, token).ConfigureAwait(false);
        return resp.Kind == PipeResponseKind.Ok;
    }

    public static async Task<MachineSettings> GetMachineSettingsAsync(this SearchService service, CancellationToken token = default)
    {
        var resp = await service.SendPipeCommandAsync(new SearchRequestMessage { Id = SearchRequestId.GetMachineSettings }, token).ConfigureAwait(false);
        if (resp.Kind == PipeResponseKind.MachineSettings && resp.MachineSettings != null) return resp.MachineSettings;
        if (resp.Kind == PipeResponseKind.Error) Logger.Log($"[SearchService] GetMachineSettings failed: {resp.Message}", LogLevel.Error);
        return new MachineSettings();
    }

    public static async Task<bool> SaveMachineSettingsAsync(this SearchService service, MachineSettings settings, CancellationToken token = default)
    {
        var resp = await service.SendPipeCommandAsync(new SearchRequestMessage { Id = SearchRequestId.SetMachineSettings, MachineSettings = settings }, token).ConfigureAwait(false);
        SearchScopeCoverage.Invalidate();
        return resp.Kind == PipeResponseKind.Ok;
    }

    // In-memory index lookup only (no disk I/O) -- paths the service isn't tracking are simply
    // absent from the result, not an error; the caller is expected to fall back to a live stat.
    public static async Task<Dictionary<string, FileMetadataEntry>> GetFileMetadataBatchAsync(this SearchService service, IReadOnlyList<string> paths, CancellationToken token = default)
    {
        var resp = await service.SendPipeCommandAsync(new SearchRequestMessage { Id = SearchRequestId.GetFileMetadata, FilePaths = paths.ToList() }, token).ConfigureAwait(false);
        if (resp.Kind == PipeResponseKind.FileMetadata && resp.FileMetadata != null) return resp.FileMetadata;
        if (resp.Kind == PipeResponseKind.Error) Logger.Log($"[SearchService] GetFileMetadataBatch failed: {resp.Message}", LogLevel.Error);
        return new Dictionary<string, FileMetadataEntry>(StringComparer.OrdinalIgnoreCase);
    }
}
