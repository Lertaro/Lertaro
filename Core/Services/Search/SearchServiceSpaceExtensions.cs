using Lertaro.Core.IndexV2.Space;
using Lertaro.Core.Services.Network;
using Lertaro.Core.Wire;

namespace Lertaro.Core.Services.Search;

/// <summary>Combines local-service and in-process network indexes without touching cache files.</summary>
public static class SearchServiceSpaceExtensions
{
    public static async Task<IReadOnlyList<SpaceIndexEntry>> GetSpaceEntriesAsync(
        this SearchService service, string? directory, CancellationToken token = default)
    {
        var networkTask = Task.Run(() => UserNetworkDriveSearch.GetSpaceEntries(directory), token);
        var response = await service.SendPipeCommandAsync(new SearchRequestMessage
        {
            Id = SearchRequestId.GetSpaceEntries,
            Drive = directory ?? string.Empty
        }, token).ConfigureAwait(false);

        var local = response.Kind == PipeResponseKind.SpaceEntries
            ? response.SpaceEntries ?? Array.Empty<SpaceIndexEntry>()
            : Array.Empty<SpaceIndexEntry>();
        var network = await networkTask.ConfigureAwait(false);

        return local.Concat(network)
            .GroupBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(entry => entry.Size)
            .ThenByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
