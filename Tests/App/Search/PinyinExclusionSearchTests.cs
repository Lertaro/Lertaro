using System.IO;
using Lertaro.Core;
using Lertaro.Core.IndexV2;
using Lertaro.Core.IndexV2.Persistence;
using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.Search;

// `!term` means "drop any candidate whose NAME contains term" (see Site/user-guide/search-syntax.md),
// and an excluded term is deliberately never pinyin-expanded, so the check only ever has something to
// compare against in the name itself. But every matcher tries the name first and then re-runs the WHOLE
// pattern against each baked alias -- and a transliteration can't contain 王菲, so the exclusion term
// found nothing there and its term set passed vacuously. The candidate the name match had just rejected
// was revived one tier later: `fhcq !王菲` still listed 王菲凤凰传奇.txt.
//
// A fake provider keeps the expected spellings fixed instead of tied to the syllable table, and drives
// the real bake (SnapshotWriter -> AliasGenerationUtf8) and the real query (IndexV2Searcher ->
// FzfPatternParser), which is where the two tiers meet.
[TestClass]
[DoNotParallelize] // registers into a process-wide registry
public sealed class PinyinExclusionSearchTests
{
    private const char Sep = (char)2;

    private sealed class FakePinyinProvider : IAliasProvider
    {
        private static readonly Dictionary<char, string> Readings = new()
        {
            ['王'] = "wang",
            ['菲'] = "fei",
            ['凤'] = "feng",
            ['凰'] = "huang",
            ['传'] = "chuan",
            ['奇'] = "qi",
        };

        public string Name => "FakePinyin";
        public IReadOnlyList<(char Start, char End)> InputRanges { get; } = new[] { ('一', '鿿') };
        public IReadOnlyList<(char Start, char End)> OutputRanges { get; } = new[] { ('a', 'z') };
        public char SyllableSeparator => Sep;

        public bool CanHandle(string text) => text.Any(Readings.ContainsKey);

        // The two shapes the real provider emits: per-character initials, and the full readings.
        public IEnumerable<string> GetAliases(string text)
        {
            var parts = text.Select(c => Readings.TryGetValue(c, out var r) ? r : c.ToString()).ToArray();
            yield return string.Concat(parts.Select(p => p[0]));
            yield return string.Join(Sep, parts);
        }

        // Silent on purpose, so this fake adds no query forms to every other test in the process
        // (FzfPattern.Parse asks every registered provider).
        public IEnumerable<string> GetQueryForms(string term) => Array.Empty<string>();
    }

    private sealed class TempIndex : IDisposable
    {
        private readonly string _tempDir;

        public LiveIndex Index { get; }

        private TempIndex(string tempDir, LiveIndex index)
        {
            _tempDir = tempDir;
            Index = index;
        }

        public static TempIndex Build(params string[] fileNames) => BuildNested(null!, fileNames);

        // With one folder above them: the ancestor pass lets a FOLDER satisfy a term the file name cannot,
        // which is the other door an exclusion has to be closed in.
        public static TempIndex BuildNested(string folder, params string[] fileNames)
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
            store.Records.Add(new FileRecord(1, 1, "", FileRecordFlags.Directory | FileRecordFlags.SourceRoot));
            UInt128 parentId = 1;
            if (folder != null)
            {
                store.Records.Add(new FileRecord(2, 1, folder, FileRecordFlags.Directory));
                parentId = 2;
            }
            for (var i = 0; i < fileNames.Length; i++)
                store.Records.Add(new FileRecord((UInt128)(i + 3), parentId, fileNames[i], FileRecordFlags.None));

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
        if (!_registered)
        {
            AliasProviderRegistry.Register(new FakePinyinProvider());
            _registered = true;
        }
    }

    private static List<string> Search(LiveIndex index, string query)
    {
        var results = new List<SearchResult>();
        IndexV2Searcher.SearchStreaming(index, query, 20, results.Add, CancellationToken.None);
        return results.Select(r => r.Name).ToList();
    }

    private static TempIndex FourNames() => TempIndex.Build("王菲凤凰传奇.txt", "王菲.txt", "凤凰传奇王菲.txt", "凤凰传奇.txt");

