using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.Tests.IndexV2.Search;

// Checks the ancestor pass against an independent statement of what it is supposed to return, over
// randomly shaped trees.
//
// It exists because the pass memoises its answer for a folder and shares it with every folder below,
// which is a real optimisation over a real property (the answer is a union along the chain, and each
// folder's chain is a suffix of its children's) -- but a sharing bug there does not fail loudly. It
// hides results: a file that should have matched is silently dropped, and only for queries that reach
// this pass at all, which is the subset a user is least likely to notice or report.
//
// The reference below is written from the rule, not from the implementation. Deriving it by turning the
// memo off would share every helper with the thing under test, so any mistake inside those helpers
// would agree with itself and pass.
[TestClass]
public sealed class PathTermFallbackRandomTreeTests
{
    // Short, overlapping, ASCII only. Overlap is the point -- terms have to be satisfiable by several
    // different segments so that sharing an answer has something to get wrong. ASCII keeps the alias
    // tier out of it, which the reference does not model.
    private static readonly string[] Words =
    {
        "alpha", "alpine", "album", "beta", "berry", "bench", "gamma", "gamut",
        "delta", "delve", "omega", "omen", "sigma", "signal", "theta", "there",
        // Non-ASCII segments take a different branch through the matcher: the byte path only handles
        // pure-ASCII names, so these are the ones that decode to chars first. Core's tests register no
        // alias provider, so they match literally and the reference below stays honest.
        "报告", "文档", "项目",
    };

    private static readonly string[] Extensions = { ".txt", ".log", ".dat", ".cfg" };

    // Operators change what a term means, and the mask is built from whatever each term decides. An
    // inverse term is the interesting one: it is satisfied by segments that do NOT contain its text,
    // so it fills the mask from the opposite direction to everything else here.
    private static readonly Func<Random, string, string>[] TermShapes =
    {
        (_, word) => word,
        (_, word) => "'" + word,      // exactness flipped
        (_, word) => "^" + word,      // prefix
        (_, word) => word + "$",      // suffix
        (_, word) => "!" + word,      // inverse
    };

    /// <param name="Superseded">Renamed since the snapshot was written, so its live name is delta-only.</param>
    private sealed record Row(UInt128 Id, UInt128 ParentId, string Name, bool IsDirectory, string FullPath,
        bool Superseded = false, bool Deleted = false);

    [TestMethod]
    public void EveryRandomTree_ReturnsExactlyWhatTheRuleSaysItShould()
    {
        // A comparison that finds nothing on both sides passes while testing nothing. These count what
        // was actually put in front of the pass, and are asserted at the end.
        var queriesRun = 0;
        var rowsExpected = 0;
        var rowsNeedingAnAncestor = 0;
        var queriesWithAnOperator = 0;
        var rowsWithANonAsciiSegment = 0;
        var rowsUnderARenamedFolder = 0;

        for (var seed = 1; seed <= 40; seed++)
        {
            var random = new Random(seed);
            var rows = BuildTree(random);
            using var fixture = LiveIndexFixture.Build("T", rows.Select(r =>
                new FileRecord(r.Id, r.ParentId, r.Name,
                    r.IsDirectory ? FileRecordFlags.Directory : FileRecordFlags.None)).Prepend(LiveIndexFixture.Root()));

            // Half the trees are searched as written and half after live changes, so the branch taken
            // when a folder has been renamed -- the one answer the pass is not allowed to share, since
            // it is built from a path string and describes a single chain -- is reached under the same
            // randomisation as everything else.
            if (seed % 2 == 0)
                rows = Mutate(fixture, rows, random);

            foreach (var terms in QueriesFor(random))
            {
                var query = string.Join(' ', terms);
                var actual = Search(fixture, query);
                var expected = Expected(rows, terms, out var viaAncestor);

                queriesRun++;
                rowsExpected += expected.Count;
                rowsNeedingAnAncestor += viaAncestor;
                if (terms.Any(t => "'^!".Contains(t[0]) || t[^1] == '$'))
                    queriesWithAnOperator++;
                rowsWithANonAsciiSegment += expected.Count(p => p.Any(c => c > 127));
                // The ones whose ancestor verdict had to come from the path-string fallback.
                var renamedFolders = rows.Where(r => r.Superseded && r.IsDirectory).Select(r => r.FullPath + "\\").ToList();
                rowsUnderARenamedFolder += expected.Count(p => renamedFolders.Any(f => p.StartsWith(f, StringComparison.Ordinal)));

                CollectionAssert.AreEquivalent(expected.ToList(), actual.ToList(),
                    $"seed {seed}, query \"{query}\"\n" +
                    $"missing: {string.Join(", ", expected.Except(actual))}\n" +
                    $"unexpected: {string.Join(", ", actual.Except(expected))}");
            }
        }

        Assert.IsGreaterThan(200, queriesRun, "not enough queries were generated to be worth anything");
        Assert.IsGreaterThan(200, rowsExpected, "the trees produced almost nothing to match");
        // The ones that only match because a FOLDER supplied a term -- the pass this exists to check.
        // Without them the whole run could be satisfied by plain name search.
        Assert.IsGreaterThan(100, rowsNeedingAnAncestor,
            "no result depended on an ancestor folder, so the ancestor pass was never exercised");
        Assert.IsGreaterThan(30, queriesWithAnOperator, "every term came out plain, so no operator was tested");
        Assert.IsGreaterThan(20, rowsWithANonAsciiSegment,
            "nothing matched through a non-ASCII segment, so the decode branch was never taken");
        Assert.IsGreaterThan(20, rowsUnderARenamedFolder,
            "nothing matched from under a renamed folder, so the one answer the pass may not share was never produced");
    }

