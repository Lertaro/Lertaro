using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.Tests.IndexV2.Search;

// The random-tree generator and the independent statement of what the ancestor pass should return. Split
// out of PathTermFallbackRandomTreeTests so both stay under the repository's 300-line limit; see that file
// for why the reference is written from the rule rather than derived from the implementation.
internal static class PathTermFallbackTreeReference
{
    // Short, overlapping, ASCII only. Overlap is the point -- terms have to be satisfiable by several
    // different segments so that sharing an answer has something to get wrong. ASCII keeps the alias
    // tier out of it, which the reference does not model.
    internal static readonly string[] Words =
    {
        "alpha", "alpine", "album", "beta", "berry", "bench", "gamma", "gamut",
        "delta", "delve", "omega", "omen", "sigma", "signal", "theta", "there",
        // Non-ASCII segments take a different branch through the matcher: the byte path only handles
        // pure-ASCII names, so these are the ones that decode to chars first. Core's tests register no
        // alias provider, so they match literally and the reference below stays honest.
        "报告", "文档", "项目",
    };

    internal static readonly string[] Extensions = { ".txt", ".log", ".dat", ".cfg" };

    /// <param name="Superseded">Renamed since the snapshot was written, so its live name is delta-only.</param>
    internal sealed record Row(UInt128 Id, UInt128 ParentId, string Name, bool IsDirectory, string FullPath,
        bool Superseded = false, bool Deleted = false);

