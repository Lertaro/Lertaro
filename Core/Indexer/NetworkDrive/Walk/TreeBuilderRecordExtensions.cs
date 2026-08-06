namespace Lertaro.Core.Indexer.NetworkDrive.Walk;

// Per-entry record creation and error counting for TreeBuilder, as extension methods (matching
// RuntimeIndex's BucketExtensions/QueryExtensions split) instead of a partial class, to keep
// TreeBuilder.cs under the project's line limit.
internal static class TreeBuilderRecordExtensions
{
    public static WalkRecordResult TryCreateRecord(this TreeBuilder builder, string child, string logicalParentPath, UInt128 parentId, out NetworkWalkRecord record, out bool isDirectory, out string fullPath)
    {
        record = default;
        isDirectory = false;
        fullPath = string.Empty;

        FileInfo info;
        FileAttributes attributes;
        try
        {
            info = new FileInfo(child);
            attributes = info.Attributes;
        }
        catch
        {
            return WalkRecordResult.AttributeError;
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
            return WalkRecordResult.ReparsePoint;

        isDirectory = (attributes & FileAttributes.Directory) != 0;
        var name = Path.GetFileName(child.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(name))
            return WalkRecordResult.InvalidName;

        var logicalPath = Path.Combine(logicalParentPath, name);
        fullPath = PathHelpers.NormalizePath(logicalPath, isDirectory);
        var id = PathHelpers.HashPath64(fullPath);
        var flags = FileRecordFlagsHelper.FromAttributes(attributes);
        // Length/timestamps can still throw on a flaky network share even though Attributes just
        // succeeded -- e.g. a real crash seen in the wild: LastAccessTimeUtc throwing
        // ArgumentOutOfRangeException("Not a valid Win32 FileTime") for a share that reports a
        // timestamp .NET can't represent. All of this is supplementary metadata, not worth failing
        // the whole record over.
        long size = 0;
        if (!isDirectory)
        {
            try { size = info.Length; } catch { }
        }
        uint creationUtc = 0, lastWriteUtc = 0, lastAccessUtc = 0;
        try { creationUtc = FileTimeHelper.ToUnixSeconds(info.CreationTimeUtc); } catch { }
        try { lastWriteUtc = FileTimeHelper.ToUnixSeconds(info.LastWriteTimeUtc); } catch { }
        try { lastAccessUtc = FileTimeHelper.ToUnixSeconds(info.LastAccessTimeUtc); } catch { }
        var fileRecord = new FileRecord(
            id,
            parentId,
            builder._namePool.Get(name),
            flags,
            size,
            creationUtc,
            lastWriteUtc,
            lastAccessUtc);
        record = new NetworkWalkRecord(fileRecord, attributes);
        return WalkRecordResult.Success;
    }

    public static void CountCreateFailure(this TreeBuilder builder, WalkRecordResult result)
    {
        switch (result)
        {
            case WalkRecordResult.AttributeError:
                builder.CountError(ref builder._attributeErrors);
                break;
            case WalkRecordResult.ReparsePoint:
                Interlocked.Increment(ref builder._reparseSkipped);
                Interlocked.Increment(ref builder._skippedItems);
                break;
            default:
                Interlocked.Increment(ref builder._skippedItems);
                break;
        }
    }

    public static void CountError(this TreeBuilder builder, ref int counter)
    {
        Interlocked.Increment(ref counter);
        Interlocked.Increment(ref builder._errors);
    }
}
