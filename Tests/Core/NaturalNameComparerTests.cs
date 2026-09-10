namespace Lertaro.Core.Tests;

// Explorer's own file-name ordering, used only for the inline window's own folder.
[TestClass]
public sealed class NaturalNameComparerTests
{
    private static string[] Sorted(params string[] names) =>
        names.OrderBy(n => n, NaturalNameComparer.Instance).ToArray();

    // The reported case: Explorer shows these three in this order, and the inline Current Folder section
    // must read the same way.
    [TestMethod]
    public void SortsTheReportedCaseTheWayExplorerDoes() =>
        CollectionAssert.AreEqual(
            new[] { "Lertaro", "LRC maker", "lx-music" },
            Sorted("lx-music", "LRC maker", "Lertaro"));

    [TestMethod]
    public void DigitsCompareByValue()
    {
        // The whole point of a natural sort: numeric runs compare as numbers, not as characters -- a plain
        // Ordinal/culture compare puts "10" before "9".
        CollectionAssert.AreEqual(
            new[] { "file2.txt", "file9.txt", "file10.txt" },
            Sorted("file10.txt", "file2.txt", "file9.txt"));
    }

    [TestMethod]
    public void LargerNumericRunsStillCompareByValue() =>
        CollectionAssert.AreEqual(
            new[] { "img9.png", "img10.png", "img100.png" },
            Sorted("img100.png", "img9.png", "img10.png"));

    [TestMethod]
    public void IsCaseInsensitiveLikeTheShell()
    {
        // Explorer's ordering does not separate casing, so "a" and "A" interleave rather than one sorting
        // wholly before the other.
        var sorted = Sorted("b.txt", "A.txt", "a.txt");

        CollectionAssert.AreEquivalent(new[] { "A.txt", "a.txt", "b.txt" }, sorted);
        Assert.AreEqual("b.txt", sorted[2]);
    }

    [TestMethod]
    public void IdenticalNamesCompareEqual() => Assert.AreEqual(0, NaturalNameComparer.Instance.Compare("same.txt", "same.txt"));

    [TestMethod]
    public void NullHandling_SortsNullFirst()
    {
        Assert.IsLessThan(0, NaturalNameComparer.Instance.Compare(null, "a.txt"));
        Assert.IsGreaterThan(0, NaturalNameComparer.Instance.Compare("a.txt", null));
        Assert.AreEqual(0, NaturalNameComparer.Instance.Compare(null, null));
    }

    [TestMethod]
    public void SortingWithTheSameNames_IsStableAcrossRuns()
    {
        var first = Sorted("c.txt", "a.txt", "b.txt");
        var second = Sorted("c.txt", "a.txt", "b.txt");

        CollectionAssert.AreEqual(first, second);
    }
}
