using System.Runtime.InteropServices;
using Lertaro.Core.SearchIndex.Fzf;

using Lertaro.Core.IndexV2.Alias;

using Lertaro.Core.Indexer.NetworkDrive.Walk;
using Lertaro.Core.SearchIndex;
namespace Lertaro.Core.IndexV2.Persistence;

// Builds a snapshot file straight from a scanner's FileRecordStore -- no RuntimeIndex intermediate.
// Soft-deleted records are folded away, survivors are re-sorted by id (stable by input order for
// hard-link ties), names dedupe into a UTF-8 arena with per-unique masks/aliases, and parent links
// resolve against the sorted id column; unresolved parents keep their true FRN in the orphan section
// so later updates can heal them, exactly like RuntimeIndex's orphan stash.
public static class SnapshotWriter
{
    // Every distinct per-drive cache path gets its own lock object, shared by every caller of Write
    // regardless of which FileRecordStore/LiveIndex instance they hold -- a network drive's periodic scan
    // checkpoint (NetworkIndex.FromStore, a brand-new LiveIndex each time) and its FileSystemWatcher's
    // incremental updates (PublishIncrementalUpdate, running against the drive's PREVIOUS still-live
    // LiveIndex/Snapshot) are two entirely separate objects with no lock in common, so nothing previously
    // stopped both from writing THIS SAME path's ".tmp" file at once. That's a real race, not a rare one:
    // the wider the file (a large network share takes longer to serialize), the longer the window both
    // can be mid-write at the same time, which is exactly what turned "occasionally" into "every time" on
    // large shares -- see the GitHub issue this fixes for the full failure signature (temp file "in use",
    // then File.Replace failing on the final path).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> _pathLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private static object GetPathLock(string path) => _pathLocks.GetOrAdd(path, static _ => new object());

