using Lertaro.Core.IndexV2.Persistence;

namespace Lertaro.Core.IndexV2.Space;

/// <summary>Read-only space view over one persisted index snapshot; it never touches indexed paths.</summary>
public sealed class IndexedSpaceSource : IDisposable
{
    private readonly Snapshot _snapshot;

    private IndexedSpaceSource(Snapshot snapshot)
    {
        _snapshot = snapshot;
        RootRow = snapshot.FirstRowForId(snapshot.RootId);
        if (RootRow < 0)
            throw new InvalidDataException("Index snapshot has no source root row.");
    }

    public string SourceKey => _snapshot.SourceKey;
    public string RootPath => _snapshot.SourceRoot;
    public DateTime LastUpdated => _snapshot.LastUpdated;
    public int RootRow { get; }
    public int TotalFiles => _snapshot.TotalFiles;
    public int TotalDirectories => Math.Max(0, _snapshot.TotalDirs - 1);
    public long TotalSize => Math.Max(0, _snapshot.RecursiveSizes[RootRow]);

    public static IndexedSpaceSource Open(string path) => new(Snapshot.Open(path));

    public IndexedSpaceEntry Root => CreateEntry(RootRow, RootPath);

    public IReadOnlyList<IndexedSpaceEntry> GetChildren(int directoryRow)
    {
        if ((uint)directoryRow >= (uint)_snapshot.Count || !_snapshot.IsDirectory(directoryRow))
            return Array.Empty<IndexedSpaceEntry>();

        var childRows = _snapshot.ChildrenOf(directoryRow);
        var result = new List<IndexedSpaceEntry>(childRows.Length);
        foreach (var row in childRows)
        {
            if (!_snapshot.IsDeleted(row) && !_snapshot.IsHiddenOrSystem(row))
                result.Add(CreateEntry(row, _snapshot.GetName(row)));
        }
        result.Sort(static (left, right) =>
        {
            var bySize = right.Size.CompareTo(left.Size);
            if (bySize != 0)
                return bySize;
            if (left.IsDirectory != right.IsDirectory)
                return left.IsDirectory ? -1 : 1;
            return StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name);
        });
        return result;
    }

    public string GetPath(int row) => (uint)row < (uint)_snapshot.Count ? _snapshot.GetFullPath(row) : RootPath;

    private IndexedSpaceEntry CreateEntry(int row, string name)
    {
        var isDirectory = _snapshot.IsDirectory(row);
        var size = _snapshot.RecursiveSizes[row];
        var duplicate = !isDirectory && _snapshot.Sizes[row] > 0 && size == 0
            && ((row > 0 && _snapshot.Ids[row - 1] == _snapshot.Ids[row])
                || (row + 1 < _snapshot.Count && _snapshot.Ids[row + 1] == _snapshot.Ids[row]));
        return new IndexedSpaceEntry(row, name, Math.Max(0, size), isDirectory, duplicate);
    }

    public void Dispose() => _snapshot.Dispose();
}
