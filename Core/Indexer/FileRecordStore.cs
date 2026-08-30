namespace Lertaro.Core;

public enum FileRecordSourceKind : byte
{
    LocalMft = 1,
    NetworkMappedDrive = 2
}

public enum FileRecordIdKind : byte
{
    MftFrn = 1,
    SourceLocalId64 = 2
}

[Flags]
public enum FileRecordFlags : ushort
{
    None = 0,
    Directory = 1,
    Deleted = 2,
    SourceRoot = 4,
    Hidden = 8,
    System = 16,
    ReadOnly = 32,
    Compressed = 64,
    Encrypted = 128,
    ReparsePoint = 512,
    // Directories only: this directory's own children were fully enumerated as of this snapshot, so a
    // later diff-aware walk (see TreeDiffBaseline) can trust its cached children when the directory's own
    // LastWriteTimeUnixSeconds still matches -- rather than trusting cached children for any directory
    // record that merely exists (which could be a directory only ever discovered, never actually listed,
    // e.g. one whose walk never got to run before a scan was interrupted).
    Listed = 256
}

public static class FileRecordFlagsHelper
{
    public static FileRecordFlags FromAttributes(FileAttributes attrs)
    {
        var flags = FileRecordFlags.None;
        if ((attrs & FileAttributes.Directory) != 0) flags |= FileRecordFlags.Directory;
        if ((attrs & FileAttributes.Hidden) != 0) flags |= FileRecordFlags.Hidden;
        if ((attrs & FileAttributes.System) != 0) flags |= FileRecordFlags.System;
        if ((attrs & FileAttributes.ReadOnly) != 0) flags |= FileRecordFlags.ReadOnly;
        if ((attrs & FileAttributes.Compressed) != 0) flags |= FileRecordFlags.Compressed;
        if ((attrs & FileAttributes.Encrypted) != 0) flags |= FileRecordFlags.Encrypted;
        if ((attrs & FileAttributes.ReparsePoint) != 0) flags |= FileRecordFlags.ReparsePoint;
        return flags;
    }

    public static FileAttributes ToAttributes(FileRecordFlags flags)
    {
        var attrs = (FileAttributes)0;
        if ((flags & FileRecordFlags.Directory) != 0) attrs |= FileAttributes.Directory;
        if ((flags & FileRecordFlags.Hidden) != 0) attrs |= FileAttributes.Hidden;
        if ((flags & FileRecordFlags.System) != 0) attrs |= FileAttributes.System;
        if ((flags & FileRecordFlags.ReadOnly) != 0) attrs |= FileAttributes.ReadOnly;
        if ((flags & FileRecordFlags.Compressed) != 0) attrs |= FileAttributes.Compressed;
        if ((flags & FileRecordFlags.Encrypted) != 0) attrs |= FileAttributes.Encrypted;
        if ((flags & FileRecordFlags.ReparsePoint) != 0) attrs |= FileAttributes.ReparsePoint;
        if (attrs == 0) attrs = FileAttributes.Normal;
        return attrs;
    }
}

// Converts the platform's native FILETIME/DateTime timestamps (100ns resolution, since 1601) down to
// whole-second Unix time (uint, since 1970). A search tool has no use for sub-second precision, and
// halving each stored timestamp from 8 to 4 bytes matters across millions of indexed rows. Range is
// 1970-01-01 through 2106-02-07; anything outside that (no real file should be) clamps to the nearest end.
public static class FileTimeHelper
{
    private static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static uint ToUnixSeconds(DateTime utc)
    {
        var seconds = (utc - UnixEpoch).TotalSeconds;
        return seconds <= 0 ? 0u : seconds >= uint.MaxValue ? uint.MaxValue : (uint)seconds;
    }

    // 0 means "not recorded" throughout the index (see ToUnixSeconds' clamp), not a real 1970 timestamp.
    public static DateTime FromUnixSeconds(uint unixSeconds) => unixSeconds == 0 ? DateTime.MinValue : UnixEpoch.AddSeconds(unixSeconds);

