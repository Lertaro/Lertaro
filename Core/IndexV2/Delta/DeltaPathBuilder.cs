namespace Lertaro.Core.IndexV2.Delta;

// Split out of DeltaOverlay purely to keep that file under the repo's per-file line limit; this
// builder has no state of its own and always operates on the one DeltaOverlay passed per call.
// DeltaChildLookup deliberately mirrors GetParentPath's resolution order below -- keep them in sync.
internal static class DeltaPathBuilder
{
    // Same Path.Combine chain as Snapshot.GetFullPath, with delta overrides patched in along the walk.
    // Stepping onto a renamed-away row forwards to the FRN's live row (delta or base); with no live
    // row yet, the accumulated tail hangs off the source root, like the old engine's re-orphaned rows.
    public static string GetFullPath(DeltaOverlay o, int baseRow)
    {
        var segments = new List<string>(8);
        var current = baseRow;
        for (var depth = 0; depth < 512; depth++)
        {
            if (o.RenamedAway.TryGetValue(current, out var awayFrn))
            {
                if (o.FindAddedDirectory(awayFrn) is { } liveRecord)
                    return AppendSegments(GetFullPath(o, liveRecord), segments);
                if (o.TryFindLiveBaseDirectory(awayFrn, out var liveRow) && liveRow != current)
                {
                    current = liveRow;
                    continue;
                }
                return AppendSegments(o.Snapshot.SourceRoot, segments);
            }

            var parent = o.ParentOf(current);
            segments.Add(o.NameOf(current));
            if (parent < 0 || parent == current)
                break;
            current = parent;
        }
        return AppendSegments(o.Snapshot.SourceRoot, segments);
    }

    internal static string GetFullPath(DeltaOverlay o, DeltaOverlay.DeltaRecord record)
    {
        // The eagerly-resolved parent row only counts while it is still LIVE -- a tombstoned/renamed-
        // away parent falls through to FRN re-resolution, and with no live heir the record hangs off
        // the source root exactly like a re-orphaned old-engine row.
        var parentPath = GetParentPath(o, record);
        return parentPath[^1] == '\\' ? parentPath + record.Name : parentPath + "\\" + record.Name;
    }

    // The resolved directory path a DeltaRecord's parent link currently points at -- exposed
    // separately from GetFullPath(record) so path-mode search can run its own directory-segment
    // matching against it without reconstructing it via string surgery on the child's full path.
    internal static string GetParentPath(DeltaOverlay o, DeltaOverlay.DeltaRecord record)
    {
        if (record.ParentBaseRow >= 0 && !o.DeletedBase.Contains(record.ParentBaseRow) && !o.RenamedAway.ContainsKey(record.ParentBaseRow))
            return GetFullPath(o, record.ParentBaseRow);
        if (o.TryFindLiveBaseDirectory(record.ParentFrn, out var baseRow))
            return GetFullPath(o, baseRow);
        if (o.FindAddedDirectory(record.ParentFrn) is { } parentRecord)
            return GetFullPath(o, parentRecord);
        return o.Snapshot.SourceRoot;
    }

    private static string AppendSegments(string basePath, List<string> reversedSegments)
    {
        var builder = new System.Text.StringBuilder(basePath, 64);
        for (var i = reversedSegments.Count - 1; i >= 0; i--)
        {
            if (reversedSegments[i].Length == 0)
                continue;
            if (builder[^1] != '\\')
                builder.Append('\\');
            builder.Append(reversedSegments[i]);
        }
        return builder.ToString();
    }
}
