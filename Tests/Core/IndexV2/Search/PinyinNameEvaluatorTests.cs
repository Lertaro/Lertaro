using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Core.Tests.IndexV2.Search;

// The evaluator's scoring and set semantics, driven by a fake live provider with scripted spans --
// the reading table plays no part here. Scores are the WinShun table spelled out in the assertions.
[TestClass]
public sealed class PinyinNameEvaluatorTests
{
    // Hands out one scripted span per term; no text inspection at all.
    private sealed class FakeLiveProvider : IAliasLiveMatcherProvider
    {
        private readonly Dictionary<string, (int Start, int End)> _full = new(StringComparer.Ordinal);
        private readonly HashSet<string> _common = new(StringComparer.Ordinal);

        public FakeLiveProvider Span(string term, int start, int end, bool common = true)
        {
            _full[term] = (start, end);
            if (common)
                _common.Add(term);
            return this;
        }

        public bool CanHandle(string text) => true;

        public IReadOnlyList<(char Start, char End)> InputRanges => Array.Empty<(char Start, char End)>();

        public IReadOnlyList<(char Start, char End)> OutputRanges => new[] { ('a', 'z') };

        public IEnumerable<string> GetAliases(string text) => Array.Empty<string>();

        public string[][] GetReadingSets(string text) => Array.Empty<string[]>();

        public void AppendSpellings(string text, List<string> dest)
        {
        }

        public IAliasTermMatcher? CreateMatcher(string term, bool commonOnly)
        {
            if (!_full.TryGetValue(term, out var span))
                return null;
            if (commonOnly && !_common.Contains(term))
                return null;
            return new FakeMatcher(span);
        }

        private sealed class FakeMatcher((int Start, int End) span) : IAliasTermMatcher
        {
            public bool TryFind(ReadOnlySpan<char> text, Span<bool> painted, out int start, out int end)
            {
                start = span.Start;
                end = span.End;
                if (start < 0 || start >= end || end > text.Length)
                {
                    start = -1;
                    end = -1;
                    return false;
                }
                for (var i = start; i < end && i < painted.Length; i++)
                    painted[i] = true;
                return true;
            }
        }
    }

    private static PinyinNameEvaluator Evaluator(string query, params IAliasLiveMatcherProvider[] providers)
        => PinyinNameEvaluator.Build(FzfPattern.Parse(query), providers);

    private static FzfPatternResult Score(string query, string name, FakeLiveProvider provider)
    {
        var evaluator = Evaluator(query, provider);
        Assert.IsTrue(evaluator.TryScore(name, null, out var result), $"'{query}' must match '{name}'");
        return result;
    }

    [TestMethod]
    public void LiteralBonus_NameStart()
    {
        // 30 (start of the name) + 25 * 5/11 (coverage).
        var result = Score("hello", "hello world", new FakeLiveProvider().Span("hello", 0, 5));
        Assert.AreEqual(41, result.Score);
    }

    [TestMethod]
    public void LiteralBonus_WordStart()
    {
        // 18 (word start) + 25 * 5/9.
        var result = Score("hello", "say hello", new FakeLiveProvider().Span("hello", 0, 5));
        Assert.AreEqual(31, result.Score);
    }

    [TestMethod]
    public void PinyinBonus_WholeNameCommonReading()
    {
        // 26 (name start, common reading) + 40 (whole stem spelled) + 25 * 2/2.
        var result = Score("bg", "报告", new FakeLiveProvider().Span("bg", 0, 2));
        Assert.AreEqual(91, result.Score);
        Assert.AreEqual(0, result.MinBegin);
        Assert.AreEqual(2, result.MaxEnd);
        Assert.IsTrue(result.ValidOffsetFound);
    }

    [TestMethod]
    public void PinyinBonus_RareReadingPaysMinusTwelve()
    {
        // -12 (the common-only matcher misses it) + 26 + 40 + 25.
        var result = Score("bg", "报告", new FakeLiveProvider().Span("bg", 0, 2, common: false));
        Assert.AreEqual(79, result.Score);
    }

    [TestMethod]
    public void PinyinBonus_WordStartInsideTheName()
    {
        // 16 (word start) + 25 * 2/4; the span does not open the name, so no whole-name bonus.
        var result = Score("bg", "x报告y", new FakeLiveProvider().Span("bg", 1, 3));
        Assert.AreEqual(28, result.Score);
    }

    [TestMethod]
    public void SetsAnd_LiteralAndPinyin()
    {
        // pinyin 26 + literal 30 (both at the name start) + coverage 25 * min(4, 2)/2.
        var result = Score("bg 报告", "报告", new FakeLiveProvider().Span("bg", 0, 2));
        Assert.AreEqual(81, result.Score);
    }

    [TestMethod]
    public void Exclusion_VetoesWhenPresent()
    {
        var evaluator = Evaluator("!报告 报", new FakeLiveProvider());
        Assert.IsFalse(evaluator.TryScore("报告", null, out _));
    }

    [TestMethod]
    public void Exclusion_SatisfiedByAbsence_ThenPinyinCarriesTheQuery()
    {
        var evaluator = Evaluator("!xyz bg", new FakeLiveProvider().Span("bg", 0, 2));
        Assert.IsTrue(evaluator.TryScore("报告", null, out var result));
        Assert.AreEqual(0, result.MinBegin);
        Assert.AreEqual(2, result.MaxEnd);
    }

    [TestMethod]
    public void OperatorTerms_AreLiteralOnly()
    {
        // "^bg" keeps its anchored literal reading: the pinyin fallback never runs for operators.
        var evaluator = Evaluator("^bg", new FakeLiveProvider().Span("bg", 0, 2));
        Assert.IsFalse(evaluator.TryScore("报告", null, out _));
    }

    [TestMethod]
    public void WithoutProviders_Declines()
    {
        // No live provider: the evaluator steps aside and the caller keeps its normal name tier.
        var evaluator = Evaluator("bg");
        Assert.IsFalse(evaluator.HasProviders);
        Assert.IsFalse(evaluator.TryScore("报告", null, out _));
    }

    [TestMethod]
    public void Paint_CoversThePinyinSpan()
    {
        var evaluator = Evaluator("bg", new FakeLiveProvider().Span("bg", 1, 3));
        var marks = new bool[4];
        Assert.IsTrue(evaluator.TryPaintTerm("x报告y", "bg", FzfTermKind.Fuzzy, marks, out var tier));
        CollectionAssert.AreEqual(new[] { false, true, true, false }, marks);
        // Two positions giving up one letter each: an initials-shaped spelling.
        Assert.AreEqual(MatchRank.TierInitials, tier);
    }

    [TestMethod]
    public void Paint_RareReadingIsItsOwnTier()
    {
        var evaluator = Evaluator("baog", new FakeLiveProvider().Span("baog", 1, 3, common: false));
        var marks = new bool[4];
        Assert.IsTrue(evaluator.TryPaintTerm("x报告y", "baog", FzfTermKind.Fuzzy, marks, out var tier));
        Assert.AreEqual(MatchRank.TierFullRare, tier);
    }
}