    // A chain longer than the walk's own depth guard, which is the second answer the pass may not share.
    // A walk that stops on the guard has seen only part of the chain, and handing that partial answer to
    // a folder further up -- whose own walk would have reached higher -- silently hides everything under
    // it. The files below sit inside the range a truncated walk collects, so they would inherit it.
    [TestMethod]
    public void AChainDeeperThanTheWalkGuard_DoesNotTruncateTheAnswerForFoldersAboveIt()
    {
        const int chainLength = 600;
        var records = new List<FileRecord> { LiveIndexFixture.Root() };
        records.Add(new FileRecord(2, 1, "topmark", FileRecordFlags.Directory));

        UInt128 parent = 2;
        for (var i = 1; i <= chainLength; i++)
        {
            records.Add(new FileRecord((UInt128)(2 + i), parent, "n" + i, FileRecordFlags.Directory));
            parent = (UInt128)(2 + i);
        }

        // One file at each of these depths, named so a single term reaches all of them.
        var depths = new[] { 5, 100, 250, 400, 560, 600 };
        var nextId = (UInt128)(3 + chainLength);
        foreach (var depth in depths)
            records.Add(new FileRecord(nextId++, (UInt128)(2 + depth), $"leaf{depth}.txt", FileRecordFlags.None));

        using var fixture = LiveIndexFixture.Build("T", records);
        var found = Search(fixture, "leaf topmark");

        // Every one of them, whatever order the pass happens to reach them in. This is what the old
        // fixed guard could not promise: a file past it saw a truncated chain, unless a shallower file
        // had already recorded the folders above -- in which case the same file matched after all.
        foreach (var depth in depths)
        {
            Assert.IsTrue(found.Any(p => p.EndsWith($"leaf{depth}.txt", StringComparison.Ordinal)),
                $"the file at depth {depth} should reach \"topmark\"");
        }
    }

    /// <summary>
    /// What the pass promises, stated directly: a row is returned when its own name satisfies at least
    /// one term, and its name together with the folders above it (and the drive root's own segments)
    /// satisfies all of them. Order does not matter -- unlike path mode, these terms carry no position.
    /// </summary>
    private static HashSet<string> Expected(List<Row> rows, string[] terms, out int viaAncestor)
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

    private static int MaskOf(string text, string[] terms)
    {
        var mask = 0;
        for (var i = 0; i < terms.Length; i++)
        {
            if (FuzzyMatcher.IsMatch(terms[i], text))
                mask |= 1 << i;
        }
        return mask;
    }

    private static HashSet<string> Search(LiveIndexFixture fixture, string query)
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
    private static List<Row> Mutate(LiveIndexFixture fixture, List<Row> rows, Random random)
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
    private static List<Row> BuildTree(Random random)
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
    // so more than one term is the whole point.
    private static IEnumerable<string[]> QueriesFor(Random random)
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
                // enough operators mixed in that none of the term kinds goes unexercised.
                var shape = random.Next(3) == 0 ? TermShapes[random.Next(TermShapes.Length)] : TermShapes[0];
                terms[t] = shape(random, text);
            }
            if (terms.Distinct().Count() == terms.Length)
                yield return terms;
        }
    }
}
