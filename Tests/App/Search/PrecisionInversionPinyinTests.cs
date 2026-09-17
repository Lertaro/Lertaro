using System.IO;
using Lertaro.Core;
using Lertaro.Core.IndexV2;
using Lertaro.Core.IndexV2.Persistence;
using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.Indexer;
using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.Search;

// The precision-inversion trigger '?' flips ONE term between fuzzy and exact. It must not change WHICH
// spellings that term may match: a pinyin query has always reached a CJK name through the alias provider,
// and inverting the term to exact must keep that -- only a "/.../" clause is name-text-exact with no alias
// tier behind it. Reported as "?wangfei should still find 王菲".
//
// Index level on purpose: the alias tier a file search uses is the one baked into the snapshot, matched by
// the byte matcher, which is a different code path from FuzzyMatcher's on-the-fly provider scan (the App
// test project is where a real provider may be registered; Core's own tests run with none).
[TestClass]
[DoNotParallelize] // registers into the process-wide alias registry
public sealed class PrecisionInversionPinyinTests
{
    private const char Sep = (char)2;

    // Models the contract the real PinyinAlias provider fills in: an initials alias, a syllable-separated
    // full reading, and separator-separated query forms -- including a partial final syllable, which is what
    // makes a half-typed search-as-you-type query reach the name.
    private sealed class FakePinyinProvider : IAliasProvider
    {
        private static readonly Dictionary<char, string> Readings = new()
        {
            ['王'] = "wang",
            ['菲'] = "fei",
        };

        private static readonly HashSet<string> Syllables = new(StringComparer.Ordinal) { "wang", "fei" };

        public string Name => "FakePinyin";
        public IReadOnlyList<(char Start, char End)> InputRanges { get; } = new[] { ('一', '鿿') };
        public IReadOnlyList<(char Start, char End)> OutputRanges { get; } = new[] { ('a', 'z') };
        public char SyllableSeparator => Sep;

        public bool CanHandle(string text) => text.Any(Readings.ContainsKey);

        public IEnumerable<string> GetAliases(string text)
        {
            var parts = Parts(text);
            yield return string.Concat(parts.Select(p => p[0]));
            yield return string.Join(Sep, parts);
        }

        public IEnumerable<string> GetQueryForms(string term) => Segment(term);

        public int[]? MapAliasToSourceIndices(string text, string alias) => null;

        private static string[] Parts(string text) =>
            text.Select(c => Readings.TryGetValue(c, out var r) ? r : c.ToString()).ToArray();

        // Every reading of the query, each piece a whole syllable except possibly the last (half-typed).
        private static IEnumerable<string> Segment(string query)
        {
            var results = new List<string>();
            Walk(query, 0, new List<string>(), results, allowPartialTail: !Syllables.Contains(query));
            return results;
        }

        private static void Walk(string query, int start, List<string> pieces, List<string> found, bool allowPartialTail)
        {
            if (found.Count >= 8)
                return;

            if (start == query.Length)
            {
                if (pieces.Count > 1)
                    found.Add(string.Join(Sep, pieces));
                return;
            }

            for (var len = Math.Min(4, query.Length - start); len >= 1; len--)
            {
                var piece = query.Substring(start, len);
                var isLast = start + len == query.Length;
                var allowed = Syllables.Contains(piece)
                    || (isLast && allowPartialTail && Syllables.Any(s => s.StartsWith(piece, StringComparison.Ordinal)));
                if (!allowed)
                    continue;

                pieces.Add(piece);
                Walk(query, start + len, pieces, found, allowPartialTail);
                pieces.RemoveAt(pieces.Count - 1);
            }
        }
    }

    private static bool _registered;

    [TestInitialize]
    public void Setup()
    {
        if (!_registered)
        {
            AliasProviderRegistry.Register(new FakePinyinProvider());
            _registered = true;
        }
    }

    // A real in-memory index: records written through the production SnapshotWriter, so the aliases under
    // test are the ones the production pipeline bakes.
    private sealed class IndexFixture : IDisposable
    {
        private readonly string _tempDir;

