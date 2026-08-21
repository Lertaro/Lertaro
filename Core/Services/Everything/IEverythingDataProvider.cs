namespace Lertaro.Core.Services.Everything;

/// <summary>Represents search results along with totals for Everything IPC replies.</summary>
public sealed record EverythingQueryResult(
    IReadOnlyList<EverythingResultItem> Items,
    uint TotalItems,
    uint TotalFolders,
    uint TotalFiles);

/// <summary>Abstraction supplying search results and run history to the Everything IPC dispatcher.</summary>
public interface IEverythingDataProvider
{
    Task<EverythingQueryResult> ExecuteQueryAsync(EverythingQueryRequest request, CancellationToken token = default);
    uint GetRunCount(string fileName);
    void SetRunCount(string fileName, uint count);
    uint IncrementRunCount(string fileName);
}
