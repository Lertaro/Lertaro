using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.Tests.SearchIndex;

[TestClass]
public sealed class MatchShapeTests
{
    [TestMethod]
    [DataRow("code-atlas", 5, 10, MatchRank.ShapeWholeWord)]
    [DataRow("atlas.png", 0, 5, MatchRank.ShapeWholeWord)]
    [DataRow("CodeAtlas", 4, 9, MatchRank.ShapeWholeWord)]
    [DataRow("atlasing", 0, 5, MatchRank.ShapeWordStart)]
    [DataRow("metatlas.txt", 3, 8, MatchRank.ShapeInsideWord)]
    [DataRow("x_atlas", 2, 7, MatchRank.ShapeWholeWord)]
    public void Classify_EnglishNameShapes(string name, int start, int end, int expected)
    {
        Assert.AreEqual(expected, MatchShape.Classify(name, start, end));
    }

    [TestMethod]
    public void Classify_WholeTextIsAWholeWord()
    {
        Assert.AreEqual(MatchRank.ShapeWholeWord, MatchShape.Classify("atlas", 0, 5));
    }

    [TestMethod]
    [DataRow("屏幕截图.txt", 0, 4, MatchRank.ShapeWholeWord)]
    [DataRow("屏幕截图.txt", 0, 2, MatchRank.ShapeWordStart)]
    [DataRow("屏幕截图.txt", 2, 4, MatchRank.ShapeInsideWord)]
    [DataRow("屏幕截图.txt", 3, 4, MatchRank.ShapeInsideWord)]
    public void Classify_UnspacedChineseRunUsesPosition(string name, int start, int end, int expected)
    {
        Assert.AreEqual(expected, MatchShape.Classify(name, start, end));
    }
}