        public LiveIndex Index { get; }

        public IndexFixture()
        {
            _tempDir = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;
            var path = Path.Combine(_tempDir, "test.idx");
            var store = new FileRecordStore
            {
                SourceKey = "T",
                SourceKind = FileRecordSourceKind.LocalMft,
                IdKind = FileRecordIdKind.MftFrn,
                RootId = 1,
                IsComplete = false,
            };
            store.Records.Add(new FileRecord(1, 1, "", FileRecordFlags.Directory | FileRecordFlags.SourceRoot));
            store.Records.Add(new FileRecord(2, 1, "王菲.txt", FileRecordFlags.None));
            store.Records.Add(new FileRecord(3, 1, "王菲演唱会.mp3", FileRecordFlags.None));
            store.Records.Add(new FileRecord(4, 1, "reports.txt", FileRecordFlags.None));

            SnapshotWriter.Write(store, path);
            Index = new LiveIndex(Snapshot.Open(path));
        }

        public List<string> Search(string query)
        {
            var names = new List<string>();
            IndexV2Searcher.SearchStreaming(Index, query, 10, r => names.Add(r.Name), CancellationToken.None);
            return names;
        }

        public void Dispose()
        {
            Index.Dispose();
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void FuzzyPinyin_ReachesTheCjkName()
    {
        using var fixture = new IndexFixture();
        SearchContext.FuzzyMatchEnabled = true;
        try
        {
            Assert.Contains("王菲.txt", fixture.Search("wangfei"), "the control case: fuzzy pinyin reaches it");
        }
        finally
        {
            SearchContext.FuzzyMatchEnabled = true;
        }
    }

    [TestMethod]
    public void InvertedToExactPinyin_StillReachesTheCjkName()
    {
        using var fixture = new IndexFixture();
        SearchContext.FuzzyMatchEnabled = true;
        try
        {
            // The reported case: '?' inverts this term to a contiguous match, which the pinyin alias must
            // still satisfy -- the alias carries syllable boundaries the user did not type, so the query
            // form (the separator-joined reading) is what makes it contiguous at all.
            Assert.Contains("王菲.txt", fixture.Search("?wangfei"), "'?' must invert precision, not drop the pinyin reading");
        }
        finally
        {
            SearchContext.FuzzyMatchEnabled = true;
        }
    }

    [TestMethod]
    public void InvertedToExactPinyin_ReachesItWithAHalfTypedLastSyllable()
    {
        using var fixture = new IndexFixture();
        SearchContext.FuzzyMatchEnabled = true;
        try
        {
            // Search-as-you-type: "wangfe" is not a syllable sequence yet, and the last piece is allowed to
            // be a prefix -- without that the inverted form would only work once the word was fully typed.
            Assert.Contains("王菲.txt", fixture.Search("?wangfe"), "a half-typed final syllable must still reach the name");
        }
        finally
        {
            SearchContext.FuzzyMatchEnabled = true;
        }
    }

    [TestMethod]
    public void InvertedToExactInitials_StillReachTheCjkName()
    {
        using var fixture = new IndexFixture();
        SearchContext.FuzzyMatchEnabled = true;
        try
        {
            // The initials alias has no internal boundaries, so a contiguous match works as typed.
            Assert.Contains("王菲.txt", fixture.Search("?wf"), "the initials reading must survive the inversion too");
        }
        finally
        {
            SearchContext.FuzzyMatchEnabled = true;
        }
    }

    [TestMethod]
    public void InvertedToExact_DoesNotReachAnUnrelatedName()
    {
        using var fixture = new IndexFixture();
        SearchContext.FuzzyMatchEnabled = true;
        try
        {
            // The inversion still has to MEAN something: a scattered query is no longer a match once the
            // term is exact, which is the whole point of typing '?'.
            Assert.DoesNotContain("reports.txt", fixture.Search("?rdm"), "a scattered subsequence must not match an exact term");
        }
        finally
        {
            SearchContext.FuzzyMatchEnabled = true;
        }
    }
}
