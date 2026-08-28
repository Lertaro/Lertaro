using System.Text;

namespace Lertaro.Core.IndexV2.Persistence;

internal enum SnapshotSection
{
    NameIds, Flags, ParentIndexes, UniqueMasks, NameOffsets, NameBlob,
    Ids, Sizes, RecursiveSizes, CreationTimes, LastWriteTimes, LastAccessTimes,
    ChildStarts, Children, UidStarts, UidRows,
    AliasStarts, AliasEntryOffsets, AliasProviderIds, AliasBlob,
    OrphanRows, OrphanFrns,
    // One bit per unique name: 1 = pure ASCII, so its raw UTF-8 bytes ARE its chars and the search
    // hot path can match them with zero decode (FzfBytePattern). Baked at write time instead of
    // re-probing per candidate per query.
    UniqueAsciiBits,
}

// Single source of truth for the V2 snapshot layout: one memory-mappable file whose columnar
// sections ARE the runtime layout. The variable-length meta block sits after the fixed header;
// sections start at Meta.SectionsOffset, each aligned so a typed span over any section (including
// the 16-byte UInt128 id columns) is naturally aligned.
internal static class SnapshotFormat
{
    public const ulong Magic = 0x0000005844494C53; // "SLIDX\0\0\0" little-endian
    // Bumped 9 -> 10: recursive logical sizes support instant, index-only space analysis.
    public const int Version = 10;
    public const int SectionAlignment = 16;

    internal sealed class Meta
    {
        public string SourceKey = string.Empty;
        // Fully-formed root prefix ("C:\", "\\server\share\", ...) -- stored, not derived, so UNC and
        // WSL sources round-trip without the reader guessing.
        public string SourceRoot = string.Empty;
        public FileRecordSourceKind SourceKind;
        public FileRecordIdKind IdKind;
        public string FileSystemType = string.Empty;
        public uint VolumeSerialNumber;
        public UInt128 RootId;
        public ulong JournalId;
        public long NextUsn;
        public int RowCount;
        public int UniqueCount;
        public int NameBlobLength;
        public int ChildrenLength;
        public int AliasEntryCount;
        public int AliasBlobLength;
        public int OrphanCount;
        public int TotalFiles;
        public int TotalDirs;
        // Meaningless for local USN/MFT drives (always true/empty there -- see FileRecordStore.
        // IsComplete/ExclusionRulesFingerprint); load-bearing for network/WSL/folder-index drives'
        // resumable-scan diffing (TreeDiffBaseline) and exclusion-rule-change detection.
        public bool IsComplete;
        public string ExclusionRulesFingerprint = string.Empty;
        // AliasProviderRegistry.ComputeProvidersFingerprint() as of the write that produced this
        // snapshot's alias data. Compared against the CURRENT set of installed alias providers on load
        // (see UsnIndexerCacheExtensions/NetworkDriveCacheLocator) -- a mismatch means a provider was
        // installed/removed or bumped its own Version since, so every unique name's aliases need
        // regenerating via a forced recompaction.
        public string AliasProvidersFingerprint = string.Empty;
        // Wall-clock time this snapshot's data was last current -- FileRecordStore.LastUpdated's V2
        // equivalent, surfaced to the UI (e.g. "last refreshed" for a network drive).
        public DateTime LastUpdated;
        public long SectionsOffset;
    }

    public static void WriteHeader(BinaryWriter writer, Meta meta)
    {
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(meta.RowCount);
        writer.Write(meta.UniqueCount);
        writer.Write(meta.NameBlobLength);
        writer.Write(meta.ChildrenLength);
        writer.Write(meta.AliasEntryCount);
        writer.Write(meta.AliasBlobLength);
        writer.Write(meta.OrphanCount);
        writer.Write(meta.TotalFiles);
        writer.Write(meta.TotalDirs);
        writer.Write((byte)meta.SourceKind);
        writer.Write((byte)meta.IdKind);
        writer.Write(meta.VolumeSerialNumber);
        writer.Write((ulong)meta.RootId);
        writer.Write((ulong)(meta.RootId >> 64));
        writer.Write(meta.JournalId);
        writer.Write(meta.NextUsn);
        writer.Write(meta.SourceKey);
        writer.Write(meta.SourceRoot);
        writer.Write(meta.FileSystemType);
        writer.Write(meta.IsComplete);
        writer.Write(meta.ExclusionRulesFingerprint);
        writer.Write(meta.AliasProvidersFingerprint);
        writer.Write(meta.LastUpdated.ToUniversalTime().Ticks);
        // SectionsOffset is derived, not stored: the reader recomputes it from its own stream position
        // after the variable-length strings, then aligns -- see FinishHeader/ReadHeader staying in sync.
    }

    // Pads the stream from the end of the header to the aligned section base and records it in meta.
    public static void FinishHeader(Stream stream, Meta meta)
    {
        meta.SectionsOffset = Align(stream.Position);
        while (stream.Position < meta.SectionsOffset)
            stream.WriteByte(0);
    }