    public static void Write(FileRecordStore store, string path)
    {
        var records = store.Records;
        var order = new List<int>(records.Count);
        for (var i = 0; i < records.Count; i++)
            if (!records[i].IsDeleted)
                order.Add(i);
        order.Sort((a, b) =>
        {
            var c = records[a].Id.CompareTo(records[b].Id);
            return c != 0 ? c : a.CompareTo(b);
        });
        var count = order.Count;

        var nameIds = new uint[count];
        var flags = new ushort[count];
        var parentIndexes = new int[count];
        var ids = new UInt128[count];
        var sizes = new long[count];
        var creation = new uint[count];
        var lastWrite = new uint[count];
        var lastAccess = new uint[count];

        var uidByName = new Dictionary<string, int>(StringComparer.Ordinal);
        var uniqueNames = new List<string>();
        var nameBlob = new MemoryStream();
        var nameOffsets = new List<uint> { 0 };
        var masks = new List<ulong>();
        var aliasStarts = new List<int>();
        var aliasEntryOffsets = new List<uint> { 0 };
        var aliasProviderIds = new List<byte>();
        var aliasBlob = new MemoryStream();
        var totalFiles = 0;
        var totalDirs = 0;

        for (var n = 0; n < count; n++)
        {
            var record = records[order[n]];
            if (!uidByName.TryGetValue(record.Name, out var uid))
            {
                uid = uidByName.Count;
                uidByName[record.Name] = uid;
                uniqueNames.Add(record.Name);
                nameBlob.Write(SnapshotFormat.NameEncoding.GetBytes(record.Name));
                nameOffsets.Add(checked((uint)nameBlob.Length));
            }

            nameIds[n] = (uint)uid;
            flags[n] = (ushort)record.Flags;
            ids[n] = record.Id;
            sizes[n] = record.Size;
            creation[n] = record.CreationTimeUnixSeconds;
            lastWrite[n] = record.LastWriteTimeUnixSeconds;
            lastAccess[n] = record.LastAccessTimeUnixSeconds;
            if (record.IsDirectory)
                totalDirs++;
            else
                totalFiles++;
        }

        // Alias generation is per-name-independent and dominates full-rebuild CPU on CJK-heavy
        // volumes, so it runs as a parallel pre-pass (byte-native via GetAliasesUtf8, no alias
        // strings materialized); the blob writes below stay strictly serial in uid order, so the
        // resulting snapshot bytes are identical to what the old inline serial loop produced.
        var uniqueCount = uniqueNames.Count;
        var uniqueMasks = new ulong[uniqueCount];
        var aliasResults = new AliasGenerationUtf8.Result?[uniqueCount];
        Parallel.For(0, uniqueCount, uid =>
        {
            // Union of name + alias chars: over-admits, never rejects a real match -- the
            // structural equivalent of the old engine bucketing rows under alias chars.
            var mask = FzfAlgorithm.GetCharMask(uniqueNames[uid]);
            aliasResults[uid] = AliasGenerationUtf8.Generate(uniqueNames[uid], ref mask);
            uniqueMasks[uid] = mask;
        });

        for (var uid = 0; uid < uniqueCount; uid++)
        {
            aliasStarts.Add(aliasProviderIds.Count);
            var result = aliasResults[uid];
            if (result != null)
            {
                var res = result.Value;
                var offset = 0;
                for (var s = 0; s < res.SegmentLengths.Length; s++)
                {
                    aliasBlob.Write(res.Bytes, offset, res.SegmentLengths[s]);
                    offset += res.SegmentLengths[s];
                    aliasEntryOffsets.Add(checked((uint)aliasBlob.Length));
                    aliasProviderIds.Add(res.ProviderIds[s]);
                }
            }
            masks.Add(uniqueMasks[uid]);
        }
        aliasStarts.Add(aliasProviderIds.Count);

        // Parent resolution against the sorted id column; unresolved links keep their FRN so a later
        // delta (or the next compaction) can heal them once the parent appears.
        var orphanRows = new List<int>();
        var orphanFrns = new List<UInt128>();
        Dictionary<ulong, int>? recordIndexMap = null;
        for (var n = 0; n < count; n++)
        {
            var record = records[order[n]];
            var parentIndex = -1;
            if (record.ParentId != record.Id)
            {
                parentIndex = SnapshotWriterOps.ResolveParentIndexWithRecordIndexFallback(ids, record.ParentId, ref recordIndexMap);
                if (parentIndex < 0)
                {
                    orphanRows.Add(n);
                    orphanFrns.Add(record.ParentId);
                }
            }
            parentIndexes[n] = parentIndex;
        }

        var childStarts = new int[count + 1];
        for (var n = 0; n < count; n++)
            if (parentIndexes[n] >= 0)
                childStarts[parentIndexes[n] + 1]++;
        for (var n = 0; n < count; n++)
            childStarts[n + 1] += childStarts[n];
        var children = new int[childStarts[count]];
        var childCursor = (int[])childStarts.Clone();
        for (var n = 0; n < count; n++)
            if (parentIndexes[n] >= 0)
                children[childCursor[parentIndexes[n]]++] = n;

        var asciiBits = new ulong[(uidByName.Count + 63) / 64];
        {
            var blob = nameBlob.GetBuffer();
            for (var uid = 0; uid < uidByName.Count; uid++)
            {
                var start = (int)nameOffsets[uid];
                var length = (int)(nameOffsets[uid + 1] - nameOffsets[uid]);
                if (System.Text.Ascii.IsValid(blob.AsSpan(start, length)))
                    asciiBits[uid >> 6] |= 1UL << (uid & 63);
            }
        }

        var uidStarts = new int[uidByName.Count + 1];
        for (var n = 0; n < count; n++)
            uidStarts[nameIds[n] + 1]++;
        for (var n = 0; n < uidByName.Count; n++)
            uidStarts[n + 1] += uidStarts[n];
        var uidRows = new int[count];
        var uidCursor = (int[])uidStarts.Clone();
        for (var n = 0; n < count; n++)
            uidRows[uidCursor[nameIds[n]]++] = n;

        var meta = new SnapshotFormat.Meta
        {
            SourceKey = store.SourceKey,
            SourceRoot = PathHelpers.BuildSourceRoot(store.SourceKey),
            SourceKind = store.SourceKind,
            IdKind = store.IdKind,
            FileSystemType = store.FileSystemType,
            VolumeSerialNumber = store.VolumeSerialNumber,
            RootId = store.RootId,
            JournalId = store.JournalId,
            NextUsn = store.NextUsn,
            RowCount = count,
            UniqueCount = uidByName.Count,
            NameBlobLength = (int)nameBlob.Length,
            ChildrenLength = children.Length,
            AliasEntryCount = aliasProviderIds.Count,
            AliasBlobLength = (int)aliasBlob.Length,
            OrphanCount = orphanRows.Count,
            TotalFiles = totalFiles,
            TotalDirs = totalDirs,
            IsComplete = store.IsComplete,
            ExclusionRulesFingerprint = store.ExclusionRulesFingerprint,
            // Stamped fresh on every write (not carried over from `store`) -- this is exactly the
            // moment every unique name's aliases just got (re)generated above, via whatever alias
            // providers are installed right now.
            AliasProvidersFingerprint = AliasProviderRegistry.ComputeProvidersFingerprint(),
            LastUpdated = store.LastUpdated,
        };

        // FileStream(..., FileMode.Create) never creates missing parent directories itself -- on a truly
        // fresh machine (no prior run has ever written here), %ProgramData%\Lertaro\indexes doesn't
        // exist yet (only Logger.cs creates its own log directory, a separate sibling path), so the very
        // first snapshot write of the session would otherwise fail with DirectoryNotFoundException.
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temp = path + ".tmp";
        // Serializes the temp-write-then-replace sequence per target path -- see GetPathLock's own
        // comment for why two entirely unrelated LiveIndex instances can otherwise both be mid-write to
        // this exact temp file at once.
        lock (GetPathLock(path))
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            {
                using var writer = new BinaryWriter(stream, SnapshotFormat.NameEncoding, leaveOpen: true);
                SnapshotFormat.WriteHeader(writer, meta);
                SnapshotFormat.FinishHeader(stream, meta);
                var offsets = SnapshotFormat.ComputeSectionOffsets(meta, out var totalLength);

                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.NameIds, MemoryMarshal.AsBytes(nameIds));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.Flags, MemoryMarshal.AsBytes(flags));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.ParentIndexes, MemoryMarshal.AsBytes(parentIndexes));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.UniqueMasks, MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(masks)));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.NameOffsets, MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(nameOffsets)));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.NameBlob, nameBlob.GetBuffer().AsSpan(0, (int)nameBlob.Length));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.Ids, MemoryMarshal.AsBytes(ids));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.Sizes, MemoryMarshal.AsBytes(sizes));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.CreationTimes, MemoryMarshal.AsBytes(creation));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.LastWriteTimes, MemoryMarshal.AsBytes(lastWrite));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.LastAccessTimes, MemoryMarshal.AsBytes(lastAccess));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.ChildStarts, MemoryMarshal.AsBytes(childStarts));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.Children, MemoryMarshal.AsBytes(children));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.UidStarts, MemoryMarshal.AsBytes(uidStarts));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.UidRows, MemoryMarshal.AsBytes(uidRows));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.AliasStarts, MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(aliasStarts)));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.AliasEntryOffsets, MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(aliasEntryOffsets)));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.AliasProviderIds, CollectionsMarshal.AsSpan(aliasProviderIds));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.AliasBlob, aliasBlob.GetBuffer().AsSpan(0, (int)aliasBlob.Length));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.OrphanRows, MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(orphanRows)));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.OrphanFrns, MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(orphanFrns)));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.UniqueAsciiBits, MemoryMarshal.AsBytes(asciiBits));
                stream.SetLength(totalLength);
            }

            FileRecordStoreReplaceHelper.ReplaceWithRetry(temp, path, SnapshotWriterOps.TryDelete);
        }
    }

    internal static int FirstRowForId(UInt128[] ids, UInt128 id) => SnapshotWriterOps.FirstRowForId(ids, id);
}
