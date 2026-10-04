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

    // Operators change what a term means, and the mask is built from whatever each term decides. The
    // exclusion is the interesting one: it is satisfied by segments that do NOT contain its text, so it
    // fills the mask from the opposite direction to everything else here -- and, unlike the rest, it is
    // also a veto on the row's own name, which no folder above can hand it (see Excludes).
    //
    // ':' and '?' are the only triggers the grammar has left; the historical '! "'" '^' '$' shapes became
    // ordinary characters when the query grammar moved to trigger characters (see TermTriggers).
    internal static readonly Func<Random, string, string>[] TermShapes =
    {
        (_, word) => word,
        (_, word) => "?" + word,  // precision flipped against the fuzzy setting
        (_, word) => ":" + word,  // exclusion
    };

    /// <param name="Superseded">Renamed since the snapshot was written, so its live name is delta-only.</param>
    internal sealed record Row(UInt128 Id, UInt128 ParentId, string Name, bool IsDirectory, string FullPath,
        bool Superseded = false, bool Deleted = false);

    /// <summary>
    /// What the pass promises, stated directly: a row is returned when its own name satisfies at least
    /// one term, its name together with the folders above it (and the drive root's own segments)
    /// satisfies all of them, and none of its <c>:terms</c> occurs in its own name. Order does not matter
    /// -- unlike path mode, these terms carry no position.
    /// </summary>
    internal static HashSet<string> Expected(List<Row> rows, string[] terms, out int viaAncestor)
    {
        var expected = new HashSet<string>(StringComparer.Ordinal);
        var fullMask = (1 << terms.Length) - 1;
        viaAncestor = 0;

        // A query that is nothing but exclusions has no positive term left to match ON, so it answers with
        // nothing rather than with everything (see FzfPattern.HasPositiveTerm) -- and no folder above can
        // supply the missing positive either.
        if (terms.All(t => t.Length > 0 && t[0] == ':'))
            return expected;

        foreach (var row in rows)
        {
            if (row.Deleted)
                continue;

            var nameMask = MaskOf(row.Name, terms);

            // `:term` is not a term a segment can satisfy the way the others are: it drops any row whose
            // OWN NAME carries the excluded text, and says nothing about the folders above. Reading it as
            // an ordinary term -- any text without it passes, so some ancestor fills its bit -- is exactly
            // what let `文档 :文档` list the 文档 rows the user had just excluded, through the folder above.
            if (Excludes(row.Name, terms))
                continue;

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
            if (SatisfiedBy(terms[i], text))
                mask |= 1 << i;
        }
        return mask;
    }

    // One term's bit. An exclusion reads for ABSENCE, and the matcher seam cannot answer that here: a
    // pattern made of nothing but a `:term` has no positive term to match on, so it matches nothing at all
    // (see FzfPattern.HasPositiveTerm), while this pass fills the bit from any segment that does not carry
    // the excluded text.
    private static bool SatisfiedBy(string term, string text) =>
        term.Length > 1 && term[0] == ':'
            ? !text.Contains(term[1..], StringComparison.OrdinalIgnoreCase)
            : FuzzyMatcher.IsMatch(term, text);

    // The exclusion veto, stated the way the engine applies it: only the row's own name can carry the
    // excluded text, and only the name is checked for it. Every generated exclusion is a bare `:word`,
    // so a plain case-insensitive contains is the whole rule here.
    private static bool Excludes(string name, string[] terms) =>
        terms.Any(t => t.Length > 1 && t[0] == ':' && name.Contains(t[1..], StringComparison.OrdinalIgnoreCase));

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
    // so more than one term is the whole point. Terms are bare words or word prefixes, sometimes with
    // one of the two remaining triggers in front of them (see TermShapes).
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
                var text = random.Next(2) == 0 || word.Length < 3 ? word : word[..random.Next(2, word.Length + 1)];
                // Mostly plain, because that is what the ancestor mask is normally built from, but with
                // enough triggers mixed in that neither term kind goes unexercised.
                var shape = random.Next(3) == 0 ? TermShapes[random.Next(TermShapes.Length)] : TermShapes[0];
                terms[t] = shape(random, text);
            }
            if (terms.Distinct().Count() == terms.Length)
                yield return terms;
        }
    }
}
