using Lertaro.Core.IndexV2;

namespace Lertaro.Core.Indexer.Usn;

// Turning a batch of applied changes into the directories it touched, for
// UsnIndexer.RecordDriveChange. Split out of UsnIndexerExtensions.cs to keep that file under the
// repo's line limit; it is the one part of applying a batch that is about telling subscribers where
// the change was rather than about the index itself.
internal static class UsnIndexerChangedDirectories
{
    /// <summary>
    /// The distinct directories the parents of a batch's records name, or null when the batch cannot
    /// be pinned down because a parent cannot be resolved to a path.
    /// </summary>
    /// <remarks>
    /// Parents rather than the records themselves, because that is the level a watcher is interested
    /// in and the level that survives the change: deleting a file, or a whole directory, leaves the
    /// parent standing and resolvable, while the record's own path is exactly what just went away.
    ///
    /// All or nothing. A parent that fails to resolve is not skipped, because a half-list reads as
    /// the complete set of places that changed, and a subscriber watching the missing one would
    /// conclude it was untouched -- the same false negative the whole mechanism exists to avoid.
    /// Returning null instead makes that batch say "somewhere, unknown", which every reader is
    /// already required to treat as "assume it was you".
    /// </remarks>
    public static List<string>? Resolve(
        LiveIndex live,
        HashSet<UInt128> parentFrns,
        string? drive = null)
    {
        if (parentFrns.Count == 0)
            return new List<string>();

        List<string> directories = new(parentFrns.Count);
        List<UInt128> unresolvedFrns = new();
        live.Read((_, delta) =>
        {
            foreach (var frn in parentFrns)
            {
                if (!delta.TryGetPathForFrn(frn, out var path) || string.IsNullOrEmpty(path))
                {
                    if (delta.TryGetHistoricalPathForFrn(frn, out path))
                    {
                        directories.Add(path);
                        continue;
                    }
                    unresolvedFrns.Add(frn);
                    continue;
                }
                directories.Add(path);
            }
            return true;
        });

        foreach (var frn in unresolvedFrns)
        {
            if (!string.IsNullOrWhiteSpace(drive) && UsnFilePathResolver.TryResolve(drive, frn, out var path))
            {
                directories.Add(path);
                continue;
            }
            return null;
        }

        return directories;
    }

    /// <summary>The directory a single watcher-reported path sits in, as a one-entry batch.</summary>
    /// <remarks>
    /// The path itself when it is a directory: a folder index reports the directory that changed, and
    /// its own parent is a level too coarse. When it is a file, its parent is what changed. Existence
    /// is not checked -- a deleted path is gone by now, and treating it as a file (which is what a
    /// missing extension-less path being taken as a directory would get wrong either way) still names
    /// a directory close enough for a subscriber's containment test to be right.
    /// </remarks>
    public static List<string> ForPath(string path, bool isDirectory)
    {
        var directory = isDirectory ? path : Path.GetDirectoryName(path);
        return string.IsNullOrEmpty(directory)
            ? new List<string>()
            : new List<string> { directory };
    }
}
