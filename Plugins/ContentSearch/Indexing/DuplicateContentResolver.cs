using System.IO.Hashing;
using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Indexing;

/// <summary>
/// Detects duplicate large files by content hash so the same document found under
/// several paths is parsed once and its text stored once: duplicate rows point at the
/// source row (content_ref) instead of holding their own text and FTS entry.
/// Split out of ContentIndexScheduler to keep that file under the repo's per-file
/// line limit; this class holds no state of its own beyond the database reference.
/// </summary>
public sealed class DuplicateContentResolver
{
    // ponytail: the 10 MB threshold is a user decision, not measured. Hashing costs one
    // sequential read, which only pays off when parsing dominates; small duplicates are
    // cheap enough to parse twice. Upgrade path: make it a config field if real-world
    // corpora disagree.
    public const long HashThresholdBytes = 10 * 1024 * 1024;

    private readonly ContentSearchDatabase _database;

    public DuplicateContentResolver(ContentSearchDatabase database) => _database = database;

    /// <summary>
    /// Streams the file through XxHash-128 and returns the hex digest, or null for files
    /// below the dedup threshold (no hashing) and unreadable files.
    /// </summary>
    public static string? ComputeHashIfLarge(string filePath, long fileLength)
    {
        if (fileLength < HashThresholdBytes)
            return null;

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var hasher = new XxHash128();
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hasher.Append(buffer.AsSpan(0, read));
            }

            return Convert.ToHexString(hasher.GetCurrentHash());
        }
        catch (Exception ex)
        {
            PluginSdk.Logger.Log(
                $"[ContentSearch] Could not hash '{filePath}' for duplicate detection: {ex.Message}",
                PluginSdk.LogLevel.Warn);
            return null;
        }
    }

    /// <summary>
    /// Returns the id of an already-indexed source row holding the same content, or null
    /// when the file is too small to hash or no source is indexed yet.
    /// </summary>
    public long? FindDuplicateSource(string? contentHash, string selfPath) =>
        string.IsNullOrEmpty(contentHash) ? null : _database.FindIndexedSourceByHash(contentHash, selfPath);
}
