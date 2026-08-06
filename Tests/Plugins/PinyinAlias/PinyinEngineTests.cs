namespace Lertaro.Plugins.PinyinAlias.Tests;

[TestClass]
public sealed class PinyinEngineTests
{
    [TestMethod]
    public void IsChinese_CommonHanzi_ReturnsTrue() => Assert.IsTrue(PinyinEngine.IsChinese('中'));

    [TestMethod]
    public void IsChinese_AsciiChar_ReturnsFalse() => Assert.IsFalse(PinyinEngine.IsChinese('a'));

    [TestMethod]
    public void IsChinese_CharBeyondTableRange_ReturnsFalse() => Assert.IsFalse(PinyinEngine.IsChinese((char)0xFFFF));

    [TestMethod]
    public void TryGetPinyins_CommonHanzi_ReturnsLowercaseAsciiPinyin()
    {
        var found = PinyinEngine.TryGetPinyins('中', out var pinyins);

        Assert.IsTrue(found);
        Assert.IsGreaterThan(0, pinyins.Length);
        CollectionAssert.Contains(pinyins, "zhong");
    }

    [TestMethod]
    public void TryGetPinyins_NonChinese_ReturnsFalseAndEmpty()
    {
        var found = PinyinEngine.TryGetPinyins('a', out var pinyins);

        Assert.IsFalse(found);
        Assert.IsEmpty(pinyins);
    }

    [TestMethod]
    public void TryGetPinyinIds_MatchesTryGetPinyinsViaSyllableUtf8()
    {
        PinyinEngine.TryGetPinyins('中', out var expectedPinyins);
        var idsFound = PinyinEngine.TryGetPinyinIds('中', out var ids);

        Assert.IsTrue(idsFound);
        Assert.HasCount(expectedPinyins.Length, ids);
        for (var i = 0; i < ids.Length; i++)
        {
            var decoded = System.Text.Encoding.ASCII.GetString(PinyinEngine.GetSyllableUtf8(ids[i]));
            Assert.AreEqual(expectedPinyins[i], decoded);
        }
    }

    [TestMethod]
    public void TryGetPinyinIds_NonChinese_ReturnsFalseAndEmpty()
    {
        var found = PinyinEngine.TryGetPinyinIds('a', out var ids);

        Assert.IsFalse(found);
        Assert.IsEmpty(ids);
    }

    [TestMethod]
    public void MayContainChinese_TextWithHanzi_ReturnsTrue() => Assert.IsTrue(PinyinEngine.MayContainChinese("hello 中文"));

    [TestMethod]
    public void MayContainChinese_PureAscii_ReturnsFalse() => Assert.IsFalse(PinyinEngine.MayContainChinese("hello world"));

    [TestMethod]
    public void TableRange_MatchesDocumentedBounds()
    {
        Assert.AreEqual((char)12295, PinyinEngine.TableRange.Start);
        Assert.AreEqual((char)(12295 + 28647 - 1), PinyinEngine.TableRange.End);
    }
}
