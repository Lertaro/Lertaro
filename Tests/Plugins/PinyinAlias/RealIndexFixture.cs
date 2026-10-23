using Lertaro.Core;
using Lertaro.Core.IndexV2;
using Lertaro.Core.IndexV2.Persistence;
using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.SearchIndex;

namespace Lertaro.Plugins.PinyinAlias.Tests;

// Real provider, real SnapshotWriter, real LiveIndex+searcher over one temp file. Shared by the test
// classes that need the whole pipeline rather than the plugin alone -- the aliases a query has to reach
// only exist once the host has baked them.
internal sealed class RealIndexFixture : IDisposable
{
    private readonly string _directory;
    private readonly string _path;

    public RealIndexFixture(params string[] names)
    {
        _directory = Directory.CreateTempSubdirectory("lertaro-pinyin-").FullName;
        _path = Path.Combine(_directory, "test.idx");

        var store = new FileRecordStore
        {
            SourceKey = "Z",
            SourceKind = FileRecordSourceKind.LocalMft,
            IdKind = FileRecordIdKind.MftFrn,
            RootId = 1,
        };
        store.Records.Add(new FileRecord(1, 1, string.Empty, FileRecordFlags.Directory | FileRecordFlags.SourceRoot));
        ulong id = 2;
        foreach (var name in names)
            store.Records.Add(new FileRecord(id++, 1, name, FileRecordFlags.None));

        SnapshotWriter.Write(store, _path);
    }

    public List<string> Search(string query, int limit = 20)
    {
        using var snapshot = Snapshot.Open(_path);
        using var index = new LiveIndex(snapshot);
        return Search(index, query, limit);
    }

    public List<string> SearchWithAddedDelta(string query, string name, int limit = 20)
    {
        using var snapshot = Snapshot.Open(_path);
        using var index = new LiveIndex(snapshot);
        index.Mutate((_, delta) =>
            delta.Upsert(10_000, 1, name, FileRecordFlags.None, 0, 0, 0, 0));
        return Search(index, query, limit);
    }

    private static List<string> Search(LiveIndex index, string query, int limit)
    {
        var results = new List<SearchResult>();
        IndexV2Searcher.SearchStreaming(index, query, limit, results.Add, CancellationToken.None);
        return results.Select(result => result.Name).ToList();
    }

    /// <summary>Indices of the characters the final highlight mask lights up for one name.</summary>
    public List<int> HighlightedIndices(string name, string query)
    {
        var mask = FuzzyMatcher.ComputeHighlightMask(name, query);
        var lit = new List<int>();
        for (var i = 0; i < mask.Length; i++)
        {
            if (mask[i])
                lit.Add(i);
        }
        return lit;
    }

    /// <summary>Alias entries of one name: whether it has any at all, and how many are readable strings.
    /// A legacy reading-grid entry is not readable, which is how these tests see the baked shape from
    /// outside Core (nothing else about a baked alias entry is public).</summary>
    public (bool HasAliases, int ReadableStrings) AliasCounts(string name)
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
