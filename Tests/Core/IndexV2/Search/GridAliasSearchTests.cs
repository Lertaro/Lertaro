using System.Text;
using Lertaro.Core.IndexV2.Persistence;
using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Core.Tests.IndexV2.Search;

// End-to-end cover for the reading-grid path: a provider's reading space goes through the real
// SnapshotWriter, is stored as a grid when that is the smaller encoding, and is then found by the real
// searcher through the same scoring pipeline a baked string alias uses.
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

    // Five readings whose initials spell "fdbjf", and a deliberately bulky string encoding so the grid
    // wins the size comparison and is what actually lands in the snapshot.
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

        public IEnumerable<string> GetAliases(string text) => new[] { "fdbjf" };
    }

    // Same readings, but a tiny string encoding -- so the snapshot must keep the string form and store
    // no grid at all.
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

    [TestMethod]
    public void SmallEnumeration_KeepsTheStringEncoding()
    {
        using var fixture = LiveIndexFixture.Build("Z", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, StringMarker + "笔记法X", FileRecordFlags.None),
        });

        var kinds = KindsOf(fixture);
        CollectionAssert.DoesNotContain(kinds, AliasEntryKind.Grid);
        CollectionAssert.Contains(kinds, AliasEntryKind.String);

        var results = new List<SearchResult>();
        IndexV2Searcher.SearchStreaming(fixture.Index, "fdbjf", 10, results.Add, CancellationToken.None);

        CollectionAssert.Contains(results.Select(result => result.Name).ToList(), StringMarker + "笔记法X");
    }
}
