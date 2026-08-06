using Lertaro.Plugins.FolderCascader.Navigation;

namespace Lertaro.Plugins.FolderCascader.Tests;

[TestClass]
public sealed class MenuBuilderCategoryPathTests
{
    [TestMethod]
    public void SplitSubMenuPath_Empty_ReturnsEmptyArray() =>
        Assert.IsEmpty(MenuBuilder.SplitSubMenuPath(""));

    [TestMethod]
    public void SplitSubMenuPath_SingleSegment_ReturnsOneElement() =>
        CollectionAssert.AreEqual(new[] { "Tools" }, MenuBuilder.SplitSubMenuPath("Tools"));

    [TestMethod]
    public void SplitSubMenuPath_MultipleSegments_SplitsOnSlash() =>
        CollectionAssert.AreEqual(new[] { "Tools", "Network" }, MenuBuilder.SplitSubMenuPath("Tools/Network"));

    [TestMethod]
    public void SplitSubMenuPath_EmptySegmentsAndWhitespace_AreDropped() =>
        CollectionAssert.AreEqual(new[] { "Tools", "Network" }, MenuBuilder.SplitSubMenuPath(" Tools // Network /"));

    [TestMethod]
    public void StartsWithPrefix_EmptyPrefix_AlwaysMatches() =>
        Assert.IsTrue(MenuBuilder.StartsWithPrefix(new[] { "Tools", "Network" }, Array.Empty<string>()));

    [TestMethod]
    public void StartsWithPrefix_MatchingPrefix_ReturnsTrue() =>
        Assert.IsTrue(MenuBuilder.StartsWithPrefix(new[] { "Tools", "Network" }, new[] { "Tools" }));

    [TestMethod]
    public void StartsWithPrefix_NonMatchingPrefix_ReturnsFalse() =>
        Assert.IsFalse(MenuBuilder.StartsWithPrefix(new[] { "Tools", "Network" }, new[] { "Apps" }));

    [TestMethod]
    public void StartsWithPrefix_ShorterThanPrefix_ReturnsFalse() =>
        Assert.IsFalse(MenuBuilder.StartsWithPrefix(new[] { "Tools" }, new[] { "Tools", "Network" }));

    [TestMethod]
    public void StartsWithPrefix_IsCaseSensitive() =>
        Assert.IsFalse(MenuBuilder.StartsWithPrefix(new[] { "tools" }, new[] { "Tools" }));

    [TestMethod]
    public void EncodeThenDecodeCategoryPath_RoundTrips()
    {
        var encoded = MenuBuilder.EncodeCategoryPath(new[] { "Tools", "Network" });

        var decoded = MenuBuilder.TryDecodeCategoryPath(encoded, out var segments);

        Assert.IsTrue(decoded);
        CollectionAssert.AreEqual(new[] { "Tools", "Network" }, segments);
    }

    [TestMethod]
    public void TryDecodeCategoryPath_RealFilesystemPath_ReturnsFalse()
    {
        var decoded = MenuBuilder.TryDecodeCategoryPath(@"C:\some\path", out var segments);

        Assert.IsFalse(decoded);
        Assert.IsEmpty(segments);
    }
}
