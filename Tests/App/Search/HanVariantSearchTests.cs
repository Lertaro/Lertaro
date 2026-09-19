using System.IO;
using Lertaro.Core;
using Lertaro.Core.IndexV2;
using Lertaro.Core.IndexV2.Persistence;
using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.Search;

// Traditional/Simplified matching END TO END: a provider's conversion has to survive two real hops, the
// snapshot bake (SnapshotWriter -> AliasGenerationUtf8 -> IAliasProvider.GetAliasesUtf8) and the query
// (IndexV2Searcher -> FzfPatternParser -> IAliasProvider.GetQueryForms). Both are host code the
// provider's own unit tests never run, and either hop alone is enough to make "电脑" miss "電腦.txt":
// without the baked alias the Simplified spelling is not in the index, and without the query form a
// Traditional term is never offered the Simplified spelling of itself.
//
// The conversion itself (HanConversion/LCMapStringEx and the length-changing character pairs) is covered
// in Tests/Plugins/PinyinAlias, including its alias-position mapping; that is also why the provider here
// is a fake. The real one needs a pinyin engine and plugin settings, neither of which has anything to do
// with the two directions under test -- and a fake keeps the expected spellings fixed instead of tied to
// what the syllable table happens to produce.
//
// App test project for the usual reason: Core's tests deliberately run with no alias provider
// registered, and a plugin test project must not reference Core at all.
[TestClass]
[DoNotParallelize] // registers into a process-wide registry and flips the process-wide fuzzy default
public sealed class HanVariantSearchTests
{
    private const char Sep = (char)2;

    // Stands in for the pinyin provider's Simplified support only: no pinyin aliases at all, so a failure
    // here can only come from the Traditional/Simplified hop.
    private sealed class FakeHanVariantProvider : IAliasProvider
    {
        // Traditional -> Simplified, the one direction a table can express: a Simplified QUERY has to
        // reach a Traditional NAME. The reverse direction needs no table at all -- the Simplified name is
        // already the literal text the Traditional query is rewritten into.
        private static readonly Dictionary<char, char> Simplified = new()
        {
            ['電'] = '电',
            ['腦'] = '脑',
            ['軟'] = '软',
            ['體'] = '体',
        };

        public string Name => "FakeHanVariant";
        public IReadOnlyList<(char Start, char End)> InputRanges { get; } = new[] { ('一', '鿿') };
        public IReadOnlyList<(char Start, char End)> OutputRanges { get; } = new[] { ('a', 'z') };

        // Declared because the real provider declares one, and because the Simplified alias is the same
        // flat, structure-free shape there: it is CJK in a provider whose output range is "pinyin
        // letters", and it must still match from the start of its own alias segment.
        public char SyllableSeparator => Sep;

        public bool CanHandle(string text) =>
            text.Any(Simplified.ContainsKey) || text.Any(Simplified.ContainsValue);

        // One flat alias: the Simplified spelling of the whole name, which is what a Simplified query
        // then reaches. Emitted as its own segment -- appending it to a pinyin alias would push it off
        // index 0, where the precise-mode boundary rule would refuse to start a match.
        public IEnumerable<string> GetAliases(string text)
        {
            var converted = ToSimplified(text);
            if (!string.Equals(converted, text, StringComparison.Ordinal))
                yield return converted;
        }

        public IEnumerable<string> GetQueryForms(string term)
        {
            var converted = ToSimplified(term);
            if (!string.Equals(converted, term, StringComparison.Ordinal))
                yield return converted;
        }

