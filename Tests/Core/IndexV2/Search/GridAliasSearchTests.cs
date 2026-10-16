using System.Text;
using Lertaro.Core.IndexV2.Persistence;
using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Core.Tests.IndexV2.Search;

// End-to-end cover for the reading-grid path: a provider's reading space goes through the real
// SnapshotWriter, is stored as a grid (the encoding a grid-capable provider's readings always take), and
// is then found by the real searcher through the same scoring pipeline a baked string alias uses.
//
// The two fake providers only ever accept names carrying a private-use marker, so registering them
// process-wide (the registry has no unregister) leaves every other test's names untouched.
[TestClass]
public sealed class GridAliasSearchTests
{
    private const char GridMarker = '\uE000';
    private const char StringMarker = '\uE001';

    [ClassInitialize]
    public static void RegisterProviders(TestContext context)
    {
        AliasProviderRegistry.Register(new GridOnlyProvider());
        AliasProviderRegistry.Register(new StringOnlyProvider());
    }

    // Five readings whose initials spell "fdbjf", and a deliberately bulky string encoding so this stands
    // for a name whose spelled-out aliases are what the grid replaces.
    private sealed class GridOnlyProvider : IAliasGridProvider
    {
        private static readonly string[][] Readings =
        {
            new[] { "fang" },
            new[] { "dan", "tan" },
            new[] { "bi" },
            new[] { "ji" },
            new[] { "fa" },
        };

        public bool CanHandle(string text) => text.Contains(GridMarker);

        public IReadOnlyList<(char Start, char End)> InputRanges => new[] { ('a', 'z') };

        public IReadOnlyList<(char Start, char End)> OutputRanges => new[] { ('a', 'z') };

        public char SyllableSeparator => (char)2;

        public string[][] GetReadingSets(string text) => Readings;

        public bool NeedsSeparatorBefore(string text, int index) => index > 0;

