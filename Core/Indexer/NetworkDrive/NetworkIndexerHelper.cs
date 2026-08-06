using Lertaro.Core.Services.Network;

namespace Lertaro.Core.Indexer.NetworkDrive;

internal static class NetworkIndexerHelper
{
    public static string ResolveDriveFromId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return string.Empty;

        return NetworkDriveResolver.GetNetworkDrives()
            .FirstOrDefault(d => string.Equals(NetworkDriveResolver.GetNetworkId(d.Letter), id, StringComparison.OrdinalIgnoreCase))
            ?.Letter ?? string.Empty;
    }

    public static NetworkIndexStatus CreateStatus(string drive, string state, int items, NetworkIndex? index, NetworkIndexStatus? current, string error = "") => new NetworkIndexStatus
    {
        Drive = drive,
        State = state,
        Items = items,
        Skipped = index?.Skipped ?? current?.Skipped ?? 0,
        Errors = index?.Errors ?? current?.Errors ?? 0,
        EnumerateErrors = index?.EnumerateErrors ?? current?.EnumerateErrors ?? 0,
        AttributeErrors = index?.AttributeErrors ?? current?.AttributeErrors ?? 0,
        ReparseSkipped = index?.ReparseSkipped ?? current?.ReparseSkipped ?? 0,
        SlowDirectories = index?.SlowDirectories ?? current?.SlowDirectories ?? 0,
        CachePath = current?.CachePath ?? IndexerHelper.GetCachePath(drive),
        LastUpdated = index?.LastUpdated ?? current?.LastUpdated,
        Error = error
        // Revision/ChangedDirectories are deliberately NOT set here. This rebuilds the status object
        // from scratch and several callers pass current: null, which would reset a revision that must
        // only ever go up -- NetworkIndexerPublisher.StoreStatus carries them across instead, in the
        // one place that owns the drive's status dictionary.
    };
}
