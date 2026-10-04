using static Lertaro.Core.Tests.IndexV2.Search.PathTermFallbackTreeReference;

namespace Lertaro.Core.Tests.IndexV2.Search;

// Checks the ancestor pass against an independent statement of what it is supposed to return, over
// randomly shaped trees. The generator and that statement live in PathTermFallbackTreeReference.
//
// It exists because the pass memoises its answer for a folder and shares it with every folder below,
// which is a real optimisation over a real property (the answer is a union along the chain, and each
// folder's chain is a suffix of its children's) -- but a sharing bug there does not fail loudly. It
// hides results: a file that should have matched is silently dropped, and only for queries that reach
// this pass at all, which is the subset a user is least likely to notice or report.
//
// The reference is written from the rule, not from the implementation. Deriving it by turning the
// memo off would share every helper with the thing under test, so any mistake inside those helpers
// would agree with itself and pass.
[TestClass]
public sealed class PathTermFallbackRandomTreeTests
{
    [TestMethod]
    public void EveryRandomTree_ReturnsExactlyWhatTheRuleSaysItShould()
    {
        // A comparison that finds nothing on both sides passes while testing nothing. These count what
        // was actually put in front of the pass, and are asserted at the end.
        var queriesRun = 0;
        var rowsExpected = 0;
        var rowsNeedingAnAncestor = 0;
        var queriesWithExactTerms = 0;
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

            // Fuzzy on and fuzzy off, because that setting is the ONLY thing left that decides a term's
            // kind -- the two take different paths through the matcher, and the exact path is otherwise
            // never reached by this test at all. Restored below: FuzzyMatchEnabled is an AsyncLocal, so
            // leaving it flipped would follow this test's context into whatever runs next.
            try
            {
                foreach (var fuzzy in new[] { true, false })
                {
                    SearchContext.FuzzyMatchEnabled = fuzzy;
                    foreach (var terms in QueriesFor(random))
                    {
                        var query = string.Join(' ', terms);
                        var actual = RunSearch(fixture, query);
                        var expected = Expected(rows, terms, out var viaAncestor);

                        queriesRun++;
                        rowsExpected += expected.Count;
                        rowsNeedingAnAncestor += viaAncestor;
                        if (!fuzzy)
                            queriesWithExactTerms++;
                        rowsWithANonAsciiSegment += expected.Count(p => p.Any(c => c > 127));
                        // The ones whose ancestor verdict had to come from the path-string fallback.
                        var renamedFolders = rows.Where(r => r.Superseded && r.IsDirectory).Select(r => r.FullPath + "\\").ToList();
                        rowsUnderARenamedFolder += expected.Count(p => renamedFolders.Any(f => p.StartsWith(f, StringComparison.Ordinal)));

                        CollectionAssert.AreEquivalent(expected.ToList(), actual.ToList(),
                            $"seed {seed}, fuzzy {fuzzy}, query \"{query}\"\n" +
                            $"missing: {string.Join(", ", expected.Except(actual))}\n" +
                            $"unexpected: {string.Join(", ", actual.Except(expected))}");
                    }
                }
            }
            finally
            {
                SearchContext.FuzzyMatchEnabled = true;
            }
        }

        Assert.IsGreaterThan(200, queriesRun, "not enough queries were generated to be worth anything");
        Assert.IsGreaterThan(200, rowsExpected, "the trees produced almost nothing to match");
        // The ones that only match because a FOLDER supplied a term -- the pass this exists to check.
        // Without them the whole run could be satisfied by plain name search.
        Assert.IsGreaterThan(100, rowsNeedingAnAncestor,
            "no result depended on an ancestor folder, so the ancestor pass was never exercised");
        Assert.IsGreaterThan(30, queriesWithExactTerms, "fuzzy was never switched off, so the exact path went untested");
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
        var found = RunSearch(fixture, "leaf topmark");

        // Every one of them, whatever order the pass happens to reach them in. This is what the old
        // fixed guard could not promise: a file past it saw a truncated chain, unless a shallower file
        // had already recorded the folders above -- in which case the same file matched after all.
        foreach (var depth in depths)
        {
            Assert.IsTrue(found.Any(p => p.EndsWith($"leaf{depth}.txt", StringComparison.Ordinal)),
                $"the file at depth {depth} should reach \"topmark\"");
        }
    }
}