    // The control: without the exclusion every candidate whose initials carry fhcq is listed, including
    // the two with 王菲 in the name -- so the assertions below fail only because of the exclusion term.
    [TestMethod]
    public void PinyinQuery_WithoutExclusion_FindsAllThree()
    {
        using var fixture = FourNames();

        CollectionAssert.AreEquivalent(
            new[] { "王菲凤凰传奇.txt", "凤凰传奇王菲.txt", "凤凰传奇.txt" },
            Search(fixture.Index, "fhcq"));
    }

    [TestMethod]
    public void PinyinQuery_WithCjkExclusion_DropsTheExcludedCandidates()
    {
        using var fixture = FourNames();

        CollectionAssert.AreEqual(new[] { "凤凰传奇.txt" }, Search(fixture.Index, "fhcq !王菲"));
    }

    // Same query without pinyin: the positive term matches the names directly, so the exclusion was
    // already honoured there. This is what the alias tier has to keep doing.
    [TestMethod]
    public void CjkQuery_WithCjkExclusion_DropsTheExcludedCandidates()
    {
        using var fixture = FourNames();

        CollectionAssert.AreEqual(new[] { "凤凰传奇.txt" }, Search(fixture.Index, "凤凰传奇 !王菲"));
    }

    // The other door an exclusion has to be closed in: the ancestor pass lets a FOLDER satisfy a term the
    // file name cannot, and an exclusion is satisfied by any segment that lacks its text -- so a file whose
    // OWN name is exactly what was excluded used to be topped back in from under a matching folder. The
    // folder's help has to survive: 笔记.txt is still found through 凤凰传奇 above it.
    [TestMethod]
    public void AncestorFanOut_WithCjkExclusion_DropsTheExcludedNameAndKeepsTheFolderMatch()
    {
        using var fixture = TempIndex.BuildNested("凤凰传奇", "王菲凤凰传奇.txt", "笔记.txt");

        // The control: the folder itself is a row, and 笔记.txt only comes back because the folder above
        // it satisfies the term its own name cannot.
        CollectionAssert.AreEquivalent(new[] { "凤凰传奇", "王菲凤凰传奇.txt" }, Search(fixture.Index, "凤凰传奇"));
        CollectionAssert.AreEquivalent(new[] { "凤凰传奇", "笔记.txt" }, Search(fixture.Index, "凤凰传奇 !王菲"));

        // The reported shape on top of it: here even the positive term only reaches these rows through the
        // folder's pinyin alias, so both tiers are alias-driven and neither may undo `!王菲`.
        CollectionAssert.AreEquivalent(new[] { "凤凰传奇", "笔记.txt" }, Search(fixture.Index, "fhcq !王菲"));
    }

    // Excluded terms are not pinyin-expanded (documented, to avoid over-exclusion): the initials of what
    // was excluded are not the excluded text, so they reach no name here.
    [TestMethod]
    public void PinyinQuery_WithPinyinExclusion_OverExcludesNothing()
    {
        using var fixture = FourNames();

        CollectionAssert.AreEquivalent(
            new[] { "王菲凤凰传奇.txt", "凤凰传奇王菲.txt", "凤凰传奇.txt" },
            Search(fixture.Index, "fhcq !wf"));
    }

    // The same rule on the public seam used for candidates that carry no index row at all -- application
    // and settings catalog items, history, folder listings.
    [TestMethod]
    public void FuzzyQuery_ExcludesByNameNotByAlias()
    {
        var query = FuzzyQuery.Parse("fhcq !王菲");

        Assert.IsTrue(query.IsMatch("凤凰传奇.txt"));
        Assert.IsFalse(query.IsMatch("王菲凤凰传奇.txt"));
        Assert.IsFalse(query.IsMatch("凤凰传奇王菲.txt"));
    }

    // A freshly created/renamed row is matched per record with its own alias array instead of against the
    // unique table -- a second implementation of the same two tiers, so the same query runs through it.
    [TestMethod]
    public void DeltaRow_PinyinQuery_WithCjkExclusion_DropsTheExcludedName()
    {
        using var fixture = FourNames();
        fixture.Index.Mutate((_, delta) => delta.Upsert(100, 1, "凤凰传奇.mp3", FileRecordFlags.None, 1, 0, 0, 0));

        var withExclusion = Search(fixture.Index, "fhcq !王菲");

        Assert.Contains("凤凰传奇.mp3", withExclusion);
        CollectionAssert.DoesNotContain(withExclusion, "王菲凤凰传奇.txt");
    }
}
