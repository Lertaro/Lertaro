using System.IO.MemoryMappedFiles;

namespace Lertaro.Core.IndexV2.Persistence;

// The V2 engine's "load": memory-map the snapshot and hand out typed spans over its sections. No
// parsing, no per-name string objects, no index rebuild -- opening is O(1) and resident memory is
// whatever pages queries actually touch. The base is immutable; live changes overlay it (DeltaOverlay)
// until a compaction folds them into a fresh file. This object must outlive every span it hands out.
public sealed unsafe class Snapshot : IDisposable
{
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly long[] _offsets;
    private byte* _base;

    internal SnapshotFormat.Meta Meta { get; }
    public int Count => Meta.RowCount;
    public int UniqueCount => Meta.UniqueCount;
    public string SourceKey => Meta.SourceKey;
    public string SourceRoot => Meta.SourceRoot;
    public int TotalFiles => Meta.TotalFiles;
    public int TotalDirs => Meta.TotalDirs;
    public FileRecordSourceKind SourceKind => Meta.SourceKind;
    public FileRecordIdKind IdKind => Meta.IdKind;
    public string FileSystemType => Meta.FileSystemType;
    public uint VolumeSerialNumber => Meta.VolumeSerialNumber;
    public UInt128 RootId => Meta.RootId;
    public ulong JournalId => Meta.JournalId;
    public long NextUsn => Meta.NextUsn;
    public bool IsComplete => Meta.IsComplete;
    public string ExclusionRulesFingerprint => Meta.ExclusionRulesFingerprint;
    public string AliasProvidersFingerprint => Meta.AliasProvidersFingerprint;
    public DateTime LastUpdated => Meta.LastUpdated;

    public static Snapshot Open(string path) => new(path);

