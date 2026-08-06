namespace Lertaro.Core.Indexer.Usn;

internal sealed class UsnDriveIndexResult
{
    public required FileRecordStore Store { get; init; }
    public required long NextUsn { get; init; }
    public required ulong JournalId { get; init; }
    public required bool IsSortedById { get; init; }
    public int ItemCount => Store.Records.Count - 1;
}
