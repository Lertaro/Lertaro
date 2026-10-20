using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.Tests.SearchIndex;

[TestClass]
public sealed class SearchNameOrderComparerTests
{
    private static int Compare(string left, string right) => SearchNameOrderComparer.Instance.Compare(left, right);

    [TestMethod]
    public void NumericRunsCompareByValue()
    {
        Assert.IsLessThan(0, Compare("a2", "a10"));
        Assert.IsGreaterThan(0, Compare("a10", "a2"));
    }

    [TestMethod]
    public void LongNumericRunsNeverOverflow()
    {
        // The old comparer called long.Parse here and threw OverflowException.
        Assert.IsLessThan(0, Compare("a12345678901234567890", "a12345678901234567891"));
        Assert.AreEqual(0, Compare("a00000000000000000001", "a1"));
    }

    [TestMethod]
    public void DigitsComeBeforeLetters()
    {
        Assert.IsLessThan(0, Compare("a1", "aa"));
    }

    [TestMethod]
    public void ShorterPrefixComesFirst()
    {
        Assert.IsLessThan(0, Compare("a", "aa"));
    }

    [TestMethod]
    public void UppercaseSortsBeforeLowercaseAtTheSameLetter()
    {
        Assert.IsLessThan(0, Compare("A", "a"));
    }
}