    private Snapshot(string path)
    {
        // Opened explicitly (not via the path-based CreateFromFile convenience overload) so the handle
        // includes FileShare.Delete: a live checkpoint/compaction cycle writes a FRESH snapshot to the
        // same stable per-drive path while THIS mapping is still open on it (SnapshotWriter.Write's
        // atomic temp-then-File.Replace needs to rename over the currently-open file) -- without
        // FileShare.Delete on every open handle to that path, ReplaceFile fails with "the process cannot
        // access the file because it is being used by another process", and no amount of retrying helps
        // since the lock isn't transient, it's held for this Snapshot's whole lifetime.
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        try
        {
            // leaveOpen:false -- the MemoryMappedFile takes ownership of `stream` from here on and
            // disposes it together with itself; not wrapped in a `using` here for that reason.
            _mmf = MemoryMappedFile.CreateFromFile(stream, mapName: null, capacity: 0, MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: false);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
        try
        {
            _accessor = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref _base);
            using var headerStream = _mmf.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);
            using var reader = new BinaryReader(headerStream, SnapshotFormat.NameEncoding);
            Meta = SnapshotFormat.ReadHeader(reader);
            _offsets = SnapshotFormat.ComputeSectionOffsets(Meta, out _);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private byte* Section(SnapshotSection section) => _base + _offsets[(int)section];

    // Hot sections -- what name matching touches.
    public ReadOnlySpan<uint> NameIds => new(Section(SnapshotSection.NameIds), Count);
    public ReadOnlySpan<ushort> Flags => new(Section(SnapshotSection.Flags), Count);
    public ReadOnlySpan<int> ParentIndexes => new(Section(SnapshotSection.ParentIndexes), Count);
    public ReadOnlySpan<ulong> UniqueMasks => new(Section(SnapshotSection.UniqueMasks), UniqueCount);
    public ReadOnlySpan<uint> NameOffsets => new(Section(SnapshotSection.NameOffsets), UniqueCount + 1);

    // Cold sections -- paged in for result display, sorting, recency and USN maintenance.
    public ReadOnlySpan<UInt128> Ids => new(Section(SnapshotSection.Ids), Count);
    public ReadOnlySpan<long> Sizes => new(Section(SnapshotSection.Sizes), Count);
    public ReadOnlySpan<uint> CreationTimes => new(Section(SnapshotSection.CreationTimes), Count);
    public ReadOnlySpan<uint> LastWriteTimes => new(Section(SnapshotSection.LastWriteTimes), Count);
    public ReadOnlySpan<uint> LastAccessTimes => new(Section(SnapshotSection.LastAccessTimes), Count);

    public ReadOnlySpan<byte> UniqueNameUtf8(int uid)
    {
        var offsets = NameOffsets;
        return new ReadOnlySpan<byte>(Section(SnapshotSection.NameBlob) + offsets[uid], (int)(offsets[uid + 1] - offsets[uid]));
    }

    public string GetUniqueName(int uid) => SnapshotFormat.NameEncoding.GetString(UniqueNameUtf8(uid));

    // Baked at write time: true = the unique name is pure ASCII, so UniqueNameUtf8's bytes ARE its
    // chars (same values, same offsets) and matching can skip UTF-16 decoding entirely.
    public bool IsUniqueAscii(int uid)
    {
        var bits = new ReadOnlySpan<ulong>(Section(SnapshotSection.UniqueAsciiBits), (UniqueCount + 63) / 64);
        return (bits[uid >> 6] & (1UL << (uid & 63))) != 0;
    }

    public string GetName(int row) => GetUniqueName((int)NameIds[row]);

    public bool IsDeleted(int row) => (Flags[row] & (ushort)FileRecordFlags.Deleted) != 0;
    public bool IsDirectory(int row) => (Flags[row] & (ushort)FileRecordFlags.Directory) != 0;

    public ReadOnlySpan<int> ChildrenOf(int row)
    {
        var starts = new ReadOnlySpan<int>(Section(SnapshotSection.ChildStarts), Count + 1);
        return new ReadOnlySpan<int>(Section(SnapshotSection.Children) + 4L * starts[row], starts[row + 1] - starts[row]);
    }

    // All rows sharing a unique name, ascending -- the search fan-out replacing per-char buckets.
    public ReadOnlySpan<int> RowsForUid(int uid)
    {
        var starts = new ReadOnlySpan<int>(Section(SnapshotSection.UidStarts), UniqueCount + 1);
        return new ReadOnlySpan<int>(Section(SnapshotSection.UidRows) + 4L * starts[uid], starts[uid + 1] - starts[uid]);
    }

    public bool HasAliases(int uid)
    {
        var starts = new ReadOnlySpan<int>(Section(SnapshotSection.AliasStarts), UniqueCount + 1);
        return starts[uid + 1] > starts[uid];
    }

    // Precomputed alias strings for a unique name, decoded on demand (only when the name itself
    // failed to match, and only for the CJK subset that has aliases at all).
    public int GetAliases(int uid, List<(string Alias, byte ProviderId)> into)
    {
        into.Clear();
        var starts = new ReadOnlySpan<int>(Section(SnapshotSection.AliasStarts), UniqueCount + 1);
        var entryOffsets = new ReadOnlySpan<uint>(Section(SnapshotSection.AliasEntryOffsets), Meta.AliasEntryCount + 1);
        var providerIds = new ReadOnlySpan<byte>(Section(SnapshotSection.AliasProviderIds), Meta.AliasEntryCount);
        for (var e = starts[uid]; e < starts[uid + 1]; e++)
        {
            var alias = SnapshotFormat.NameEncoding.GetString(
                new ReadOnlySpan<byte>(Section(SnapshotSection.AliasBlob) + entryOffsets[e], (int)(entryOffsets[e + 1] - entryOffsets[e])));
            into.Add((alias, providerIds[e]));
        }
        return into.Count;
    }

    // Zero-decode alias access -- the span counterpart of GetAliases, for matching an alias's raw
    // UTF-8 bytes via a caller-owned scratch buffer without materializing a string per alias.
    // AliasEntryRange gives the [start, end) entry indices for a uid; AliasUtf8/AliasProviderId
    // address one entry.
    internal (int Start, int End) AliasEntryRange(int uid)
    {
        var starts = new ReadOnlySpan<int>(Section(SnapshotSection.AliasStarts), UniqueCount + 1);
        return (starts[uid], starts[uid + 1]);
    }

    internal ReadOnlySpan<byte> AliasUtf8(int entry)
    {
        var entryOffsets = new ReadOnlySpan<uint>(Section(SnapshotSection.AliasEntryOffsets), Meta.AliasEntryCount + 1);
        return new ReadOnlySpan<byte>(Section(SnapshotSection.AliasBlob) + entryOffsets[entry], (int)(entryOffsets[entry + 1] - entryOffsets[entry]));
    }

    internal byte AliasProviderId(int entry)
        => new ReadOnlySpan<byte>(Section(SnapshotSection.AliasProviderIds), Meta.AliasEntryCount)[entry];

    // First (lowest) row holding this id, or -1; hard-link duplicates sit adjacent.
    public int FirstRowForId(UInt128 id)
    {
        var ids = Ids;
        int low = 0, high = ids.Length - 1, found = -1;
        while (low <= high)
        {
            var mid = low + ((high - low) >> 1);
            if (ids[mid] >= id)
            {
                if (ids[mid] == id)
                    found = mid;
                high = mid - 1;
            }
            else
            {
                low = mid + 1;
            }
        }
        return found;
    }

    // The true parent FRN stashed for a row whose parent wasn't indexed at build time.
    public bool TryGetOrphanParent(int row, out UInt128 parentFrn)
    {
        var rows = new ReadOnlySpan<int>(Section(SnapshotSection.OrphanRows), Meta.OrphanCount);
        var frns = new ReadOnlySpan<UInt128>(Section(SnapshotSection.OrphanFrns), Meta.OrphanCount);
        int low = 0, high = rows.Length - 1;
        while (low <= high)
        {
            var mid = low + ((high - low) >> 1);
            if (rows[mid] == row)
            {
                parentFrn = frns[mid];
                return true;
            }
            if (rows[mid] < row)
                low = mid + 1;
            else
                high = mid - 1;
        }
        parentFrn = default;
        return false;
    }

    // The true parent FRN for a row -- resolved parent's own id, the stashed orphan FRN if
    // unresolved, or self (root) if genuinely rootless. Mirrors QueryExtensions.GetParentId.
    public UInt128 GetParentId(int row)
    {
        var parentIndex = ParentIndexes[row];
        if ((uint)parentIndex < (uint)Count)
            return Ids[parentIndex];
        return TryGetOrphanParent(row, out var parentFrn) ? parentFrn : Ids[row];
    }

    // Base-only path building with the old engine's orphan recovery: an unresolved parent link
    // re-resolves through its stashed FRN to a live directory row, else the tail hangs off the source
    // root. Delta-aware path building lives in the overlay. Depth cap is a corruption backstop only.
    public string GetFullPath(int row)
    {
        var segments = new List<string>(8);
        var current = row;
        for (var depth = 0; depth < 512; depth++)
        {
            var parent = ParentIndexes[current];
            segments.Add(GetName(current));
            if (parent < 0)
            {
                if (TryGetOrphanParent(current, out var parentFrn)
                    && FirstRowForId(parentFrn) is var resolved && resolved >= 0
                    && resolved != current && IsDirectory(resolved) && !IsDeleted(resolved))
                {
                    current = resolved;
                    segments.Add(GetName(current));
                    parent = ParentIndexes[current];
                    if (parent < 0 || parent == current)
                        break;
                    current = parent;
                    continue;
                }
                break;
            }
            if (parent == current)
                break;
            current = parent;
        }

        var builder = new System.Text.StringBuilder(SourceRoot, 64);
        for (var i = segments.Count - 1; i >= 0; i--)
        {
            if (segments[i].Length == 0)
                continue;
            if (builder[^1] != '\\')
                builder.Append('\\');
            builder.Append(segments[i]);
        }
        return builder.ToString();
    }

    public void Dispose()
    {
        if (_base != null)
        {
            _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            _base = null;
        }
        _accessor?.Dispose();
        _mmf.Dispose();
    }
}