    public static Meta ReadHeader(BinaryReader reader)
    {
        if (reader.ReadUInt64() != Magic)
            throw new InvalidDataException("IndexV2 snapshot magic mismatch");
        if (reader.ReadInt32() != Version)
            throw new InvalidDataException("IndexV2 snapshot version mismatch");

        var meta = new Meta
        {
            RowCount = reader.ReadInt32(),
            UniqueCount = reader.ReadInt32(),
            NameBlobLength = reader.ReadInt32(),
            ChildrenLength = reader.ReadInt32(),
            AliasEntryCount = reader.ReadInt32(),
            AliasBlobLength = reader.ReadInt32(),
            OrphanCount = reader.ReadInt32(),
            TotalFiles = reader.ReadInt32(),
            TotalDirs = reader.ReadInt32(),
            SourceKind = (FileRecordSourceKind)reader.ReadByte(),
            IdKind = (FileRecordIdKind)reader.ReadByte(),
            VolumeSerialNumber = reader.ReadUInt32(),
        };
        var rootLow = reader.ReadUInt64();
        var rootHigh = reader.ReadUInt64();
        meta.RootId = new UInt128(rootHigh, rootLow);
        meta.JournalId = reader.ReadUInt64();
        meta.NextUsn = reader.ReadInt64();
        meta.SourceKey = reader.ReadString();
        meta.SourceRoot = reader.ReadString();
        meta.FileSystemType = reader.ReadString();
        meta.IsComplete = reader.ReadBoolean();
        meta.ExclusionRulesFingerprint = reader.ReadString();
        meta.AliasProvidersFingerprint = reader.ReadString();
        meta.LastUpdated = new DateTime(reader.ReadInt64(), DateTimeKind.Utc).ToLocalTime();
        meta.SectionsOffset = Align(reader.BaseStream.Position);
        return meta;
    }

    // Section offsets derive from meta counts alone, in declaration order, each 16-aligned -- the
    // writer seeks to these positions and the reader maps spans over them, so this method is the only
    // place layout order exists.
    public static long[] ComputeSectionOffsets(Meta meta, out long totalLength)
    {
        ValidateCounts(meta);
        var sizes = new long[]
        {
            4L * meta.RowCount,            // NameIds
            2L * meta.RowCount,            // Flags
            4L * meta.RowCount,            // ParentIndexes
            8L * meta.UniqueCount,         // UniqueMasks
            4L * ((long)meta.UniqueCount + 1),   // NameOffsets
            meta.NameBlobLength,           // NameBlob
            16L * meta.RowCount,           // Ids
            8L * meta.RowCount,            // Sizes
            8L * meta.RowCount,            // RecursiveSizes
            4L * meta.RowCount,            // CreationTimes
            4L * meta.RowCount,            // LastWriteTimes
            4L * meta.RowCount,            // LastAccessTimes
            4L * ((long)meta.RowCount + 1),      // ChildStarts
            4L * meta.ChildrenLength,      // Children
            4L * ((long)meta.UniqueCount + 1),   // UidStarts
            4L * meta.RowCount,            // UidRows
            4L * ((long)meta.UniqueCount + 1),   // AliasStarts
            4L * ((long)meta.AliasEntryCount + 1), // AliasEntryOffsets
            meta.AliasEntryCount,          // AliasProviderIds
            meta.AliasBlobLength,          // AliasBlob
            4L * meta.OrphanCount,         // OrphanRows
            16L * meta.OrphanCount,        // OrphanFrns
            8L * (((long)meta.UniqueCount + 63) / 64), // UniqueAsciiBits
        };

        var offsets = new long[sizes.Length];
        var cursor = meta.SectionsOffset;
        for (var i = 0; i < sizes.Length; i++)
        {
            cursor = Align(cursor);
            offsets[i] = cursor;
            cursor += sizes[i];
        }
        totalLength = Align(cursor);
        return offsets;
    }

    private static void ValidateCounts(Meta meta)
    {
        if (meta.RowCount < 0 || meta.UniqueCount < 0 || meta.NameBlobLength < 0 || meta.ChildrenLength < 0
            || meta.AliasEntryCount < 0 || meta.AliasBlobLength < 0 || meta.OrphanCount < 0
            || meta.TotalFiles < 0 || meta.TotalDirs < 0)
            throw new InvalidDataException("IndexV2 snapshot contains a negative metadata count");
    }

    // Reads just the small fixed+variable header (no mmap) -- for cache-discovery scans that need a
    // summary of every drive's snapshot without paying for a full mapping of each one.
    public static Meta? TryReadHeaderFromFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, NameEncoding);
            return ReadHeader(reader);
        }
        catch (IOException)
        {
            throw; // transient (e.g. mid-write) -- propagate so callers don't mistake this for corruption
        }
        catch (Exception ex)
        {
            Logger.Log($"[SnapshotFormat] Failed to read header {path}: {ex.Message}", LogLevel.Error);
            return null;
        }
    }

    public static long Align(long offset) => (offset + SectionAlignment - 1) & ~(long)(SectionAlignment - 1);

    // The lookup dictionaries live only for the duration of a build; UTF-8 encoding is fixed.
    public static readonly Encoding NameEncoding = Encoding.UTF8;
}