        public void GetReadingAliasesUtf8(string text, AliasByteSink dest)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < 64; i++)
            {
                if (i > 0)
                    builder.Append('|');
                builder.Append("fdbjf");
            }
            dest.AddString(builder.ToString());
        }

        public void GetStandaloneAliasesUtf8(string text, AliasByteSink dest)
        {
        }

        // The initials alias is one character per source character, which is the mapping the real
        // provider derives for its own initial-alias shape. Without it the host can decide a grid hit but
        // cannot map it back onto the name, so nothing lights up.
        public int[]? MapAliasToSourceIndices(string text, string alias)
        {
            if (alias.Length != text.Length)
                return null;
            var map = new int[alias.Length];
            for (var i = 0; i < map.Length; i++)
                map[i] = i;
            return map;
        }

        // Deliberately NOT a spelling of the readings this provider can answer: a grid-stored name's live
        // aliases are its lossy enumerated ones, which is exactly the case the highlight tier has to cover
        // without help from a matching string alias.
        public IEnumerable<string> GetAliases(string text) => new[] { "zzz" };
    }

    // The same readings, with a tiny string encoding: still stored as the grid, because the grid is how a
    // grid-capable provider's readings are stored rather than one of two encodings to choose between.
    private sealed class StringOnlyProvider : IAliasGridProvider
    {
        private static readonly string[][] Readings =
        {
            new[] { "fang" },
            new[] { "dan" },
            new[] { "bi" },
            new[] { "ji" },
            new[] { "fa" },
        };

        public bool CanHandle(string text) => text.Contains(StringMarker);

        public IReadOnlyList<(char Start, char End)> InputRanges => new[] { ('a', 'z') };

        public IReadOnlyList<(char Start, char End)> OutputRanges => new[] { ('a', 'z') };

        public char SyllableSeparator => (char)2;

        public string[][] GetReadingSets(string text) => Readings;

        public bool NeedsSeparatorBefore(string text, int index) => index > 0;

        public void GetReadingAliasesUtf8(string text, AliasByteSink dest) => dest.AddString("fdbjf");

        public void GetStandaloneAliasesUtf8(string text, AliasByteSink dest)
        {
        }

        public IEnumerable<string> GetAliases(string text) => new[] { "fdbjf" };
    }

    private static List<AliasEntryKind> KindsOf(LiveIndexFixture fixture)
        => fixture.Index.Read((snapshot, _) =>
        {
            var kinds = new List<AliasEntryKind>();
            for (var uid = 0; uid < snapshot.UniqueCount; uid++)
            {
                var (start, end) = snapshot.AliasEntryRange(uid);
                for (var e = start; e < end; e++)
                    kinds.Add(snapshot.AliasKind(e));
            }
            return kinds;
        });

    [TestMethod]
    public void GridEntry_IsBakedAndFoundWithoutEnumeratingTheReadings()
    {
        using var fixture = LiveIndexFixture.Build("Z", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, GridMarker + "笔记法X", FileRecordFlags.None),
        });

        CollectionAssert.Contains(KindsOf(fixture), AliasEntryKind.Grid);

        var results = new List<SearchResult>();
        IndexV2Searcher.SearchStreaming(fixture.Index, "fdbjf", 10, results.Add, CancellationToken.None);

        CollectionAssert.Contains(results.Select(result => result.Name).ToList(), GridMarker + "笔记法X");
    }

    // The grid tier decides terms from the readings, so the clauses that describe the candidate's own text
    // -- a `/.../` and a `:term` -- need the name handed to it the same way FzfPattern.TryMatchAlias draws
    // that line for the string aliases. A path segment asked about itself passes no name and keeps reading
    // the clauses off its own spelling.
    [TestMethod]
    public void GridEntry_StillRequiresTheClausesThatReadTheName()
    {
        using var fixture = LiveIndexFixture.Build("Z", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, GridMarker + "笔记法X", FileRecordFlags.None),
        });

        CollectionAssert.Contains(KindsOf(fixture), AliasEntryKind.Grid);

        List<string> Search(string query)
        {
            var results = new List<SearchResult>();
            IndexV2Searcher.SearchStreaming(fixture.Index, query, 10, results.Add, CancellationToken.None);
            return results.Select(r => r.Name).ToList();
        }

        // The control: the initials only exist in the grid, so this hit is the grid tier answering.
        CollectionAssert.Contains(Search("fdbjf"), GridMarker + "笔记法X");

        // No text of this name -- and no reading of it -- contains "zz", and a regex clause describes the
        // NAME. The class form is deliberate: with no literal for the character prefilter to reject on,
        // only a matcher that consulted a text can answer it.
        Assert.IsEmpty(Search(@"fdbjf /[z]{2}/"), "a grid hit does not answer a regex clause");

        // The other direction, which is the one the tier used to get wrong: the name really does carry the
        // clause's text while no reading of it does, and refusing the row here meant the clause had been
        // read off the materialized pinyin (see SearchMatcherGridAliases.TryMatchPayload).
        CollectionAssert.Contains(Search(@"fdbjf /X/"), GridMarker + "笔记法X");
        // The name-level veto still applies to a grid hit: no reading carries 笔记, the name does.
        Assert.IsEmpty(Search("fdbjf :笔记"), "a grid hit does not answer an exclusion");
    }

    // The delta overlay generates aliases in memory, so a grid-worthy row reaches search through the
    // GridAlias path rather than a snapshot section. This is the invariant that matters: the same row
    // must answer the same queries before compaction (delta) and after it (baked grid).
    [TestMethod]
    public void DeltaRow_AnswersTheSameQueriesBeforeAndAfterCompaction()
    {
        // Five characters, matching the stub provider's five reading sets -- the grid materializes one
        // character per position, so a shorter name can only ever answer a shorter initials query.
        var name = GridMarker + "筆記法X";
        using var fixture = LiveIndexFixture.Build("Z", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "readme.txt", FileRecordFlags.None),
        });

        fixture.Index.Mutate((snapshot, delta) => delta.Upsert(new UInt128(0, 9), 1, name, FileRecordFlags.None, 0, 0, 0, 0));

        var deltaGrids = fixture.Index.Read((_, delta) => delta.Added.Single(record => record.Name == name).Grids.Length);
        Assert.AreEqual(1, deltaGrids, "the delta row must carry a grid entry");

        // The regex clause is in this list because both halves have to answer it the same way: the delta
        // row decides through SearchMatcherRow and the baked one through the snapshot's alias tiers, and a
        // clause reads the row's own NAME in either.
        string[] Queries = { "fdbjf", "tf", @"fdbjf /X/" };
        var before = Queries.ToDictionary(q => q, q => Search(fixture, q));
        CollectionAssert.Contains(before["fdbjf"], name, "the delta row's grid must be searched, not just its spelled-out aliases");
        CollectionAssert.Contains(before["tf"], name);
        CollectionAssert.Contains(before[@"fdbjf /X/"], name, "a delta row's own name satisfies the clause even though no reading of it does");

        Assert.IsTrue(fixture.Index.Compact(fixture.Path, force: true), "compaction must succeed");

        CollectionAssert.Contains(KindsOf(fixture), AliasEntryKind.Grid, "after compaction the row is baked as a grid");
        foreach (var query in Queries)
            CollectionAssert.AreEqual(before[query], Search(fixture, query), $"'{query}' changed across compaction");
    }

    private static List<string> Search(LiveIndexFixture fixture, string query)
    {
        var results = new List<SearchResult>();
        IndexV2Searcher.SearchStreaming(fixture.Index, query, 10, results.Add, CancellationToken.None);
        return results.Select(result => result.Name).ToList();
    }

    // Regression for the tier that keeps a grid-only hit paintable. The App's item gate reads the final
    // highlight mask, and an empty mask is NoMatch there -- so a name the engine found through its grid
    // would otherwise be dropped before it was ever shown. The provider's string aliases deliberately do
    // not match this query, so only the grid tier can light anything.
    [TestMethod]
    public void HighlightMask_LightsUpAGridOnlyMatch()
    {
        var name = GridMarker + "筆記法X";

        var mask = FuzzyMatcher.ComputeHighlightMask(name, "fdbjf");

        Assert.AreEqual(name.Length, mask.Length);
        Assert.IsTrue(mask.Take(5).All(lit => lit), "the initials tier maps one character per position");
    }

    [TestMethod]
    public void PathMode_FindsAGridAliasInAPathSegment()
    {
        using var fixture = LiveIndexFixture.Build("Z", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, GridMarker + "笔记法X", FileRecordFlags.Directory),
            new FileRecord(3, 2, "report.txt", FileRecordFlags.None),
        });

        var results = new List<SearchResult>();
        IndexV2Searcher.SearchStreaming(fixture.Index, @"fdbjf\report", 10, results.Add, CancellationToken.None);

        CollectionAssert.Contains(KindsOf(fixture), AliasEntryKind.Grid);
        CollectionAssert.Contains(results.Select(result => result.Name).ToList(), "report.txt");
    }

    [TestMethod]
    public void SmallEnumeration_TakesTheGridAsWell()
    {
        using var fixture = LiveIndexFixture.Build("Z", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, StringMarker + "笔记法X", FileRecordFlags.None),
        });

        // The grid is the encoding, whatever the spelled-out form would have cost: that form is the same
        // reading space enumerated and capped, so keeping it because it happens to be smaller would keep a
        // name's own reading out of reach (see AliasGeneration.TryEncodeGrid).
        var kinds = KindsOf(fixture);
        CollectionAssert.Contains(kinds, AliasEntryKind.Grid);

        var results = new List<SearchResult>();
        IndexV2Searcher.SearchStreaming(fixture.Index, "fdbjf", 10, results.Add, CancellationToken.None);

        CollectionAssert.Contains(results.Select(result => result.Name).ToList(), StringMarker + "笔记法X");
    }
}
