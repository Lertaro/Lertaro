using System.IO;
using System.Text;
using Lertaro.Core;
using Lertaro.Core.IndexV2;
using Lertaro.Core.IndexV2.Persistence;
using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.Search;

// The reported bug, end to end through the real search: "zghsy :演唱会" (pinyin initials of 中国好声音
// plus an exclusion) returned F:\Downloads\中国好声音演唱会.txt.
//
// Nothing in the query's positive term appears in the CJK name, so the hit can only come from the alias
// the provider baked at index time -- and the exclusion can only be evaluated against the name, because
// the alias is pinyin. The tier that answered with the alias therefore had to be told which of the two
// strings it is holding; it used to match the whole pattern against the alias, where "演唱会" is absent
// by construction, so the exclusion passed for every candidate. See FzfPattern.TryMatchAlias.
//
// App test project for the usual reason (same as HanVariantSearchTests): the bake path needs a provider
// registered process-wide, and Core's own tests deliberately run without one.
[TestClass]
[DoNotParallelize]
public sealed class ExclusionAliasSearchTests
{
    // Initials of every character the fixture's names are built from, so the baked alias has exactly the
    // shape the pinyin provider produces (中国好声音演唱会 -> zghsyych). A fake rather than the real
    // provider: the pinyin table has its own tests, and this one is about which STRING an exclusion is
    // allowed to read.
    private sealed class FakeInitialsProvider : IAliasProvider
    {
        private static readonly Dictionary<char, char> Initials = new()
        {
            ['中'] = 'z', ['国'] = 'g', ['好'] = 'h', ['声'] = 's', ['音'] = 'y',
            ['演'] = 'y', ['唱'] = 'c', ['会'] = 'h', ['采'] = 'c', ['访'] = 'f',
        };

        public string Name => "FakeInitials";
        public IReadOnlyList<(char Start, char End)> InputRanges { get; } = new[] { ('一', '鿿') };
        public IReadOnlyList<(char Start, char End)> OutputRanges { get; } = new[] { ('a', 'z') };

        public bool CanHandle(string text) =>
            // Deliberately narrow rather than "any CJK character": this provider is registered for the
            // whole App test process, and a name it claimed would silently gain an alias in every other
            // test's snapshot too (AliasRankingTests' 好爱哦耶.mp3 shares 好 with this fixture).
            text.Contains("中国好声音", StringComparison.Ordinal) || text.Contains("zghsy", StringComparison.Ordinal);

        // One flat initials alias for the whole name, non-CJK characters passed through as they are --
        // the message a query typed as initials reaches.
        public IEnumerable<string> GetAliases(string text)
        {
            var alias = new StringBuilder(text.Length);
            foreach (var c in text)
                alias.Append(Initials.TryGetValue(c, out var initial) ? initial : c);
            yield return alias.ToString();
        }
    }

    // A real snapshot on disk, opened the way production opens it, so the bake path under test is the
    // real one (SnapshotWriter -> AliasGenerationUtf8 -> the provider).
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
                SourceKey = "T",
                SourceKind = FileRecordSourceKind.LocalMft,
                IdKind = FileRecordIdKind.MftFrn,
                RootId = 1,
            };
            store.Records.Add(new FileRecord(1, 1, "", FileRecordFlags.Directory | FileRecordFlags.SourceRoot));
            for (var i = 0; i < fileNames.Length; i++)
                store.Records.Add(new FileRecord((UInt128)(i + 2), 1, fileNames[i], FileRecordFlags.None));

            SnapshotWriter.Write(store, path);
            return new TempIndex(tempDir, new LiveIndex(Snapshot.Open(path)));
        }

        public List<string> Search(string query)
        {
            var results = new List<SearchResult>();
            IndexV2Searcher.SearchStreaming(Index, query, 10, results.Add, CancellationToken.None);
            return results.Select(r => r.Name).ToList();
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
            AliasProviderRegistry.Register(new FakeInitialsProvider());
            _registered = true;
        }
    }

    [TestMethod]
    public void InitialsQuery_ReachesBothNamesThroughTheirBakedAliases()
    {
        // The control for the tests below: nothing in "zghsy" is in either CJK name, so these hits can
        // only be the baked aliases (中国好声音采访 -> "zghsycf..." too -- 音 is the fifth character of
        // 好声音, not of the 采访 suffix). Without it, a name missing below would prove nothing.
        using var fixture = TempIndex.Build("中国好声音演唱会.txt", "中国好声音采访.txt");

        var names = fixture.Search("zghsy");

        Assert.Contains("中国好声音演唱会.txt", names);
        Assert.Contains("中国好声音采访.txt", names);
    }

    [TestMethod]
    public void Exclusion_StillVetoesAnAliasOnlyHit()
    {
        // The reported query: 演唱会 appears in neither the alias nor the positive term, so the
        // exclusion can only be read off the name -- which is exactly where the served-up tier used to
        // fail to look. The other file stays, because its own alias satisfies "zghsy" and nothing
        // excludes it: the fix must not turn into a blanket rejection of the alias tier.
        using var fixture = TempIndex.Build("中国好声音演唱会.txt", "中国好声音采访.txt");

        var names = fixture.Search("zghsy :演唱会");

        Assert.DoesNotContain("中国好声音演唱会.txt", names);
        Assert.Contains("中国好声音采访.txt", names);
    }

    [TestMethod]
    public void Exclusion_LeavesNamesItDoesNotName()
    {
        // The mirror image, so "the excluded one is gone" cannot pass by dropping every alias hit.
        using var fixture = TempIndex.Build("中国好声音演唱会.txt", "中国好声音采访.txt");

        var names = fixture.Search("zghsy :采访");

        Assert.Contains("中国好声音演唱会.txt", names);
        Assert.DoesNotContain("中国好声音采访.txt", names);
    }

    [TestMethod]
    public void Exclusion_DoesNotResurrectALiteralNameRejection()
    {
        // The name carries the positive term AND the excluded text, so the name tier rejects it -- and
        // the alias tier must not hand it back. The alias is pinyin and has no 演唱会 in it, which is
        // precisely why matching the alias alone used to resurrect this row.
        using var fixture = TempIndex.Build("zghsy演唱会.txt", "zghsy采访.txt");

        var names = fixture.Search("zghsy :演唱会");

        Assert.Contains("zghsy采访.txt", names);
        Assert.DoesNotContain("zghsy演唱会.txt", names);
    }

    [TestMethod]
    public void FuzzyMatcherSeam_AlsoReadsTheExclusionOffTheName()
    {
        // The process-wide seam the plugins, the CLI and the item catalogs call: there is no snapshot and
        // no record there, so `text` is the candidate's own name and therefore the only thing an exclusion
        // may read. Same rule, reached through FuzzyQuery/FuzzyMatcher instead of the index scan.
        Assert.IsTrue(FuzzyMatcher.IsMatch("zghsy", "中国好声音演唱会.txt"));
        Assert.IsFalse(FuzzyMatcher.IsMatch("zghsy :演唱会", "中国好声音演唱会.txt"));
    }
}
