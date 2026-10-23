using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Core.Tests.IndexV2.Search;

// The evaluator's scoring and set semantics, driven by a fake live provider with scripted spans --
// the reading table plays no part here. A word the name literally says keeps fzf's number; the WinShun
// table spelled out in the assertions decides only the words that had to be read.
[TestClass]
[DoNotParallelize]
public sealed class PinyinNameEvaluatorTests
{
    private bool _previousFuzzy;

    [TestInitialize]
    public void UseDefaultFuzzyMode()
    {
        _previousFuzzy = SearchContext.FuzzyMatchEnabled;
        SearchContext.FuzzyMatchEnabled = true;
    }

    [TestCleanup]
    public void RestoreFuzzyMode() => SearchContext.FuzzyMatchEnabled = _previousFuzzy;

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
    public void LiteralMatch_KeepsFzfsOwnVerdict()
    {
        // Nothing had to be read, so fzf scores it: "hello" at the start of a name and after a space are
        // both word starts to FzfAlgorithm and pay the same 140. The reference's 30/18/4 rung only decides
        // candidates the name's own text cannot answer -- which is what keeps an ordinary search ranked the
        // way it was before the readings went live.
        var provider = new FakeLiveProvider().Span("hello", 0, 5);
        Assert.AreEqual(140, Score("hello", "hello world", provider).Score);
        Assert.AreEqual(140, Score("hello", "say hello", provider).Score);
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
    public void Extension_DoesNotDiluteCoverage()
    {
        // A file that spells its own name is no less exact for carrying an extension. Measured over the
        // whole name, ".txt" thins the coverage term and the file loses to the folder it lives in -- the
        // failure mode the reference records as its pitfall 2360 (curl vs curl.exe).
        var provider = new FakeLiveProvider().Span("bg", 0, 2);
        Assert.AreEqual(Score("bg", "报告", provider).Score, Score("bg", "报告.txt", provider).Score);
    }

    [TestMethod]
    public void SetsAnd_LiteralAndPinyin()
    {
        // fzf's own 62 for the word the name literally says, plus 26 for the word only the readings can
        // answer (name start, common reading). No coverage term: something literal anchored this candidate,
        // so the reading table's aggregate terms stay out of the number.
        var result = Score("bg 报告", "报告", new FakeLiveProvider().Span("bg", 0, 2));
        Assert.AreEqual(62 + 26, result.Score);
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
    public void JoinedWords_UseOneAcceptedSpanAndEqualTheUnspacedScore()
    {
        var provider = new FakeLiveProvider()
            .Span("sou", 0, 1)
            .Span("suo", 0, 1)
            .Span("jie", 0, 1)
            .Span("guo", 0, 1)
            .Span("sousuojieguo", 0, 4);
        var spaced = Evaluator("sou suo jie guo", provider);
        var unspaced = Evaluator("sousuojieguo", provider);

        Assert.IsTrue(spaced.HasJoinedBranch);
        // Four per-word position bonuses exceed the one-word score numerically; canonical source
        // coverage, not that token-count artifact, must still select the joined witness.
        Assert.IsTrue(spaced.TryScore("搜索结果", null, out var spacedResult, out var witness));
        Assert.IsTrue(unspaced.TryScore("搜索结果", null, out var unspacedResult));
        Assert.AreEqual(91, spacedResult.Score);
        Assert.AreEqual(unspacedResult.Score, spacedResult.Score);
        Assert.IsTrue(witness.IsJoined);
        Assert.AreEqual(0, witness.Start);
        Assert.AreEqual(4, witness.End);
        Assert.AreEqual(MatchRank.TierFull, witness.Tier);

        var marks = new bool[4];
        Assert.IsTrue(spaced.TryPaintJoined("搜索结果", marks, out var painted));
        CollectionAssert.AreEqual(new[] { true, true, true, true }, marks);
        Assert.AreEqual(witness, painted);
    }

    [TestMethod]
    public void JoinedWords_DoNotReplaceBetterOrdinarySourceCoverage()
    {
        var provider = new FakeLiveProvider()
            .Span("sou", 0, 2)
            .Span("suo", 2, 4)
            .Span("sousuo", 2, 4);
        var evaluator = Evaluator("sou suo", provider);

        Assert.IsTrue(evaluator.HasJoinedBranch);
        Assert.IsTrue(evaluator.TryScore("搜索结果", null, out _, out var witness));
        Assert.IsFalse(witness.IsJoined);
        var marks = new bool[4];
        Assert.IsFalse(evaluator.TryPaintJoined("搜索结果", marks, out _));
    }

    [TestMethod]
    public void JoinedWords_ProviderEnumerationDoesNotChooseTheTieWinner()
    {
        var left = new FakeLiveProvider()
            .Span("sou", -1, -1)
            .Span("suo", -1, -1)
            .Span("sousuo", 0, 1);
        var boundary = new FakeLiveProvider()
            .Span("sou", -1, -1)
            .Span("suo", -1, -1)
            .Span("sousuo", 2, 5);

        var forward = Evaluator("sou suo", left, boundary);
        var reverse = Evaluator("sou suo", boundary, left);
        Assert.IsTrue(forward.TryScore("甲 搜索结", null, out var forwardScore, out var forwardWitness));
        Assert.IsTrue(reverse.TryScore("甲 搜索结", null, out var reverseScore, out var reverseWitness));
        Assert.AreEqual(forwardScore, reverseScore);
        Assert.AreEqual(forwardWitness, reverseWitness);
        Assert.AreEqual(0, forwardWitness.Start);
    }

    [TestMethod]
    public void JoinedWords_RejectOperatorsOrCrossProviderComposition()
    {
        var complete = new FakeLiveProvider()
            .Span("sou", 0, 1)
            .Span("suo", 1, 2)
            .Span("sousuo", 0, 2);
        Assert.IsFalse(Evaluator("^sou suo", complete).HasJoinedBranch);
        Assert.IsFalse(Evaluator("c:sou suo", complete).HasJoinedBranch);
        Assert.IsFalse(Evaluator("'sou suo", complete).HasJoinedBranch);
        Assert.IsFalse(Evaluator("sou !suo", complete).HasJoinedBranch);
        Assert.IsFalse(Evaluator("sou | suo", complete).HasJoinedBranch);

        var first = new FakeLiveProvider().Span("sou", 0, 1).Span("sousuo", 0, 2);
        var second = new FakeLiveProvider().Span("suo", 1, 2).Span("sousuo", 0, 2);
        Assert.IsFalse(Evaluator("sou suo", first, second).HasJoinedBranch);
    }

    [TestMethod]
    public void JoinedWords_RejectNonAsciiAndOverlongInput()
    {
        var nonAscii = new FakeLiveProvider()
            .Span("sou", 0, 1)
            .Span("搜", 0, 1)
            .Span("sou搜", 0, 1);
        Assert.IsFalse(Evaluator("sou 搜", nonAscii).HasJoinedBranch);

        var withinFirst = new string('a', 31);
        var second = new string('b', 32);
        var boundary = new FakeLiveProvider()
            .Span(withinFirst, 0, 1)
            .Span(second, 0, 1)
            .Span(withinFirst + second, 0, 1);
        Assert.IsTrue(Evaluator($"{withinFirst} {second}", boundary).HasJoinedBranch);

        var overFirst = new string('a', 32);
        var overlong = new FakeLiveProvider()
            .Span(overFirst, 0, 1)
            .Span(second, 0, 1)
            .Span(overFirst + second, 0, 1);
        Assert.IsFalse(Evaluator($"{overFirst} {second}", overlong).HasJoinedBranch);
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
