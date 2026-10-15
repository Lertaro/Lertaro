using Lertaro.Core;
using Lertaro.Core.IndexV2;
using Lertaro.Core.IndexV2.Persistence;
using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.SearchIndex;

namespace Lertaro.Plugins.PinyinAlias.Tests;

// The integration the plugin's own tests could not reach and the doc's verification list called out as
// missing: the REAL provider, baked by the real SnapshotWriter into a real snapshot, found by the real
// searcher. Everything else in this project exercises the plugin in isolation.
//
// The name is the shape the original ticket was about -- 40 polyphonic characters, whose spelled-out
// alias set is the cross product of its readings and therefore only ever partially enumerated. The row
// must be findable by a reading pattern that the enumerated variants do not contain.
[TestClass]
public sealed class PinyinGridSnapshotTests
{
    private const string PolyphonicName = "重.pdf";

    // The 184-character name from the ticket, verbatim, as a highlight fixture.
    private const string TicketName =
        "防弹笔记法-简单输出式笔记架构,保护高产出、高效能心流 -- 电脑玩物站长Esor -- View zhi chang li, 2AB964, Chu ban, Tai bei shi, 2022 -- 城邦出版集团 创意市集 -- 9786267149096 -- df7686e59ff25513fb9c9ac4e906db6d -- Anna’s Archive";

    [ClassInitialize]
    public static void RegisterRealProvider(TestContext context)
        => AliasProviderRegistry.Register(new PinyinAliasProvider());

    [TestMethod]
    public void LongPolyphonicName_IsFoundThroughItsGridWithNoStringAliasToMatch()
    {
        using var temp = new TempIndex();
        var name = new string('重', 40) + PolyphonicName;

        temp.Write(name);

        // "cz" asks for the SECOND reading of one character and then the first reading of a later one.
        // The enumerated variants all put the first reading in table order first, so no baked string can
        // supply that order; the grid can, because it holds every reading at every position.
        var results = temp.Search("cz");

        CollectionAssert.Contains(results, name);
    }

    // Regression for the grid highlight tier's own gating: it must only speak when the string aliases
    // painted nothing. Running it on top of a name whose aliases ARE the enumerated strings lights a
    // second, differently-wrong set of positions -- here four characters from the middle of the name
    // (构/护/电/脑) for a query that only spells out 防弹.
    [TestMethod]
    [DataRow("fangdan")]
    [DataRow("fangdan bi j f")]
    public void Highlight_DoesNotLightUnrelatedCharactersForAnAbbreviatedQuery(string query)
    {
        using var temp = new TempIndex();
        temp.Write(TicketName);

        var mask = FuzzyMatcher.ComputeHighlightMask(TicketName, query);

        Assert.IsTrue(mask[0] && mask[1], "'fangdan' is exactly 防弹's pinyin");
        foreach (var index in new[] { 14, 17, 31, 32 })
            Assert.IsFalse(mask[index], $"position {index} ('{TicketName[index]}') is not part of '{query}'");
    }

    [TestMethod]
    public void LongPolyphonicName_BakesNoStringAliasesFromThePinyinProvider()
    {
        using var temp = new TempIndex();
        var name = new string('重', 40) + PolyphonicName;

        temp.Write(name);

        // Proves the hit above came from the grid: the pinyin provider contributes no readable string
        // alias for this name at all, only the grid entry (which GetAliases deliberately skips).
        var (hasAliases, stringAliases) = temp.AliasCounts(name);
        Assert.IsTrue(hasAliases, "the name must carry at least the grid entry");
        Assert.AreEqual(0, stringAliases, "the enumerated aliases must have lost to the grid for this name");
    }

    private sealed class TempIndex : IDisposable
    {
        private readonly string _directory;
        private readonly string _path;

        public TempIndex()
        {
            _directory = Directory.CreateTempSubdirectory("lertaro-pinyin-grid-").FullName;
            _path = Path.Combine(_directory, "test.idx");
        }

        public void Write(string name)
        {
            var store = new FileRecordStore
            {
                SourceKey = "Z",
                SourceKind = FileRecordSourceKind.LocalMft,
                IdKind = FileRecordIdKind.MftFrn,
                RootId = 1,
            };
            store.Records.Add(new FileRecord(1, 1, string.Empty, FileRecordFlags.Directory | FileRecordFlags.SourceRoot));
            store.Records.Add(new FileRecord(2, 1, name, FileRecordFlags.None));
            SnapshotWriter.Write(store, _path);
        }

        public List<string> Search(string query)
        {
            using var snapshot = Snapshot.Open(_path);
            using var index = new LiveIndex(snapshot);
            var results = new List<SearchResult>();
            IndexV2Searcher.SearchStreaming(index, query, 10, results.Add, CancellationToken.None);
            return results.Select(result => result.Name).ToList();
        }

        public (bool HasAliases, int StringAliases) AliasCounts(string name)
        {
            using var snapshot = Snapshot.Open(_path);
            for (var row = 0; row < snapshot.Count; row++)
            {
                if (snapshot.GetName(row) != name)
                    continue;

                var uid = (int)snapshot.NameIds[row];
                var into = new List<(string Alias, byte ProviderId)>();
                return (snapshot.HasAliases(uid), snapshot.GetAliases(uid, into));
            }

            Assert.Fail($"'{name}' is not in the snapshot");
            return default;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch
            {
            }
        }
    }
}