        // Length-preserving, unlike the real converter (one Traditional character can become two
        // Simplified ones). Matching does not care -- only the highlight mask does, and that mapping is
        // tested where it lives.
        private static string ToSimplified(string text)
        {
            var chars = text.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (Simplified.TryGetValue(chars[i], out var simplified))
                    chars[i] = simplified;
            }
            return new string(chars);
        }
    }

    // A real snapshot on disk, opened the way production opens it, so the bake path under test is the
    // real one. Only public API is used (SnapshotWriter.Write / Snapshot.Open / LiveIndex).
    private sealed class TempIndex : IDisposable
    {
        private readonly string _tempDir;

        public LiveIndex Index { get; }

        private TempIndex(string tempDir, LiveIndex index)
        {
            _tempDir = tempDir;
            Index = index;
        }

        public static TempIndex Build(params string[] fileNames)
        {
            var tempDir = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;
            var path = Path.Combine(tempDir, "test.idx");

            var store = new FileRecordStore
            {
                SourceKey = "Z",
                SourceKind = FileRecordSourceKind.LocalMft,
                IdKind = FileRecordIdKind.MftFrn,
                RootId = 1,
            };
            // The conventional self-parented root row every fixture needs at id 1.
            store.Records.Add(new FileRecord(1, 1, "", FileRecordFlags.Directory | FileRecordFlags.SourceRoot));
            for (var i = 0; i < fileNames.Length; i++)
                store.Records.Add(new FileRecord((UInt128)(i + 2), 1, fileNames[i], FileRecordFlags.None));

            SnapshotWriter.Write(store, path);
            return new TempIndex(tempDir, new LiveIndex(Snapshot.Open(path)));
        }

        public void Dispose()
        {
            Index.Dispose();
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    private static bool _registered;

    [TestInitialize]
    public void Setup()
    {
        // Registered once for the process: the registry has no unregister, and a second registration
        // would bake every name twice.
        if (!_registered)
        {
            AliasProviderRegistry.Register(new FakeHanVariantProvider());
            _registered = true;
        }
    }

    private static List<SearchResult> Search(LiveIndex index, string query)
    {
        var results = new List<SearchResult>();
        IndexV2Searcher.SearchStreaming(index, query, 10, results.Add, CancellationToken.None);
        return results;
    }

    // The direction that needs the baked alias: nothing in the query's own text appears in the name.
    [TestMethod]
    public void SimplifiedQuery_FindsTheTraditionalName()
    {
        SearchContext.FuzzyMatchEnabled = false;
        try
        {
            using var fixture = TempIndex.Build("電腦.txt", "电脑.txt");
            var results = Search(fixture.Index, "电脑");
            var names = results.Select(r => r.Name).ToList();

            // The literal hit is the control: it proves the search itself ran, so the assertion below
            // fails only when the Simplified alias is missing from the bake.
            Assert.Contains("电脑.txt", names);
            Assert.Contains("電腦.txt", names);
        }
        finally
        {
            SearchContext.FuzzyMatchEnabled = true;
        }
    }

    // The direction that needs the query form: the Traditional term must be offered its Simplified
    // spelling, because the Simplified NAME carries no Traditional alias of its own (there is nothing to
    // convert that would change it).
    [TestMethod]
    public void TraditionalQuery_FindsTheSimplifiedName()
    {
        SearchContext.FuzzyMatchEnabled = false;
        try
        {
            using var fixture = TempIndex.Build("電腦.txt", "电脑.txt");
            var results = Search(fixture.Index, "電腦");
            var names = results.Select(r => r.Name).ToList();

            Assert.Contains("電腦.txt", names);
            Assert.Contains("电脑.txt", names);
        }
        finally
        {
            SearchContext.FuzzyMatchEnabled = true;
        }
    }

    // Conversion is not a precise-mode-only trick: fuzzy is the default mode, and it must not lose the
    // extra spellings the precise path gains.
    [TestMethod]
    public void FuzzySearch_AlsoCrossesTheTwoSpellings()
    {
        using var fixture = TempIndex.Build("電腦.txt", "电脑.txt");

        Assert.IsTrue(Search(fixture.Index, "电脑").Any(r => r.Name == "電腦.txt"));
        Assert.IsTrue(Search(fixture.Index, "電腦").Any(r => r.Name == "电脑.txt"));
    }

    // A Traditional query must not start reaching sounds it does not have: the Simplified alias is one
    // more spelling of the same name, not a wildcard.
    [TestMethod]
    public void UnrelatedTraditionalQuery_DoesNotMatch()
    {
        using var fixture = TempIndex.Build("電腦.txt", "电脑.txt");

        Assert.IsEmpty(Search(fixture.Index, "軟體"));
    }

    // The OTHER real call shape, and the one an application result takes: a plugin-provided catalog item
    // (Start Menu / desktop / a custom app search folder) is matched as a title plus whatever aliases the
    // host baked for it -- there is no snapshot and no record, so nothing here can fall back on the bake
    // the tests above exercise. See SearchableItemMapper.CollectSearchableItemResults.
    //
    // This is the reported bug: the Traditional query matched the Simplified TITLE (the provider's query
    // form did its job) but produced no highlight mask, because the mask skipped provider-supplied forms
    // and the aliases baked for a Simplified candidate contain no Traditional spelling to map back from.
    // BestMatch is the gate SearchableItemMapper uses, and it reads a rank, not a match -- so the
    // application row vanished while the same query still listed a same-named FILE, whose engine gates on
    // the match itself.
    [TestMethod]
    public void TraditionalQuery_FindsASimplifiedItemTitleThroughItsQueryForm()
    {
        var match = FuzzyQuery.Parse("電腦").BestMatch("电脑云", null);

        Assert.IsTrue(match.IsMatch, "the provider's Simplified spelling of the query must count as a match here too");
    }

    // The item path's other direction, through the aliases the host bakes from GetAliases: a Traditional
    // TITLE with a Simplified query has no query form to lean on, so the item's own alias set is what has
    // to carry it. (This direction never had the bug -- the alias is a CJK string, and
    // AliasHighlightMarker maps it back onto the source text.)
    [TestMethod]
    public void SimplifiedQuery_FindsATraditionalItemTitleThroughItsBakedAlias()
    {
        var aliases = new FakeHanVariantProvider().GetAliases("電腦云").ToList();

        Assert.IsFalse(FuzzyQuery.Parse("电脑").BestMatch("電腦云", null).IsMatch,
            "the control: nothing in the Simplified query is in the Traditional title");
        Assert.IsTrue(FuzzyQuery.Parse("电脑").BestMatch("電腦云", aliases).IsMatch);
    }

    // The mask the fix paints is the user-visible half: the characters the provider's spelling matched
    // must light up, exactly as they do when the query is the one literally present.
    [TestMethod]
    public void TraditionalQuery_HighlightsTheCharactersItsSimplifiedFormMatched()
    {
        var query = FuzzyQuery.Parse("電腦");
        const string title = "网易电脑管家";

        var mask = query.HighlightMask(title);

        CollectionAssert.AreEqual(
            new[] { false, false, true, true, false, false },
            mask,
            "电脑 sits at index 2 in 网易电脑管家 and is what the query form matched");
    }
}