    // For MFT/ReFS scanners, which read a raw FILETIME long straight out of an on-disk buffer.
    public static uint FileTimeToUnixSeconds(long fileTimeUtc)
    {
        if (fileTimeUtc <= 0)
            return 0;
        try
        {
            return ToUnixSeconds(DateTime.FromFileTimeUtc(fileTimeUtc));
        }
        catch (ArgumentOutOfRangeException)
        {
            return 0;
        }
    }

    // Best-effort mtime stat for a root record, defaulting to 0 ("not recorded") on any failure -- shared
    // by every walk/scan pipeline's root-record construction (NetworkIndex.Build, LocalDriveWalkBuilder.Build,
    // ReFsScanner.ScanDrive, IndexCacheManager.CreateEmptyStore). A real value here matters: without it,
    // TreeDiffBaseline can never match the root against a live check on a later resume, permanently
    // forcing every top-level entry to be re-listed no matter how unchanged they actually are.
    public static uint TryGetLastWriteTimeUnixSeconds(string path)
    {
        try { return ToUnixSeconds(Directory.GetLastWriteTimeUtc(path)); }
        catch { return 0; }
    }
}

public readonly struct FileRecord
{
    public FileRecord(
        UInt128 id,
        UInt128 parentId,
        string name,
        FileRecordFlags flags,
        long size = 0,
        uint creationTimeUnixSeconds = 0,
        uint lastWriteTimeUnixSeconds = 0,
        uint lastAccessTimeUnixSeconds = 0)
    {
        Id = id;
        ParentId = parentId;
        Name = name;
        Flags = flags;
        Size = size;
        CreationTimeUnixSeconds = creationTimeUnixSeconds;
        LastWriteTimeUnixSeconds = lastWriteTimeUnixSeconds;
        LastAccessTimeUnixSeconds = lastAccessTimeUnixSeconds;
    }

    public UInt128 Id { get; }
    public UInt128 ParentId { get; }
    public string Name { get; }
    public FileRecordFlags Flags { get; }
    // Logical (apparent) size in bytes. Always 0 for directories.
    public long Size { get; }
    // Whole-second Unix time (UTC). See FileTimeHelper for the precision/range trade-off.
    public uint CreationTimeUnixSeconds { get; }
    public uint LastWriteTimeUnixSeconds { get; }
    public uint LastAccessTimeUnixSeconds { get; }
    public bool IsDirectory => (Flags & FileRecordFlags.Directory) != 0;
    public bool IsDeleted => (Flags & FileRecordFlags.Deleted) != 0;
}

public sealed class FileRecordStore
{
    public string SourceKey { get; set; } = string.Empty;
    public FileRecordSourceKind SourceKind { get; set; }
    public FileRecordIdKind IdKind { get; set; }
    public string FileSystemType { get; set; } = string.Empty;
    public uint VolumeSerialNumber { get; set; }
    public UInt128 RootId { get; set; }
    public ulong JournalId { get; set; }
    public long NextUsn { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.Now;
    // False for a mid-walk checkpoint or an interrupted scan; true only once the walk that produced this
    // store finished in full. Consulted at startup (network/WSL drives only) to decide whether a saved
    // cache can be trusted as-is or the scan needs to resume/finish first -- see TreeDiffBaseline for how
    // a resumed scan still reuses whatever this store's directories already recorded as FileRecordFlags.Listed.
    public bool IsComplete { get; set; }
    // A hash of the global exclusion settings (ExcludedPaths/IgnoredPathGlobs/IgnoredPathRegexes) as of the
    // walk that produced this store -- lets a resumed walk tell whether exclusion rules changed since then
    // without needing any external signal (see TreeDiffBaseline / TreeBuilder's recheckExclusions). Only
    // meaningful for NetworkMappedDrive stores; a local MFT/USN store never sets it.
    public string ExclusionRulesFingerprint { get; set; } = string.Empty;
    public List<FileRecord> Records { get; } = new();
}
