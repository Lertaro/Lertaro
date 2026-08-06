using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.Tests.SearchIndex.Fzf;

[TestClass]
public sealed class FzfCharTablesTests
{
    [TestMethod]
    public void GetClass_Char_MatchesGeneralAlgorithmAcrossAsciiRange()
    {
        for (var c = 0; c < 128; c++)
            Assert.AreEqual((byte)FzfAlgorithm.GetClass((char)c), FzfCharTables.GetClass((char)c), $"mismatch at char {c}");
    }

    [TestMethod]
    public void GetClass_NonAsciiChar_FallsBackToGeneralAlgorithm()
    {
        var c = 'é';

        Assert.AreEqual((byte)FzfAlgorithm.GetClass(c), FzfCharTables.GetClass(c));
    }

    [TestMethod]
    public void GetClass_Byte_MatchesCharOverloadForAsciiRange()
    {
        for (var b = 0; b < 128; b++)
            Assert.AreEqual(FzfCharTables.GetClass((char)b), FzfCharTables.GetClass((byte)b));
    }

    [TestMethod]
    public void ToLower_AsciiUppercase_MatchesInvariantToLower()
    {
        Assert.AreEqual('a', FzfCharTables.ToLower('A'));
        Assert.AreEqual('z', FzfCharTables.ToLower('Z'));
        Assert.AreEqual('5', FzfCharTables.ToLower('5'));
    }

    [TestMethod]
    public void ToLower_NonAscii_FallsBackToCharToLowerInvariant() => Assert.AreEqual(char.ToLowerInvariant('É'), FzfCharTables.ToLower('É'));

    [TestMethod]
    public void ToLower_Byte_MatchesCharOverload() => Assert.AreEqual((byte)'a', FzfCharTables.ToLower((byte)'A'));

    [TestMethod]
    public void Bonus_MatchesGeneralAlgorithmForEveryClassPairAndScheme()
    {
        foreach (var scheme in Enum.GetValues<FzfScoringScheme>())
        {
            foreach (var previous in Enum.GetValues<FzfAlgorithm.CharClass>())
            {
                foreach (var current in Enum.GetValues<FzfAlgorithm.CharClass>())
                {
                    var expected = (short)FzfAlgorithm.BonusFor(previous, current, scheme);
                    var actual = FzfCharTables.Bonus(scheme, (byte)previous, (byte)current);
                    Assert.AreEqual(expected, actual, $"mismatch for {scheme}/{previous}/{current}");
                }
            }
        }
    }

    [TestMethod]
    public void CharsEqual_CaseSensitive_RequiresExactMatch()
    {
        Assert.IsTrue(FzfCharTables.CharsEqual('a', 'a', caseSensitive: true));
        Assert.IsFalse(FzfCharTables.CharsEqual('a', 'A', caseSensitive: true));
    }

    [TestMethod]
    public void CharsEqual_CaseInsensitive_LowersOnlyTheTextSide()
    {
        // Only `text` gets lowered before comparing -- callers must pass an already-lowercased pattern.
        Assert.IsTrue(FzfCharTables.CharsEqual('A', 'a', caseSensitive: false));
        Assert.IsFalse(FzfCharTables.CharsEqual('a', 'A', caseSensitive: false));
    }

    [TestMethod]
    public void CharsEqual_Byte_BehavesLikeCharOverload()
    {
        Assert.IsTrue(FzfCharTables.CharsEqual((byte)'A', (byte)'a', caseSensitive: false));
        Assert.IsFalse(FzfCharTables.CharsEqual((byte)'A', (byte)'a', caseSensitive: true));
    }
}
