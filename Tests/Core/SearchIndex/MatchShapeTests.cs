using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.Tests.SearchIndex;

// How the query sits in a name -- the leading match key in every window. Asserted through
// HighlightMask.ComputeRank, which is what every ranking site reads, rather than MatchShape alone, so the
// per-term and per-set folding is covered too.
[TestClass]
public sealed class MatchShapeTests
{
    [TestMethod]
    [DataRow("atlas")]
    [DataRow("code-atlas")]
    [DataRow("atlas.png")]
    [DataRow("my_atlas")]
    [DataRow("code atlas")]
    [DataRow("CodeAtlas")]
    [DataRow("AtlasViewer")]
    [DataRow("atlas01")]
    [DataRow("ATLAS")]
    [DataRow("metatlas_atlas")] // the best occurrence counts, not the first
    public void WholeWord(string name) => Assert.AreEqual(MatchRank.ShapeWholeWord, ShapeOf(name, "atlas"));

    [TestMethod]
    [DataRow("atlasing")]
    [DataRow("atlases.txt")]
    [DataRow("ATLASES")]
    public void WordStart(string name) => Assert.AreEqual(MatchRank.ShapeWordStart, ShapeOf(name, "atlas"));

    [TestMethod]
    [DataRow("metatlas.txt")]
    [DataRow("fatlasing")]
    public void InsideWord(string name) => Assert.AreEqual(MatchRank.ShapeInsideWord, ShapeOf(name, "atlas"));

    // The reported names: they contain a-t-l-a-s only as scattered letters.
    [TestMethod]
    [DataRow("AutofillStates")]
    [DataRow("AutofillStrikeDatabase")]
    [DataRow("annotatescrollbar.js")]
    public void Scattered(string name) => Assert.AreEqual(MatchRank.ShapeScattered, ShapeOf(name, "atlas"));

    // An unspaced CJK run is ONE word for shape classification (MatchShape's ponytail note: position, not a
    // segmenter), so a hit that starts mid-run is inside a word rather than a whole one.
    [TestMethod]
    public void UncasedScript_RunIsOneWordSoAMidRunHitSitsInsideIt() =>
        Assert.AreEqual(MatchRank.ShapeInsideWord, ShapeOf("网易云音乐", "音乐"));

    // AND across words: the weaker word decides.
    [TestMethod]
    public void MultiTerm_WorstWordDecides() =>
        Assert.AreEqual(MatchRank.ShapeWordStart, ShapeOf("code-atlasing", "code atlas"));

    // OR within a set: the better alternative decides.
    [TestMethod]
    public void OrTerms_BestAlternativeDecides() =>
        Assert.AreEqual(MatchRank.ShapeWholeWord, ShapeOf("code-atlas", "atl | atlas"));

    // A negated word describes nothing about how the name matched, so it must not demote the row.
    [TestMethod]
    public void InverseTerm_DoesNotAffectShape() =>
        Assert.AreEqual(MatchRank.ShapeWholeWord, ShapeOf("code-atlas", "atlas :zzz"));

    [TestMethod]
    public void NoMatch_HasNoNameShape() =>
        Assert.AreEqual(MatchRank.ShapeNoName, HighlightMask.ComputeRank("readme", FzfPattern.Parse("xyz")).Shape);

    // The classifier on its own, for the boundary rules the query-level tests above can only reach
    // through a matched span: main rebuilt MatchShape.Classify, so its inputs are pinned directly here.
    [TestMethod]
    [DataRow("code-atlas", 5, 10, MatchRank.ShapeWholeWord)]
    [DataRow("atlas.png", 0, 5, MatchRank.ShapeWholeWord)]
    [DataRow("CodeAtlas", 4, 9, MatchRank.ShapeWholeWord)]
    [DataRow("atlasing", 0, 5, MatchRank.ShapeWordStart)]
    [DataRow("metatlas.txt", 3, 8, MatchRank.ShapeInsideWord)]
    [DataRow("x_atlas", 2, 7, MatchRank.ShapeWholeWord)]
    public void Classify_EnglishNameShapes(string name, int start, int end, int expected) => Assert.AreEqual(expected, MatchShape.Classify(name, start, end));

    [TestMethod]
    public void Classify_WholeTextIsAWholeWord() => Assert.AreEqual(MatchRank.ShapeWholeWord, MatchShape.Classify("atlas", 0, 5));

    [TestMethod]
    [DataRow("屏幕截图.txt", 0, 4, MatchRank.ShapeWholeWord)]
    [DataRow("屏幕截图.txt", 0, 2, MatchRank.ShapeWordStart)]
    [DataRow("屏幕截图.txt", 2, 4, MatchRank.ShapeInsideWord)]
    [DataRow("屏幕截图.txt", 3, 4, MatchRank.ShapeInsideWord)]
    public void Classify_UnspacedChineseRunUsesPosition(string name, int start, int end, int expected) => Assert.AreEqual(expected, MatchShape.Classify(name, start, end));

    private static int ShapeOf(string name, string query)
    {
        var rank = HighlightMask.ComputeRank(name, FzfPattern.Parse(query));
        Assert.IsTrue(rank.IsMatch, $"{name} should match {query}");
        return rank.Shape;
    }
}