    /// <summary>
    /// What the pass promises, stated directly: a row is returned when its own name satisfies at least
    /// one term, and its name together with the folders above it (and the drive root's own segments)
    /// satisfies all of them. Order does not matter -- unlike path mode, these terms carry no position.
    /// </summary>
    internal static HashSet<string> Expected(List<Row> rows, string[] terms, out int viaAncestor)
    {
        var expected = new HashSet<string>(StringComparer.Ordinal);
        var fullMask = (1 << terms.Length) - 1;
        viaAncestor = 0;

        foreach (var row in rows)
        {
            if (row.Deleted)
                continue;

            var nameMask = MaskOf(row.Name, terms);

            // A row renamed since the snapshot was written lives only in delta state. The ancestor pass
            // walks base rows and skips it, so the sole way back is the name search's own delta pass --
            // which matches the WHOLE query against the new name, with no help from any folder.
            if (row.Superseded)
            {
                if (nameMask == fullMask)
                    expected.Add(row.FullPath);
                continue;
            }

            if (nameMask == 0)
                continue;

            var mask = nameMask;
            // Every segment above this row, plus "T:" -- the drive root is a segment the pass offers too.
            var segments = row.FullPath.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < segments.Length - 1; i++)
                mask |= MaskOf(segments[i], terms);

            if (mask != fullMask)
                continue;

            expected.Add(row.FullPath);
            if (nameMask != fullMask)
                viaAncestor++;
        }
        return expected;
    }

    internal static int MaskOf(string text, string[] terms)
    {
        var mask = 0;
        for (var i = 0; i < terms.Length; i++)
        {
            if (FuzzyMatcher.IsMatch(terms[i], text))
                mask |= 1 << i;
        }
        return mask;
    }

    internal static HashSet<string> RunSearch(LiveIndexFixture fixture, string query)
    {
        var results = new HashSet<string>(StringComparer.Ordinal);
        // Far above anything these trees produce, so nothing is lost to the page limit and the
        // comparison is about what matches rather than about ranking.
        IndexV2Searcher.SearchStreaming(fixture.Index, query, 5000, r => results.Add(r.Path), CancellationToken.None);
        return results;
    }

    /// <summary>
    /// Renames some rows and deletes some files, then restates the tree the way it now reads. Only
    /// files are deleted: removing a folder tombstones everything under it, which is a rule of its own
    /// and not what this test is about.
    /// </summary>
    internal static List<Row> Mutate(LiveIndexFixture fixture, List<Row> rows, Random random)
    {
        var renamed = new Dictionary<UInt128, string>();
        var deleted = new HashSet<UInt128>();

        foreach (var row in rows)
        {
            var roll = random.Next(10);
            if (roll == 0)
                renamed[row.Id] = Words[random.Next(Words.Length)] + "-renamed" + random.Next(5) + (row.IsDirectory ? "" : ".txt");
            else if (roll == 1 && !row.IsDirectory)
                deleted.Add(row.Id);
        }

        fixture.Index.Mutate((_, delta) =>
        {
            foreach (var row in rows)
            {
                if (deleted.Contains(row.Id))
                    delta.Remove(row.Id);
                else if (renamed.TryGetValue(row.Id, out var name))
                    delta.Upsert(row.Id, row.ParentId, name,
                        row.IsDirectory ? FileRecordFlags.Directory : FileRecordFlags.None, 0, 0, 0, 0);
            }
        });

        // Paths have to be rebuilt from the live names, since a renamed folder moves everything below it.
        var nameOf = rows.ToDictionary(r => r.Id, r => renamed.TryGetValue(r.Id, out var n) ? n : r.Name);
        var parentOf = rows.ToDictionary(r => r.Id, r => r.ParentId);

        string PathOf(UInt128 id)
        {
            var segments = new List<string>();
            var current = id;
            while (nameOf.ContainsKey(current))
            {
                segments.Add(nameOf[current]);
                current = parentOf[current];
            }
            segments.Reverse();
            return "T:\\" + string.Join('\\', segments);
        }

        return rows.ConvertAll(r => r with
        {
            Name = nameOf[r.Id],
            FullPath = PathOf(r.Id),
            Superseded = renamed.ContainsKey(r.Id),
            Deleted = deleted.Contains(r.Id),
        });
    }

    // Trees deep enough that chains overlap and shallow enough to stay readable when one fails.
    internal static List<Row> BuildTree(Random random)
    {
        var rows = new List<Row>();
        var folders = new List<(UInt128 Id, string Path)> { (1, "T:") };
        var nextId = (UInt128)2;

        var folderCount = random.Next(6, 16);
        for (var i = 0; i < folderCount; i++)
        {
            var (parentId, parentPath) = folders[random.Next(folders.Count)];
            var name = Words[random.Next(Words.Length)] + (random.Next(3) == 0 ? "" : "-" + random.Next(5));
            var path = parentPath + "\\" + name;
            rows.Add(new Row(nextId, parentId, name, true, path));
            folders.Add((nextId, path));
            nextId++;
        }

        var fileCount = random.Next(10, 40);
        for (var i = 0; i < fileCount; i++)
        {
            var (parentId, parentPath) = folders[random.Next(folders.Count)];
            var name = Words[random.Next(Words.Length)] + random.Next(20) + Extensions[random.Next(Extensions.Length)];
            rows.Add(new Row(nextId, parentId, name, false, parentPath + "\\" + name));
            nextId++;
        }

        return rows;
    }

    // Two and three terms: one is never routed here at all, and the mask is what the sharing is about,
    // so more than one term is the whole point. Terms are bare words or word prefixes -- the parser
    // turns no character into an operator any more, so there is no shape left to vary.
    internal static IEnumerable<string[]> QueriesFor(Random random)
    {
        for (var i = 0; i < 12; i++)
        {
            var count = random.Next(2, 4);
            var terms = new string[count];
            for (var t = 0; t < count; t++)
            {
                var word = Words[random.Next(Words.Length)];
                // Sometimes a prefix rather than the whole word, so a term can be satisfied by several
                // different segments at once.
                terms[t] = random.Next(2) == 0 || word.Length < 3 ? word : word[..random.Next(2, word.Length + 1)];
            }
            if (terms.Distinct().Count() == terms.Length)
                yield return terms;
        }
    }
}
