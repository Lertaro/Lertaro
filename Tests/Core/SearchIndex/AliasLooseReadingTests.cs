using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Core.Tests.SearchIndex;

// The alias lane reads an Exact term a second, loose way (see FzfPatternAliasReading) so an
// abbreviation that straddles syllables still reaches its file in exact mode. That loose reading is
// gated by the same tightness the painter applies; without the gate it also accepted a scatter the
// painter refuses, and since the literal read used to be skipped whenever a live provider existed, such
// a row could rank on a hit the user cannot see ("zbl" against a name whose only "z b l" are the
// letters of "z-library" and "1lib").
[TestClass]
[DoNotParallelize]
public sealed class AliasLooseReadingTests
{
    private const string Text =
        "商务印书馆汉译世界学术名著丛书28册套装 (约翰.罗 等) (z-library.sk, 1lib.sk, z-lib.sk).epub";

    [TestMethod]
    public void ExactMode_DoesNotReachANameByAliasScatter()
    {
        using var scope = new IdentityAliasScope();
        var before = SearchContext.DefaultFuzzyMatchEnabled;
        try
        {
            SearchContext.DefaultFuzzyMatchEnabled = false;
            Assert.IsFalse(FuzzyMatcher.IsMatch("zbl", Text));

            // The loose reading still admits a tight abbreviation inside the same alias.
            Assert.IsTrue(FuzzyMatcher.IsMatch("zL", Text));
        }
        finally
        {
            SearchContext.DefaultFuzzyMatchEnabled = before;
        }
    }

    [TestMethod]
    public void FuzzyMode_ReachesTheNameThroughTheLiteralRead()
    {
        using var scope = new IdentityAliasScope();
        var before = SearchContext.DefaultFuzzyMatchEnabled;
        try
        {
            SearchContext.DefaultFuzzyMatchEnabled = true;
            Assert.IsTrue(FuzzyMatcher.IsMatch("zbl", Text));
        }
        finally
        {
            SearchContext.DefaultFuzzyMatchEnabled = before;
        }
    }

    // Registers an alias provider whose only alias is the text itself -- the shape the pinyin provider
    // produces for an already-Simplified name. The provider stays registered (there is no unregister) but
    // is filtered out for every later test on dispose.
    private sealed class IdentityAliasScope : IDisposable
    {
        private readonly Func<IAliasProvider, bool> _previous;

        public IdentityAliasScope()
        {
            _previous = AliasProviderRegistry.FilterFunc;
            AliasProviderRegistry.Register(new IdentityAliasProvider());
        }

        public void Dispose() => AliasProviderRegistry.FilterFunc = p => p is not IdentityAliasProvider && _previous(p);
    }

    private sealed class IdentityAliasProvider : IAliasProvider
    {
        public IReadOnlyList<(char Start, char End)> InputRanges { get; } = new[] { ('\u4e00', '\u9fff') };
        public IReadOnlyList<(char Start, char End)> OutputRanges { get; } = new[] { ('a', 'z') };
        public char SyllableSeparator => '\'';
        public bool CanHandle(string text) => AliasProviderRegistry.HasNonAscii(text);
        public IEnumerable<string> GetAliases(string text) => [text];
    }
}
