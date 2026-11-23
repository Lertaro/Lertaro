using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>Session-owned counters: constant work per progress event, including reset on a checksum retry.</summary>
internal sealed class LocalSendReceiveProgress
{
    private readonly Dictionary<string, int> _indices = new(StringComparer.Ordinal);
    private readonly long[] _transferred;
    private readonly HashSet<string> _completed = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private long _totalTransferred;

    internal long TotalBytes { get; }
    internal int TotalFiles => _transferred.Length;

    internal LocalSendReceiveProgress(IReadOnlyDictionary<string, LocalSendFileDto> files, IReadOnlySet<string>? selectedIds)
    {
        foreach (var (id, file) in files)
        {
            if (selectedIds != null && !selectedIds.Contains(id)) continue;
            _indices.Add(id, _indices.Count);
            TotalBytes = checked(TotalBytes + file.Size);
        }
        _transferred = new long[_indices.Count];
    }

    internal int FileIndex(string id) => _indices[id] + 1;

    internal long Update(string id, long bytes)
    {
        lock (_gate)
        {
            var index = _indices[id];
            _totalTransferred += bytes - _transferred[index];
            _transferred[index] = bytes;
            return _totalTransferred;
        }
    }

    internal int Complete(string id)
    {
        lock (_gate)
        {
            _completed.Add(id);
            return _completed.Count;
        }
    }
}
