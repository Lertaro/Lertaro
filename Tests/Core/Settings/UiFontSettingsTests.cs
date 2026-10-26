namespace Lertaro.Core.Tests.Settings;

[TestClass]
public sealed class UiFontSettingsTests
{
    [TestMethod]
    public void Normalize_NullOrEmpty_ReturnsEmptyChain() => Assert.IsEmpty(UiFontSettings.Normalize(null));

    [TestMethod]
    public void Normalize_TrimsAndDropsEmptyEntries()
        => CollectionAssert.AreEqual(
            new[] { "Microsoft YaHei", "Meiryo" },
            UiFontSettings.Normalize(["  Microsoft YaHei  ", "", "   ", "Meiryo"]));

    [TestMethod]
    public void Normalize_DeduplicatesCaseInsensitivelyInOriginalOrder()
        => CollectionAssert.AreEqual(
            new[] { "Microsoft YaHei", "meiryo" },
            UiFontSettings.Normalize(["Microsoft YaHei", "meiryo", "MICROSOFT YAHEI"]));

    [TestMethod]
    public void Normalize_KeepsAtMostThreeFamilies()
        => CollectionAssert.AreEqual(
            new[] { "One", "Two", "Three" },
            UiFontSettings.Normalize(["One", "Two", "Three", "Four", "Five"]));

    [TestMethod]
    public void Normalize_DropsNamesACompositeFontStringCannotCarry()
        => CollectionAssert.AreEqual(
            new[] { "Microsoft YaHei" },
            UiFontSettings.Normalize(["Bad,Name", "Line\nBreak", "Microsoft YaHei"]));

    [TestMethod]
    public void Normalize_PreservesCjkFamilyNames()
        => CollectionAssert.AreEqual(
            new[] { "微软雅黑", "メイリオ", "맑은 고딕" },
            UiFontSettings.Normalize(["微软雅黑", "メイリオ", "맑은 고딕"]));
}
