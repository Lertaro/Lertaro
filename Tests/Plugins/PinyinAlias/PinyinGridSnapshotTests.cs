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
        // The enumerated variants all put the dominant reading first, so no baked string can supply that
        // order; the grid can, because it holds every reading at every position.
        var results = temp.Search("cz");

        CollectionAssert.Contains(results, name);
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
